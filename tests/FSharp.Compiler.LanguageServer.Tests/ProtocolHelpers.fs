module LanguageServer.ProtocolHelpers

open System
open System.Text.Json
open Xunit

open FSharp.Compiler.LanguageServer
open StreamJsonRpc
open System.IO
open System.Diagnostics

open Microsoft.VisualStudio.LanguageServer.Protocol
open Nerdbank.Streams

open FSharp.Test.ProjectGeneration.WorkspaceHelpers
open FSharp.Compiler.CodeAnalysis.Workspace
open FSharp.Compiler.CodeAnalysis.ProjectSnapshot

#nowarn "57"

type TestRpcClient(jsonRpc, rpcTrace, workspace, initializeResult: InitializeResult) =

    member val JsonRpc = jsonRpc
    member val RpcTraceWriter = rpcTrace
    member val Workspace = workspace
    member val Capabilities = initializeResult.Capabilities

    member _.RpcTrace = rpcTrace.ToString()

let createLanguageServer (workspace: FSharpWorkspace) (config: FSharpLanguageServerConfig option) =
    let rpcTrace = new StringWriter()

    let (inputStream, outputStream), server =
        match config with
        | Some cfg -> FSharpLanguageServer.Create(LspLogger Trace.TraceInformation, workspace, config = cfg)
        | None -> FSharpLanguageServer.Create(workspace)

    let formatter = new SystemTextJsonFormatter()
    addVSExtensionJsonConverters formatter.JsonSerializerOptions

    let messageHandler =
        new HeaderDelimitedMessageHandler(inputStream, outputStream, formatter)

    let jsonRpc = new JsonRpc(messageHandler)

    let listener = new TextWriterTraceListener(rpcTrace)
    server.JsonRpc.TraceSource.Listeners.Add(listener) |> ignore
    server.JsonRpc.TraceSource.Switch.Level <- SourceLevels.All

    let initializeParams =
        InitializeParams(
            ProcessId = Process.GetCurrentProcess().Id,
            RootUri = Uri("file:///c:/temp"),
            InitializationOptions = None
        )

    jsonRpc.StartListening()

    task {
        let! response = jsonRpc.InvokeWithParameterObjectAsync<InitializeResult>("initialize", initializeParams)
        return TestRpcClient(jsonRpc, rpcTrace, workspace, response)
    }

let initializeLanguageServer (workspace) =
    let workspace = defaultArg workspace (FSharpWorkspace())
    createLanguageServer workspace None

[<Literal>]
let cleanCode = "let x = 1"

[<Literal>]
let notMutableCode = "let x = 1\nx <- 2"

let openDocument (client: TestRpcClient) (fileUri: Uri) (content: string) (version: int) =
    client.JsonRpc.NotifyWithParameterObjectAsync(
        Methods.TextDocumentDidOpenName,
        DidOpenTextDocumentParams(
            TextDocument = TextDocumentItem(Uri = fileUri, LanguageId = "F#", Version = version, Text = content)))

let changeDocument (client: TestRpcClient) (fileUri: Uri) (content: string) (version: int) =
    client.JsonRpc.NotifyWithParameterObjectAsync(
        Methods.TextDocumentDidChangeName,
        DidChangeTextDocumentParams(
            TextDocument = VersionedTextDocumentIdentifier(Uri = fileUri, Version = version),
            ContentChanges = [| TextDocumentContentChangeEvent(Text = content) |]))

let closeDocument (client: TestRpcClient) (fileUri: Uri) =
    client.JsonRpc.NotifyWithParameterObjectAsync(
        Methods.TextDocumentDidCloseName,
        DidCloseTextDocumentParams(TextDocument = TextDocumentIdentifier(Uri = fileUri)))

let pullDiagnosticResponse (client: TestRpcClient) (fileUri: Uri) =
    client.JsonRpc.InvokeWithParameterObjectAsync<SumType<RelatedFullDocumentDiagnosticReport, RelatedUnchangedDocumentDiagnosticReport>>(
        Methods.TextDocumentDiagnosticName,
        DocumentDiagnosticParams(TextDocument = TextDocumentIdentifier(Uri = fileUri)))

let pullDiagnostics (client: TestRpcClient) (fileUri: Uri) =
    task {
        let! response = pullDiagnosticResponse client fileUri
        return response.First
    }

let setupSingleFileProject (client: TestRpcClient) (content: string) =
    let fileOnDisk = sourceFileOnDisk content
    let _pid = client.Workspace.Projects.AddOrUpdate(ProjectConfig.Create(), [ fileOnDisk.LocalPath ])
    fileOnDisk

let openAndPullDiagnostics (client: TestRpcClient) (fileUri: Uri) (content: string) =
    task {
        do! openDocument client fileUri content 1
        let! report = pullDiagnostics client fileUri
        return report.Items
    }

let pullVsDiagnosticsRaw(client: TestRpcClient) (fileUri: Uri) =
    client.JsonRpc.InvokeWithParameterObjectAsync<JsonElement>(
        Methods.TextDocumentDiagnosticName,
        DocumentDiagnosticParams(TextDocument = TextDocumentIdentifier(Uri = fileUri)))

