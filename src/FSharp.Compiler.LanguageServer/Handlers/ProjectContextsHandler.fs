namespace FSharp.Compiler.LanguageServer.Handlers

open Microsoft.CommonLanguageServerProtocol.Framework
open Microsoft.VisualStudio.LanguageServer.Protocol
open FSharp.Compiler.LanguageServer.Common

open System.Threading
open System.Threading.Tasks

#nowarn "57"

type ProjectContextsHandler() =
    interface IMethodHandler with
        member _.MutatesSolutionState = false

    interface IRequestHandler<VSGetProjectContextsParams, VSProjectContextList, FSharpRequestContext> with
        [<LanguageServerEndpoint("textDocument/_vs_getProjectContexts", LanguageServerConstants.DefaultLanguageName)>]
        member _.HandleRequestAsync
            (request: VSGetProjectContextsParams, context: FSharpRequestContext, _cancellationToken: CancellationToken)
            =
            let uri = request.TextDocument.Uri

            let snapshots = context.Workspace.Query.GetProjectSnapshotsForFile(uri)

            let projectContexts =
                snapshots
                |> Array.map (fun snapshot ->
                    VSProjectContext(
                        Label = snapshot.Label,
                        Id = (snapshot.Identifier.ToString()),
                        Kind = VSProjectKind.FSharp
                    ))

            Task.FromResult(
                VSProjectContextList(
                    ProjectContexts = projectContexts,
                    DefaultIndex = 0
                )
            )
