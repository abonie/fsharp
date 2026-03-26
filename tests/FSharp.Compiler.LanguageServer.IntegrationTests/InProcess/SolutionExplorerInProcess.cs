// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.OperationProgress;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;
using NuGet.SolutionRestoreManager;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.VisualStudio.Extensibility.Testing;

internal partial class SolutionExplorerInProcess
{
    public async Task CreateSingleProjectSolutionAsync(string name, string template, CancellationToken cancellationToken)
    {
        await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

        await CreateSolutionAsync(name, cancellationToken);
        await AddProjectAsync(name, template, cancellationToken);
    }

    public async Task CreateSolutionAsync(string solutionName, CancellationToken cancellationToken)
    {
        await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

        var solutionPath = CreateTemporaryPath();
        await CreateSolutionAsync(solutionPath, solutionName, cancellationToken);
    }

    private async Task CreateSolutionAsync(string solutionPath, string solutionName, CancellationToken cancellationToken)
    {
        await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

        await CloseSolutionAsync(cancellationToken);

        var solutionFileName = Path.ChangeExtension(solutionName, ".sln");
        Directory.CreateDirectory(solutionPath);

        var solution = await GetRequiredGlobalServiceAsync<SVsSolution, IVsSolution>(cancellationToken);
        ErrorHandler.ThrowOnFailure(solution.CreateSolution(solutionPath, solutionFileName, (uint)__VSCREATESOLUTIONFLAGS.CSF_SILENT));
        ErrorHandler.ThrowOnFailure(solution.SaveSolutionElement((uint)__VSSLNSAVEOPTIONS.SLNSAVEOPT_ForceSave, null, 0));
    }

    private async Task<string> GetDirectoryNameAsync(CancellationToken cancellationToken)
    {
        await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

        var solution = await GetRequiredGlobalServiceAsync<SVsSolution, IVsSolution>(cancellationToken);
        ErrorHandler.ThrowOnFailure(solution.GetSolutionInfo(out _, out var solutionFileFullPath, out _));
        if (string.IsNullOrEmpty(solutionFileFullPath))
        {
            throw new InvalidOperationException();
        }

        return Path.GetDirectoryName(solutionFileFullPath);
    }

    public async Task AddProjectAsync(string projectName, string projectTemplate, CancellationToken cancellationToken)
    {
        await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

        var projectPath = Path.Combine(await GetDirectoryNameAsync(cancellationToken), projectName);
        var projectTemplatePath = await GetProjectTemplatePathAsync(projectTemplate, cancellationToken);
        var solution = await GetRequiredGlobalServiceAsync<SVsSolution, IVsSolution6>(cancellationToken);
        ErrorHandler.ThrowOnFailure(solution.AddNewProjectFromTemplate(projectTemplatePath, null, null, projectPath, projectName, null, out _));
    }

    private async Task<string> GetProjectTemplatePathAsync(string projectTemplate, CancellationToken cancellationToken)
    {
        await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

        var dte = await GetRequiredGlobalServiceAsync<SDTE, EnvDTE.DTE>(cancellationToken);
        var solution = (EnvDTE80.Solution2)dte.Solution;

        return solution.GetProjectTemplate(projectTemplate, "FSharp");
    }

    public async Task RestoreNuGetPackagesAsync(CancellationToken cancellationToken)
    {
        await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

        var dte = await GetRequiredGlobalServiceAsync<SDTE, EnvDTE.DTE>(cancellationToken);
        var solution = (EnvDTE80.Solution2)dte.Solution;
        foreach (var project in solution.Projects.OfType<EnvDTE.Project>())
        {
            await RestoreNuGetPackagesAsync(project.FullName, cancellationToken);
        }
    }

    public async Task RestoreNuGetPackagesAsync(string projectName, CancellationToken cancellationToken)
    {
        await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

        var operationProgressStatus = await GetRequiredGlobalServiceAsync<SVsOperationProgress, IVsOperationProgressStatusService>(cancellationToken);
        var stageStatus = operationProgressStatus.GetStageStatus(CommonOperationProgressStageIds.Intellisense);
        await stageStatus.WaitForCompletionAsync();

        var solutionRestoreService = await GetComponentModelServiceAsync<IVsSolutionRestoreService>(cancellationToken);
        await solutionRestoreService.CurrentRestoreOperation;

        var projectFullPath = (await GetProjectAsync(projectName, cancellationToken)).FullName;
        var solutionRestoreStatusProvider = await GetComponentModelServiceAsync<IVsSolutionRestoreStatusProvider>(cancellationToken);
        if (await solutionRestoreStatusProvider.IsRestoreCompleteAsync(cancellationToken))
        {
            return;
        }

        var solutionRestoreService2 = (IVsSolutionRestoreService2)solutionRestoreService;
        await solutionRestoreService2.NominateProjectAsync(projectFullPath, cancellationToken);

        while (true)
        {
            if (await solutionRestoreStatusProvider.IsRestoreCompleteAsync(cancellationToken))
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken);
        }
    }

    public async Task OpenFileAsync(string projectName, string relativeFilePath, CancellationToken cancellationToken)
    {
        await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

        var filePath = await GetAbsolutePathForProjectRelativeFilePathAsync(projectName, relativeFilePath, cancellationToken);
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException(filePath);
        }

        VsShellUtilities.OpenDocument(ServiceProvider.GlobalProvider, filePath, VSConstants.LOGVIEWID.Code_guid, out _, out _, out _, out _);
    }

    private string CreateTemporaryPath()
    {
        return Path.Combine(Path.GetTempPath(), "fsharp-lsp-test", Path.GetRandomFileName());
    }

    private async Task<EnvDTE.Project> GetProjectAsync(string nameOrFileName, CancellationToken cancellationToken)
    {
        await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

        var dte = await GetRequiredGlobalServiceAsync<SDTE, EnvDTE.DTE>(cancellationToken);
        var solution = (EnvDTE80.Solution2)dte.Solution;
        return solution.Projects.OfType<EnvDTE.Project>().First(
            project =>
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                return string.Equals(project.FileName, nameOrFileName, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(project.Name, nameOrFileName, StringComparison.OrdinalIgnoreCase);
            });
    }

    private async Task<string> GetAbsolutePathForProjectRelativeFilePathAsync(string projectName, string relativeFilePath, CancellationToken cancellationToken)
    {
        await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

        var dte = await GetRequiredGlobalServiceAsync<SDTE, EnvDTE.DTE>(cancellationToken);
        var solution = dte.Solution;

        var project = solution.Projects.Cast<EnvDTE.Project>().First(x => x.Name == projectName);
        var projectPath = Path.GetDirectoryName(project.FullName);
        return Path.Combine(projectPath, relativeFilePath);
    }
}
