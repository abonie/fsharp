// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace FSharp.VisualStudio.Extension;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipelines;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FSharp.Compiler.CodeAnalysis.Workspace;
using FSharp.Compiler.Diagnostics;
using FSharp.Compiler.LanguageServer;
using FSharp.Compiler.LanguageServer.Common;

using Microsoft.CommonLanguageServerProtocol.Framework;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FSharp.Control;
using Microsoft.FSharp.Core;
using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Editor;
using Microsoft.VisualStudio.Extensibility.LanguageServer;
using Microsoft.VisualStudio.Extensibility.Settings;
using Microsoft.VisualStudio.LanguageServer.Protocol;
using Microsoft.VisualStudio.ProjectSystem.Query;
using Microsoft.VisualStudio.RpcContracts.LanguageServerProvider;
using Nerdbank.Streams;

/// <inheritdoc/>
#pragma warning disable VSEXTPREVIEW_LSP // Type is for evaluation purposes only and is subject to change or removal in future updates.

#pragma warning disable VSEXTPREVIEW_PROJECTQUERY_PROPERTIES_BUILDPROPERTIES // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.


internal static class Extensions
{
    public static List<IQueryResultItem<T>> Please<T>(this IAsyncQueryable<T> x) => x.QueryAsync(CancellationToken.None).ToBlockingEnumerable().ToList();

    public static async Task<T> StartAsTaskAsync<T>(this FSharpAsync<T> async, CancellationToken cancellationToken)
    {
        return await FSharpAsync.StartAsTask(async, FSharpOption<TaskCreationOptions>.None, FSharpOption<CancellationToken>.Some(cancellationToken));
    }

    /// <summary>
    /// Converts a FSharpDiagnostic to VSDiagnostic with project information.
    /// Similar to ToLspDiagnostic but populates VS-specific fields including project context.
    /// </summary>
    public static VSDiagnostic ToLspVsDiagnostic(this FSharpDiagnostic diagnostic, VSDiagnosticProjectInformation[]? projectInfoArray)
    {
        DiagnosticSeverity severity;
        if (diagnostic.Severity == FSharpDiagnosticSeverity.Error)
            severity = DiagnosticSeverity.Error;
        else if (diagnostic.Severity == FSharpDiagnosticSeverity.Warning)
            severity = DiagnosticSeverity.Warning;
        else if (diagnostic.Severity == FSharpDiagnosticSeverity.Info)
            severity = DiagnosticSeverity.Information;
        else if (diagnostic.Severity == FSharpDiagnosticSeverity.Hidden)
            severity = DiagnosticSeverity.Hint;
        else
            severity = DiagnosticSeverity.Error;

        var range = new Microsoft.VisualStudio.LanguageServer.Protocol.Range
        {
            Start = new Position
            {
                Line = diagnostic.Range.StartLine - 1,
                Character = diagnostic.Range.StartColumn
            },
            End = new Position
            {
                Line = diagnostic.Range.EndLine - 1,
                Character = diagnostic.Range.EndColumn
            }
        };

        return new VSDiagnostic
        {
            Range = range,
            Severity = severity,
            Message = $"LSP: {diagnostic.Message}",
            Code = new SumType<int, string>(diagnostic.ErrorNumberText),
            Projects = projectInfoArray
        };
    }
}

/// <summary>
/// Represents information about a project context for a file.
/// Used to track which projects contain which source files for linked file support.
/// </summary>
internal sealed class ProjectContextInfo
{
    public required string ProjectPath { get; init; }
    public required string ProjectName { get; init; }
    public required string? OutputPath { get; init; }
    public string? ProjectGuid { get; init; }
    public string? ConfigurationName { get; init; }

    /// <summary>
    /// Converts this project context info to the LSP protocol format.
    /// Follows Roslyn's format: {ContextGuid}|{ProjectPath} ({ProjectGuid})
    /// </summary>
    public VSProjectContext ToVSProjectContext()
    {
        // Generate a context GUID based on configuration
        // For now, use a deterministic GUID based on project path and configuration
        var contextGuid = GenerateContextGuid();

        // Format the project GUID with braces if available
        var projectGuidPart = !string.IsNullOrEmpty(ProjectGuid)
            ? $" ({{{ProjectGuid}}})"
            : string.Empty;

        // Format: {ContextGuid}|{ProjectPath} ({ProjectGuid})
        // Example from Roslyn: 3c0b3e3f-116c-4569-926a-1dc65d429b70|Q:\source\ConsoleApp1\ConsoleApp1\ConsoleApp1.csproj ({5B68813F-23C2-8DF2-1B1F-9033A2893F62})
        var id = $"{contextGuid}|{ProjectPath}{projectGuidPart}";

        return new VSProjectContext
        {
            Id = id,
            Label = ProjectName,
            Kind = VSProjectKind.FSharp
        };
    }

