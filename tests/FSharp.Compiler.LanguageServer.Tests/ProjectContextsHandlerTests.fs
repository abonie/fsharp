module LanguageServer.ProjectContextsHandlerTests

open System
open Xunit

open Microsoft.VisualStudio.LanguageServer.Protocol

open FSharp.Test.ProjectGeneration.WorkspaceHelpers
open FSharp.Compiler.CodeAnalysis.Workspace
open FSharp.Compiler.CodeAnalysis.ProjectSnapshot

open LanguageServer.ProtocolHelpers

#nowarn "57"

let pullProjectContexts (client: TestRpcClient) (fileUri: Uri) =
    client.JsonRpc.InvokeAsync<VSProjectContextList>(
        "textDocument/_vs_getProjectContexts",
        VSGetProjectContextsParams(TextDocument = TextDocumentItem(Uri = fileUri)))

[<Fact>]
let ``ProjectContexts returns single context for file in one project`` () =
    task {
        let! client = initializeLanguageServer None
        let fileOnDisk = setupSingleFileProject client cleanCode
        do! openDocument client fileOnDisk cleanCode 1

        let! result = pullProjectContexts client fileOnDisk

        Assert.NotNull(result)
        Assert.NotNull(result.ProjectContexts)
        Assert.Equal(1, result.ProjectContexts.Length)
        Assert.Equal(0, result.DefaultIndex)
        Assert.False(String.IsNullOrWhiteSpace(result.ProjectContexts[0].Label))
        Assert.False(String.IsNullOrWhiteSpace(result.ProjectContexts[0].Id))
        Assert.Equal(VSProjectKind.FSharp, result.ProjectContexts[0].Kind)
    }

[<Fact>]
let ``ProjectContexts returns multiple contexts for shared file`` () =
    task {
        let! client = initializeLanguageServer None
        let sharedFile = sourceFileOnDisk cleanCode

        let _pid1 = client.Workspace.Projects.AddOrUpdate(ProjectConfig.Create(name = "projA"), [ sharedFile.LocalPath ])
        let _pid2 = client.Workspace.Projects.AddOrUpdate(ProjectConfig.Create(name = "projB"), [ sharedFile.LocalPath ])

        do! openDocument client sharedFile cleanCode 1

        let! result = pullProjectContexts client sharedFile

        Assert.NotNull(result)
        Assert.NotNull(result.ProjectContexts)
        Assert.True(result.ProjectContexts.Length >= 2, $"Expected at least 2 project contexts but got {result.ProjectContexts.Length}")
        Assert.Equal(0, result.DefaultIndex)

        let ids = result.ProjectContexts |> Array.map (fun c -> c.Id) |> Array.distinct
        Assert.Equal(result.ProjectContexts.Length, ids.Length)

        Assert.All(result.ProjectContexts, fun c ->
            Assert.Equal(VSProjectKind.FSharp, c.Kind))
    }

[<Fact>]
let ``ProjectContexts returns empty array for file not in any project`` () =
    task {
        let! client = initializeLanguageServer None
        let fileOnDisk = sourceFileOnDisk cleanCode
        do! openDocument client fileOnDisk cleanCode 1

        let! result = pullProjectContexts client fileOnDisk

        Assert.NotNull(result)
        Assert.NotNull(result.ProjectContexts)
        Assert.Equal(0, result.ProjectContexts.Length)
        Assert.Equal(0, result.DefaultIndex)
    }
