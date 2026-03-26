// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Extensibility.Testing;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Shell.TableControl;
using Microsoft.VisualStudio.Shell.TableManager;
using Task = System.Threading.Tasks.Task;

namespace FSharp.Compiler.LanguageServer.IntegrationTests.InProcess;

[TestService]
internal partial class ErrorListInProcess
{
    /// <summary>
    /// Configures the error list to show all entry types from all sources.
    /// </summary>
    public async Task ShowAllEntriesAsync(CancellationToken cancellationToken)
    {
        await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

        var errorList = await GetRequiredGlobalServiceAsync<SVsErrorList, IErrorList>(cancellationToken);
        errorList.AreBuildErrorSourceEntriesShown = true;
        errorList.AreOtherErrorSourceEntriesShown = true;
        errorList.AreErrorsShown = true;
        errorList.AreWarningsShown = true;
        errorList.AreMessagesShown = false;
    }

    /// <summary>
    /// Returns all currently visible error list entries with structured column data.
    /// </summary>
    public async Task<ImmutableArray<ErrorListEntry>> GetAllEntriesAsync(CancellationToken cancellationToken)
    {
        await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

        var errorItems = await GetErrorItemsAsync(cancellationToken);
        var list = new List<ErrorListEntry>();

        foreach (var item in errorItems)
        {
            list.Add(new ErrorListEntry
            {
                Severity = item.GetCategory(),
                Description = item.GetText(),
                ProjectName = item.GetProjectName(),
                FileName = Path.GetFileName(item.GetPath() ?? item.GetDocumentName()),
                Line = item.GetLine(),
                Column = item.GetColumn(),
                ErrorCode = item.GetErrorCode(),
                BuildTool = item.GetBuildTool(),
            });
        }

        return list.ToImmutableArray();
    }

    /// <summary>
    /// Waits for at least <paramref name="minCount"/> error list entries to appear,
    /// polling with retries until the specified timeout.
    /// </summary>
    public async Task<ImmutableArray<ErrorListEntry>> WaitForEntriesAsync(
        int minCount,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        timeout ??= TimeSpan.FromSeconds(120);
        var sw = Stopwatch.StartNew();

        while (sw.Elapsed < timeout)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var entries = await GetAllEntriesAsync(cancellationToken);
            if (entries.Length >= minCount)
            {
                return entries;
            }

            await Task.Delay(2000, cancellationToken);
        }

        // Final attempt
        return await GetAllEntriesAsync(cancellationToken);
    }

    public async Task<int> GetErrorCountAsync(CancellationToken cancellationToken)
    {
        return await GetErrorCountAsync(__VSERRORCATEGORY.EC_WARNING, cancellationToken);
    }

    public async Task<int> GetErrorCountAsync(__VSERRORCATEGORY minimumSeverity, CancellationToken cancellationToken)
    {
        await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

        var errorItems = await GetErrorItemsAsync(cancellationToken);
        return errorItems.Count(e => e.GetCategory() <= minimumSeverity);
    }

    private async Task<ImmutableArray<ITableEntryHandle>> GetErrorItemsAsync(CancellationToken cancellationToken)
    {
        await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

        var errorList = await GetRequiredGlobalServiceAsync<SVsErrorList, IErrorList>(cancellationToken);
        var args = await errorList.TableControl.ForceUpdateAsync();
        return args.AllEntries.ToImmutableArray();
    }
}

/// <summary>
/// Structured representation of a VS Error List entry.
/// </summary>
internal sealed class ErrorListEntry
{
    public __VSERRORCATEGORY Severity { get; set; }
    public string? Description { get; set; }
    public string? ProjectName { get; set; }
    public string? FileName { get; set; }
    public int? Line { get; set; }
    public int? Column { get; set; }
    public string? ErrorCode { get; set; }
    public string? BuildTool { get; set; }
}