    /// <summary>
    /// Generates a context GUID for this project configuration.
    /// Uses a deterministic hash of project path and configuration to ensure consistency.
    /// </summary>
    private string GenerateContextGuid()
    {
        // Use the configuration name if available, otherwise default
        var configPart = ConfigurationName ?? "Default";
        var input = $"{ProjectPath}|{configPart}";

        // Create a deterministic GUID based on the input string
        using var md5 = System.Security.Cryptography.MD5.Create();
        var hash = md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(input));
        var guid = new Guid(hash);

        return guid.ToString();
    }
}

internal class VsServerCapabilitiesOverride : IServerCapabilitiesOverride
{
    public ServerCapabilities OverrideServerCapabilities(FSharpLanguageServerConfig config, ServerCapabilities value, ClientCapabilities clientCapabilities)
    {
        var capabilities = new VSInternalServerCapabilities
        {
            TextDocumentSync = value.TextDocumentSync,
            SupportsDiagnosticRequests = config.EnabledFeatures.Diagnostics,
            ProjectContextProvider = true,
            DiagnosticProvider =
                config.EnabledFeatures.Diagnostics ?

            new()
            {
                SupportsMultipleContextsDiagnostics = false,
                DiagnosticKinds = [
                        // Support a specialized requests dedicated to task-list items.  This way the client can ask just
                        // for these, independently of other diagnostics.  They can also throttle themselves to not ask if
                        // the task list would not be visible.
                        //VSInternalDiagnosticKind.Task,
                        // Dedicated request for workspace-diagnostics only.  We will only respond to these if FSA is on.
                        VSInternalDiagnosticKind.Syntax,
                        // Fine-grained diagnostics requests.  Importantly, this separates out syntactic vs semantic
                        // requests, allowing the former to quickly reach the user without blocking on the latter.  In a
                        // similar vein, compiler diagnostics are explicitly distinct from analyzer-diagnostics, allowing
                        // the former to appear as soon as possible as they are much more critical for the user and should
                        // not be delayed by a slow analyzer.
                        //new("Semantic"),
                        //new(PullDiagnosticCategories.DocumentAnalyzerSyntax),
                        //new(PullDiagnosticCategories.DocumentAnalyzerSemantic),
                    ]
            } : null,
            //HoverProvider = new HoverOptions()
            //{
            //    WorkDoneProgress = true
            //}
        };
        return capabilities;
    }
}