let openAndPullVsDiagnosticsRaw (client: TestRpcClient) (fileUri: Uri) (content: string) =
    task {
        do! openDocument client fileUri content 1
        return! pullVsDiagnosticsRaw client fileUri
    }

let getVsDiagnosticItems (response: JsonElement) =
    let items = response.GetProperty("items")
    Assert.True(items.ValueKind = JsonValueKind.Array, "Expected 'items' property in diagnostic response")
    items.EnumerateArray() |> ResizeArray

let getVsProjects (diagnosticItem: JsonElement) =
    let projects = diagnosticItem.GetProperty("_vs_projects")
    Assert.True(projects.ValueKind = JsonValueKind.Array, "Expected '_vs_projects' property in VS diagnostic")
    projects.EnumerateArray() |> ResizeArray

let getVsProjectName (project: JsonElement) =
    let name = project.GetProperty("_vs_projectName")
    Assert.True(name.ValueKind <> JsonValueKind.Undefined, "Expected '_vs_projectName' property in project info")
    name.GetString()

let getVsProjectIdentifier (project: JsonElement) =
    let id = project.GetProperty("_vs_projectIdentifier")
    Assert.True(id.ValueKind <> JsonValueKind.Undefined, "Expected '_vs_projectIdentifier' property in project info")
    id.GetString()

let getProjectContexts (client: TestRpcClient) (fileUri: Uri) =
    client.JsonRpc.InvokeWithParameterObjectAsync<VSProjectContextList>(
        "textDocument/_vs_getProjectContexts",
        VSGetProjectContextsParams(TextDocument = TextDocumentItem(Uri = fileUri)))

[<Literal>]
let sharedModuleContent = "module Shared\nlet x = 1"

let setupMultiProjectFile (client: TestRpcClient) (content: string) (projectNames: string list) =
    let fileOnDisk = sourceFileOnDisk content
    for name in projectNames do
        client.Workspace.Projects.AddOrUpdate(ProjectConfig.Empty(name = name), [ fileOnDisk.LocalPath ]) |> ignore
    fileOnDisk

let requestCodeActions (client: TestRpcClient) (fileUri: Uri) (line: int) =
    let range = Range(Start = Position(Line = line, Character = 0), End = Position(Line = line, Character = 999))
    client.JsonRpc.InvokeWithParameterObjectAsync<CodeAction array>(
        Methods.TextDocumentCodeActionName,
        CodeActionParams(
            TextDocument = TextDocumentIdentifier(Uri = fileUri),
            Range = range,
            Context = CodeActionContext(Diagnostics = [||])))

let openAndRequestCodeActions (client: TestRpcClient) (fileUri: Uri) (content: string) (line: int) =
    task {
        do! openDocument client fileUri content 1
        // Pull diagnostics first so the server has them cached
        let! _diags = pullDiagnostics client fileUri
        return! requestCodeActions client fileUri line
    }

let pullDiagnosticsInContext (client: TestRpcClient) (fileUri: Uri) (projectContext: VSProjectContext) =
    task {
        let! response =
            client.JsonRpc.InvokeWithParameterObjectAsync<SumType<RelatedFullDocumentDiagnosticReport, RelatedUnchangedDocumentDiagnosticReport>>(
                Methods.TextDocumentDiagnosticName,
                DocumentDiagnosticParams(TextDocument = VSTextDocumentIdentifier(Uri = fileUri, ProjectContext = projectContext)))
        return response.First
    }

/// Adds two projects sharing the same source file but using different conditional compilation
/// defines. The two projects also have distinct output paths so they look like two TFMs of the
/// same project to the workspace. Returns the file URI and the two FSharpProjectIdentifiers.
let setupMultiTargetLikeProject (client: TestRpcClient) (content: string) (sharedProjectFileName: string) (defineA: string) (defineB: string) =
    let fileOnDisk = sourceFileOnDisk content
    let dir = System.IO.Path.GetDirectoryName(fileOnDisk.LocalPath)
    let projectFileName = System.IO.Path.Combine(dir, sharedProjectFileName)

    let configA =
        ProjectConfig(
            projectFileName,
            outputFileName = Some (System.IO.Path.Combine(dir, $"out.{defineA}.dll")),
            referencesOnDisk = [],
            otherOptions = [ $"--define:{defineA}" ])
    let configB =
        ProjectConfig(
            projectFileName,
            outputFileName = Some (System.IO.Path.Combine(dir, $"out.{defineB}.dll")),
            referencesOnDisk = [],
            otherOptions = [ $"--define:{defineB}" ])

    let pidA = client.Workspace.Projects.AddOrUpdate(configA, [ fileOnDisk.LocalPath ])
    let pidB = client.Workspace.Projects.AddOrUpdate(configB, [ fileOnDisk.LocalPath ])
    fileOnDisk, pidA, pidB
