namespace FSharp.Compiler.LanguageServer.Handlers

open System
open System.IO
open System.Security.Cryptography
open System.Text

open Microsoft.CommonLanguageServerProtocol.Framework
open Microsoft.VisualStudio.LanguageServer.Protocol
open FSharp.Compiler.LanguageServer.Common

open System.Threading
open System.Threading.Tasks

#nowarn "57"
#nowarn "3261"

type ProjectContextsHandler() =

    static member internal MakeProjectContextId(projectFileName: string, projectId: string option) =
        let guid =
            match projectId with
            | Some id ->
                match Guid.TryParse(id) with
                | true, g -> g
                | _ -> Guid(MD5.HashData(Encoding.UTF8.GetBytes(projectFileName)))
            | None -> Guid(MD5.HashData(Encoding.UTF8.GetBytes(projectFileName)))

        $"{guid}|{projectFileName}"

    interface IMethodHandler with
        member _.MutatesSolutionState = false

    interface IRequestHandler<VSGetProjectContextsParams, VSProjectContextList, FSharpRequestContext> with
        [<LanguageServerEndpoint("textDocument/_vs_getProjectContexts", LanguageServerConstants.DefaultLanguageName)>]
        member _.HandleRequestAsync
            (request: VSGetProjectContextsParams, context: FSharpRequestContext, _cancellationToken: CancellationToken)
            =
            let empty = VSProjectContextList(ProjectContexts = [||], DefaultIndex = 0)

            match request.TextDocument with
            | null -> Task.FromResult(empty)
            | textDoc when isNull textDoc.Uri -> Task.FromResult(empty)
            | textDoc ->
                let snapshots = context.Workspace.Query.GetProjectSnapshotsForFile(textDoc.Uri)

                let projectContexts =
                    snapshots
                    |> Array.map (fun snapshot ->
                        VSProjectContext(
                            Label = Path.GetFileNameWithoutExtension(snapshot.ProjectFileName),
                            Id = ProjectContextsHandler.MakeProjectContextId(snapshot.ProjectFileName, snapshot.ProjectId),
                            Kind = VSProjectKind.FSharp
                        ))

                Task.FromResult(
                    VSProjectContextList(
                        ProjectContexts = projectContexts,
                        DefaultIndex = 0
                    )
                )