internal class VsDiagnosticsHandler
    : IRequestHandler<VSInternalDiagnosticParams, VSInternalDiagnosticReport[], FSharpRequestContext>,
      IRequestHandler<VSGetProjectContextsParams, VSProjectContextList, FSharpRequestContext>
{
    private readonly Func<ProjectObserver?> getProjectObserver;

    public VsDiagnosticsHandler(Func<ProjectObserver?> getProjectObserver)
    {
        this.getProjectObserver = getProjectObserver;
    }

    public bool MutatesSolutionState => false;

    [LanguageServerEndpoint(VSInternalMethods.DocumentPullDiagnosticName, LanguageServerConstants.DefaultLanguageName)]
    public async Task<VSInternalDiagnosticReport[]> HandleRequestAsync(VSInternalDiagnosticParams request, FSharpRequestContext context, CancellationToken cancellationToken)
    {
        var diagnosticsAsync = context.Workspace.Query.GetDiagnosticsForFile(request!.TextDocument!.Uri);
        var report = await FSharpAsync.StartAsTask(diagnosticsAsync, FSharpOption<TaskCreationOptions>.None, FSharpOption<CancellationToken>.Some(cancellationToken));

        // Get project contexts for the file
        VSDiagnosticProjectInformation[]? projectInfoArray = null;
        var observer = getProjectObserver();
        if (observer != null)
        {
            try
            {
                var uri = request.TextDocument.Uri;
                var filePath = uri.LocalPath;
                var contextInfos = observer.GetProjectContextsForFile(filePath).ToArray();

                if (contextInfos.Length > 0)
                {
                    projectInfoArray = contextInfos
                        .Select(info => new VSDiagnosticProjectInformation
                        {
                            ProjectIdentifier = info.ProjectPath,
                            ProjectName = info.ProjectName,
                            Context = info.ToVSProjectContext().Id
                        })
                        .ToArray();
                }
            }
            catch (Exception ex)
            {
                Trace.TraceError($"Error getting project contexts for diagnostics: {ex}");
            }
        }

        var vsReport = new VSInternalDiagnosticReport
        {
            ResultId = report.ResultId,
            //Identifier = 1,
            //Version = 1,
            Diagnostics = [.. report.Diagnostics.Select(d => d.ToLspVsDiagnostic(projectInfoArray))]
        };

        return [vsReport];
    }

    [LanguageServerEndpoint("textDocument/_vs_getProjectContexts", LanguageServerConstants.DefaultLanguageName)]
    public Task<VSProjectContextList> HandleRequestAsync(VSGetProjectContextsParams request, FSharpRequestContext context, CancellationToken cancellationToken)
    {
        try
        {
            // Get file path from request URI
            var uri = request.TextDocument.Uri;
            var filePath = uri.LocalPath;

            Trace.TraceInformation($"GetProjectContexts requested for: {filePath}");

            // Get project observer
            var observer = getProjectObserver();
            if (observer == null)
            {
                Trace.TraceWarning("ProjectObserver not available for GetProjectContexts request");
                return Task.FromResult<VSProjectContextList>(null!);
            }

            // Get all projects containing this file
            var contextInfos = observer.GetProjectContextsForFile(filePath).ToArray();

            if (contextInfos.Length == 0)
            {
                Trace.TraceInformation($"No projects found for file: {filePath}");
                return Task.FromResult<VSProjectContextList>(null!);
            }

            Trace.TraceInformation($"Found {contextInfos.Length} project(s) for file: {filePath}");

            // Convert to VS protocol format
            var contexts = contextInfos
                .Select(info => info.ToVSProjectContext())
                .ToArray();

            // For now, use first project as default
            // Future enhancement: track active project or use CPS active configuration
            var defaultIndex = 0;

            var result = new VSProjectContextList
            {
                ProjectContexts = contexts,
                DefaultIndex = defaultIndex
            };

            return Task.FromResult(result);
        }
        catch (Exception ex)
        {
            Trace.TraceError($"Error in GetProjectContexts: {ex}");
            return Task.FromResult<VSProjectContextList>(null!);
        }
    }
}


internal class SolutionObserver : IObserver<IQueryResults<ISolutionSnapshot>>
{
    public void OnCompleted()
    {

    }

    public void OnError(Exception error)
    {
    }

    public void OnNext(IQueryResults<ISolutionSnapshot> value)
    {
        Trace.TraceInformation("Solution was updated");
    }

}

internal class ProjectObserver(FSharpWorkspace workspace) : IObserver<IQueryResults<IProjectSnapshot>>
{
    private readonly FSharpWorkspace workspace = workspace;

    /// <summary>
    /// Maps file paths to the list of projects that contain them.
    /// Enables lookup of all projects containing a file (for linked files).
    /// </summary>
    private readonly ConcurrentDictionary<string, List<ProjectContextInfo>> fileToProjects = new();

    /// <summary>
    /// Maps project paths to their context information for quick lookup.
    /// </summary>
    private readonly ConcurrentDictionary<string, ProjectContextInfo> projectPathToInfo = new();

