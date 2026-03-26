// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Linq;
using System.Threading.Tasks;
using FSharp.Compiler.LanguageServer.IntegrationTests.InProcess;
using Microsoft.VisualStudio.Shell.Interop;
using Xunit;

namespace FSharp.Compiler.LanguageServer.IntegrationTests;

/// <summary>
/// Tests that F# LSP diagnostics appear in the VS Error List with correctly populated columns,
/// including the Project column which requires VSDiagnosticProjectInformation from the language server.
/// </summary>
public class ErrorListDiagnosticsTests : AbstractLspIntegrationTest
{
    private const string CodeWithError = """
        module Test

        let add (x: int) (y: int) = x + y
        let result = add 1 "hello"
        """;

    private const string CodeWithWarning = """
        module Test

        open System.IO

        let stream = MemoryStream()
        """;

    [IdeFact]
    public async Task LspDiagnostics_ErrorList_ProjectColumnPopulated()
    {
        var projectName = "TestProject";
        var template = WellKnownProjectTemplates.FSharpNetCoreClassLibrary;

        await SolutionExplorer.CreateSingleProjectSolutionAsync(projectName, template, TestToken);
        await SolutionExplorer.RestoreNuGetPackagesAsync(TestToken);
        await Editor.SetTextAsync(CodeWithError, TestToken);

        await ErrorList.ShowAllEntriesAsync(TestToken);
        var entries = await ErrorList.WaitForEntriesAsync(minCount: 1, cancellationToken: TestToken);

        Assert.NotEmpty(entries);
        foreach (var entry in entries)
        {
            Assert.False(
                string.IsNullOrEmpty(entry.ProjectName),
                $"Diagnostic '{entry.Description}' has an empty Project column. " +
                $"Expected project name containing '{projectName}'.");
        }
    }

    [IdeFact]
    public async Task LspDiagnostics_ErrorList_ProjectNameMatchesProjectFile()
    {
        var projectName = "TestProject";
        var template = WellKnownProjectTemplates.FSharpNetCoreClassLibrary;

        await SolutionExplorer.CreateSingleProjectSolutionAsync(projectName, template, TestToken);
        await SolutionExplorer.RestoreNuGetPackagesAsync(TestToken);
        await Editor.SetTextAsync(CodeWithError, TestToken);

        await ErrorList.ShowAllEntriesAsync(TestToken);
        var entries = await ErrorList.WaitForEntriesAsync(minCount: 1, cancellationToken: TestToken);

        Assert.NotEmpty(entries);
        foreach (var entry in entries)
        {
            Assert.True(
                entry.ProjectName?.Contains(projectName) == true,
                $"Diagnostic project name '{entry.ProjectName}' does not match " +
                $"expected project name '{projectName}'.");
        }
    }

    [IdeFact]
    public async Task LspDiagnostics_ErrorList_WarningHasProjectColumn()
    {
        var projectName = "TestProject";
        var template = WellKnownProjectTemplates.FSharpNetCoreClassLibrary;

        await SolutionExplorer.CreateSingleProjectSolutionAsync(projectName, template, TestToken);
        await SolutionExplorer.RestoreNuGetPackagesAsync(TestToken);
        await Editor.SetTextAsync(CodeWithWarning, TestToken);

        await ErrorList.ShowAllEntriesAsync(TestToken);
        var entries = await ErrorList.WaitForEntriesAsync(minCount: 1, cancellationToken: TestToken);

        var warnings = entries
            .Where(e => e.Severity == __VSERRORCATEGORY.EC_WARNING)
            .ToArray();

        Assert.NotEmpty(warnings);
        foreach (var warning in warnings)
        {
            Assert.False(
                string.IsNullOrEmpty(warning.ProjectName),
                $"Warning '{warning.Description}' has an empty Project column.");
        }
    }

    [IdeFact]
    public async Task LspDiagnostics_ErrorList_AllColumnsPopulated()
    {
        var projectName = "TestProject";
        var template = WellKnownProjectTemplates.FSharpNetCoreClassLibrary;

        await SolutionExplorer.CreateSingleProjectSolutionAsync(projectName, template, TestToken);
        await SolutionExplorer.RestoreNuGetPackagesAsync(TestToken);
        await Editor.SetTextAsync(CodeWithError, TestToken);

        await ErrorList.ShowAllEntriesAsync(TestToken);
        var entries = await ErrorList.WaitForEntriesAsync(minCount: 1, cancellationToken: TestToken);

        Assert.NotEmpty(entries);
        foreach (var entry in entries)
        {
            Assert.False(string.IsNullOrEmpty(entry.Description),
                "Diagnostic has empty Description.");

            Assert.False(string.IsNullOrEmpty(entry.ProjectName),
                $"Diagnostic '{entry.Description}' has empty Project column.");

            Assert.False(string.IsNullOrEmpty(entry.FileName),
                $"Diagnostic '{entry.Description}' has empty File column.");

            Assert.NotNull(entry.Line);

            Assert.False(string.IsNullOrEmpty(entry.ErrorCode),
                $"Diagnostic '{entry.Description}' has empty Error Code.");
        }
    }
}
