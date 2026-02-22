module LanguageServer.ProtocolHelpers

open System
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

    let formatter = new JsonMessageFormatter()

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
        let! response = jsonRpc.InvokeAsync<InitializeResult>("initialize", initializeParams)
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
    client.JsonRpc.NotifyAsync(
        Methods.TextDocumentDidOpenName,
        DidOpenTextDocumentParams(
            TextDocument = TextDocumentItem(Uri = fileUri, LanguageId = "F#", Version = version, Text = content)))

let changeDocument (client: TestRpcClient) (fileUri: Uri) (content: string) (version: int) =
    client.JsonRpc.NotifyAsync(
        Methods.TextDocumentDidChangeName,
        DidChangeTextDocumentParams(
            TextDocument = VersionedTextDocumentIdentifier(Uri = fileUri, Version = version),
            ContentChanges = [| TextDocumentContentChangeEvent(Text = content) |]))

let closeDocument (client: TestRpcClient) (fileUri: Uri) =
    client.JsonRpc.NotifyAsync(
        Methods.TextDocumentDidCloseName,
        DidCloseTextDocumentParams(TextDocument = TextDocumentIdentifier(Uri = fileUri)))

let pullDiagnosticResponse (client: TestRpcClient) (fileUri: Uri) =
    client.JsonRpc.InvokeAsync<SumType<RelatedFullDocumentDiagnosticReport, RelatedUnchangedDocumentDiagnosticReport>>(
        Methods.TextDocumentDiagnosticName,
        DocumentDiagnosticParams(TextDocument = TextDocumentIdentifier(Uri = fileUri)))

let pullDiagnostics (client: TestRpcClient) (fileUri: Uri) =
    task {
        let! response = pullDiagnosticResponse client fileUri
        let docs = response.First.RelatedDocuments
        Assert.True(docs.ContainsKey(fileUri), $"RelatedDocuments missing entry for {fileUri}")
        return docs[fileUri].First
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

let getProjectContexts (client: TestRpcClient) (fileUri: Uri) =
    client.JsonRpc.InvokeAsync<VSProjectContextList>(
        "textDocument/_vs_getProjectContexts",
        VSGetProjectContextsParams(TextDocument = TextDocumentItem(Uri = fileUri)))

let setupMultiProjectFile (client: TestRpcClient) (content: string) (projectNames: string list) =
    let fileOnDisk = sourceFileOnDisk content
    for name in projectNames do
        client.Workspace.Projects.AddOrUpdate(ProjectConfig.Empty(name = name), [ fileOnDisk.LocalPath ]) |> ignore
    fileOnDisk