    internal void ProcessProject(IProjectSnapshot project)
    {
        project.Id.TryGetValue("ProjectPath", out var projectPath);

        List<(string, string)> projectInfos = [];

        if (projectPath != null && projectPath.ToLower().EndsWith(".fsproj"))
        {
            var configs = project.ActiveConfigurations.ToList();

            // Extract project GUID if available
            string? projectGuid = null;
            project.Id.TryGetValue("Guid", out projectGuid);

            foreach (var config in configs)
            {
                if (config != null)
                {
                    // Extract configuration name (e.g., "Debug|AnyCPU")
                    var configDimensions = config.ConfigurationDimensions?.ToList();
                    var configName = configDimensions != null && configDimensions.Count > 0
                        ? string.Join("|", configDimensions.Select(d => d.Value))
                        : null;

                    // Extract bin output path for each active config
                    var data = config.OutputGroups;

                    string? outputPath = null;
                    foreach (var group in data)
                    {
                        if (group.Name == "Built")
                        {
                            foreach (var output in group.Outputs)
                            {
                                if (output.FinalOutputPath != null && (output.FinalOutputPath.ToLower().EndsWith(".dll") || output.FinalOutputPath.ToLower().EndsWith(".exe")))
                                {
                                    outputPath = output.FinalOutputPath;
                                    break;
                                }
                            }
                            if (outputPath != null)
                            {
                                break;
                            }
                        }
                    }

                    foreach (var ruleResults in config.RuleResults)
                    {
                        // XXX Idk why `.Where` does not work with these IAsyncQueryable type
                        if (ruleResults?.RuleName == "CompilerCommandLineArgs")
                        {
                            // XXX Not sure why there would be more than one item for this rule result
                            // Taking first one, ignoring the rest
                            var args = ruleResults?.Items?.FirstOrDefault()?.Name;
                            if (args != null && outputPath != null) projectInfos.Add((outputPath, args));
                        }
                    }

                    // Create and store project context info for file tracking
                    if (outputPath != null)
                    {
                        var projectName = Path.GetFileNameWithoutExtension(projectPath);
                        var contextInfo = new ProjectContextInfo
                        {
                            ProjectPath = projectPath,
                            ProjectName = projectName,
                            OutputPath = outputPath,
                            ProjectGuid = projectGuid,
                            ConfigurationName = configName
                        };

                        projectPathToInfo.AddOrUpdate(
                            projectPath,
                            contextInfo,
                            (_, _) => contextInfo);

                        // Track source files for this project
                        TrackSourceFiles(project, contextInfo);
                    }
                }
            }

            foreach (var projectInfo in projectInfos)
            {
                workspace.Projects.AddOrUpdate(projectPath, projectInfo.Item1, projectInfo.Item2.Split(';'));
            }

            //var graphPath = Path.Combine(Path.GetDirectoryName(projectPath) ?? ".", "..", "depGraph.md");

            //workspace.projects.Debug_DumpGraphOnEveryChange = FSharpOption<string>.Some(graphPath);

            //Trace.TraceInformation($"Auto-saving workspace graph to {graphPath}");

        }
    }

    /// <summary>
    /// Tracks source files from the project snapshot, building a mapping of files to projects.
    /// This enables fast lookup for the GetProjectContexts endpoint.
    /// </summary>
    private void TrackSourceFiles(IProjectSnapshot project, ProjectContextInfo contextInfo)
    {
        try
        {
            var sourceFiles = project.Files;
            if (sourceFiles == null)
            {
                Trace.TraceInformation($"No files found for project {contextInfo.ProjectName}");
                return;
            }

            var fileCount = 0;
            foreach (var file in sourceFiles)
            {
                // Only track Compile items (.fs, .fsi files that are compiled)
                // Exclude Content, None, Resource, etc.
                if (file.ItemType != null &&
                    (file.ItemType.Equals("Compile", StringComparison.OrdinalIgnoreCase) ||
                     file.ItemType.Equals("CompileBefore", StringComparison.OrdinalIgnoreCase) ||
                     file.ItemType.Equals("CompileAfter", StringComparison.OrdinalIgnoreCase)))
                {
                    var filePath = file.Path;
                    if (string.IsNullOrEmpty(filePath)) continue;

                    // Normalize path to handle case sensitivity and path separators
                    try
                    {
                        filePath = Path.GetFullPath(filePath);
                    }
                    catch (Exception ex)
                    {
                        Trace.TraceWarning($"Failed to normalize path '{filePath}': {ex.Message}");
                        continue;
                    }

                    fileToProjects.AddOrUpdate(
                        filePath,
                        _ => new List<ProjectContextInfo> { contextInfo },
                        (_, existingList) =>
                        {
                            // Avoid duplicates - check if this project is already tracked for this file
                            if (!existingList.Any(p => p.ProjectPath.Equals(contextInfo.ProjectPath, StringComparison.OrdinalIgnoreCase)))
                            {
                                existingList.Add(contextInfo);
                            }
                            return existingList;
                        });

                    fileCount++;
                }
            }

            Trace.TraceInformation($"Tracked {fileCount} source files for project {contextInfo.ProjectName}");
        }
        catch (Exception ex)
        {
            Trace.TraceError($"Error tracking source files for project {contextInfo.ProjectName}: {ex}");
        }
    }

    /// <summary>
    /// Gets all project contexts for a given file path.
    /// Returns all projects that contain this file (for linked file support).
    /// </summary>
    internal IEnumerable<ProjectContextInfo> GetProjectContextsForFile(string filePath)
    {
        if (string.IsNullOrEmpty(filePath))
        {
            return Enumerable.Empty<ProjectContextInfo>();
        }

        // Normalize path to match how we stored it
        try
        {
            filePath = Path.GetFullPath(filePath);
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"Failed to normalize path '{filePath}' for lookup: {ex.Message}");
            return Enumerable.Empty<ProjectContextInfo>();
        }

        var contexts = fileToProjects.TryGetValue(filePath, out var ctxs)
            ? ctxs
            : Enumerable.Empty<ProjectContextInfo>();

        Trace.TraceInformation($"Found {contexts.Count()} project(s) for file {Path.GetFileName(filePath)}");
        return contexts;
    }

    /// <summary>
    /// Gets the total number of files being tracked. Useful for diagnostics.
    /// </summary>
    internal int GetTrackedFileCount() => fileToProjects.Count;

    /// <summary>
    /// Gets the total number of projects being tracked. Useful for diagnostics.
    /// </summary>
    internal int GetTrackedProjectCount() => projectPathToInfo.Count;

    public void OnNext(IQueryResults<IProjectSnapshot> result)
    {
        foreach (var project in result)
        {
            this.ProcessProject(project);
        }
    }

    public void OnCompleted()
    {
    }

    public void OnError(Exception error)
    {
    }
}


[VisualStudioContribution]
internal class FSharpLanguageServerProvider : LanguageServerProvider
{
    /// <summary>
    /// Stores reference to the project observer for use by handlers.
    /// </summary>
    private ProjectObserver? projectObserver;

    /// <summary>
    /// Gets the document type for FSharp code files.
    /// </summary>
    [VisualStudioContribution]
    public static DocumentTypeConfiguration FSharpDocumentType => new("F#")
    {
        FileExtensions = [".fs", ".fsi", ".fsx"],
        BaseDocumentType = LanguageServerBaseDocumentType,
    };

    /// <inheritdoc/>
    public override LanguageServerProviderConfiguration LanguageServerProviderConfiguration => new(
        "%FSharpLspExtension.FSharpLanguageServerProvider.DisplayName%",
        [Microsoft.VisualStudio.Extensibility.DocumentFilter.FromDocumentType(FSharpDocumentType)]);

    /// <inheritdoc/>
    public override async Task<IDuplexPipe?> CreateServerConnectionAsync(CancellationToken cancellationToken)
    {
        var activitySourceName = "fsc";

        FSharp.Compiler.LanguageServer.Activity.listenToSome();

#pragma warning disable VSEXTPREVIEW_SETTINGS // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.

        // Write default settings unless they're overridden. Otherwise users can't even find which settings exist.

        var settingsReadResult = await this.Extensibility.Settings().ReadEffectiveValuesAsync(FSharpExtensionSettings.AllStringSettings, cancellationToken);

        var settingValues = FSharpExtensionSettings.AllStringSettings.Select(
            setting => (setting, settingsReadResult.ValueOrDefault(setting, defaultValue: FSharpExtensionSettings.UNSET)));

        foreach (var (setting, value) in settingValues.Where(x => x.Item2 == FSharpExtensionSettings.UNSET))
        {
            await this.Extensibility.Settings().WriteAsync(batch =>
                batch.WriteSetting(setting, FSharpExtensionSettings.BOTH), "write default settings", cancellationToken);
        }

        var enabled = new[] { FSharpExtensionSettings.LSP, FSharpExtensionSettings.BOTH };

        var serverConfig = new FSharpLanguageServerConfig(
            new FSharpLanguageServerFeatures(
                diagnostics: enabled.Contains(settingsReadResult.ValueOrDefault(FSharpExtensionSettings.GetDiagnosticsFrom, defaultValue: FSharpExtensionSettings.BOTH))
                ));

        var disposeToEndSubscription =
            this.Extensibility.Settings().SubscribeAsync(
                [FSharpExtensionSettings.FSharpCategory],
                cancellationToken,
                changeHandler: result =>
                {
                    Trace.TraceInformation($"Settings update", result);
                });

#pragma warning restore VSEXTPREVIEW_SETTINGS // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.


        //const string vsMajorVersion = "17.0";

        //var settings = OpenTelemetryExporterSettingsBuilder
        //    .CreateVSDefault(vsMajorVersion)
        //    .Build();

        //try
        //{
        //    var tracerProvider = Sdk.CreateTracerProviderBuilder()
        //            .AddVisualStudioDefaultTraceExporter(settings)
        //            //.AddConsoleExporter()
        //            .AddOtlpExporter()
        //            .Build();
        //}
        //catch (Exception e)
        //{
        //    Trace.TraceError($"Failed to create OpenTelemetry tracer provider: {e}");
        //}


        var activitySource = new ActivitySource(activitySourceName);
        var activity = activitySource.CreateActivity("CreateServerConnectionAsync", ActivityKind.Internal);

        if (activity != null)
        {
            activity.Start();
        }
        else
        {
            Trace.TraceWarning("Failed to start OpenTelemetry activity, there are no listeners");
        }

        var ws = this.Extensibility.Workspaces();

        var projectQuery = (IAsyncQueryable<IProjectSnapshot> project) => project
            .With(p => p.ActiveConfigurations
                .With(c => c.ConfigurationDimensions.With(d => d.Name).With(d => d.Value))
                .With(c => c.Properties.With(p => p.Name).With(p => p.Value))
                .With(c => c.OutputGroups.With(g => g.Name).With(g => g.Outputs.With(o => o.Name).With(o => o.FinalOutputPath).With(o => o.RootRelativeURL)))
                .With(c => c.RuleResultsByRuleName("CompilerCommandLineArgs")
                    .With(r => r.RuleName)
                    .With(r => r.Items)))
            .With(p => p.ProjectReferences
                .With(r => r.ReferencedProjectPath)
                .With(r => r.CanonicalName)
                .With(r => r.Id)
                .With(r => r.Name)
                .With(r => r.ProjectGuid)
                .With(r => r.ReferencedProjectId)
                .With(r => r.ReferenceType))
            .With(p => p.Files
                .With(f => f.Path)
                .With(f => f.ItemType));

        IQueryResults<IProjectSnapshot>? result = await ws.QueryProjectsAsync(p => projectQuery(p).With(p => new { p.ActiveConfigurations, p.Id, p.Guid }), cancellationToken);

        var workspace = new FSharpWorkspace();

        // Store observer reference for use by handlers
        ProjectObserver? observer = null;

        foreach (var project in result)
        {
            observer = new ProjectObserver(workspace);

            await projectQuery(project.AsQueryable()).SubscribeAsync(observer, CancellationToken.None);

            // TODO: should we do this, or are we guaranteed it will get processed?
            // observer.ProcessProject(project);
        }

        // Store the observer for handler access
        this.projectObserver = observer;

        // Log diagnostic info about tracked files and projects
        if (observer != null)
        {
            Trace.TraceInformation(
                $"F# LSP: Tracked {observer.GetTrackedFileCount()} files across {observer.GetTrackedProjectCount()} projects");
        }

        var ((inputStream, outputStream), _server) = FSharpLanguageServer.Create(workspace, serverConfig, (serviceCollection) =>
        {
            serviceCollection.AddSingleton<IServerCapabilitiesOverride, VsServerCapabilitiesOverride>();
            serviceCollection.AddSingleton<IMethodHandler>(sp =>
                new VsDiagnosticsHandler(() => this.projectObserver));
        });

        var solutions = await ws.QuerySolutionAsync(
    solution => solution.With(solution => solution.FileName),
    cancellationToken);

        var singleSolution = solutions.FirstOrDefault();

        if (singleSolution != null)
        {
            var unsubscriber = await singleSolution
                .AsQueryable()
                .With(p => p.Projects.With(p => p.Files))
                .SubscribeAsync(new SolutionObserver(), CancellationToken.None);
        }

        return new DuplexPipe(
            PipeReader.Create(inputStream),
            PipeWriter.Create(outputStream));
    }

    /// <inheritdoc/>
    public override Task OnServerInitializationResultAsync(ServerInitializationResult serverInitializationResult, LanguageServerInitializationFailureInfo? initializationFailureInfo, CancellationToken cancellationToken)
    {
        if (serverInitializationResult == ServerInitializationResult.Failed)
        {
            // Log telemetry for failure and disable the server from being activated again.
            this.Enabled = false;
        }

        return base.OnServerInitializationResultAsync(serverInitializationResult, initializationFailureInfo, cancellationToken);
    }
}
#pragma warning restore VSEXTPREVIEW_LSP // Type is for evaluation purposes only and is subject to change or removal in future updates.
