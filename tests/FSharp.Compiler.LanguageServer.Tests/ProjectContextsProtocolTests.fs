module LanguageServer.ProjectContextsProtocolTests

open System
open Xunit

open Microsoft.VisualStudio.LanguageServer.Protocol
open FSharp.Test.ProjectGeneration.WorkspaceHelpers
open FSharp.Compiler.CodeAnalysis.Workspace
open FSharp.Compiler.CodeAnalysis.ProjectSnapshot

open LanguageServer.ProtocolHelpers

#nowarn "57"

let getProjectContexts (client: TestRpcClient) (fileUri: Uri) =
    client.JsonRpc.InvokeAsync<VSProjectContextList>(
        "textDocument/_vs_getProjectContexts",
        VSGetProjectContextsParams(TextDocument = TextDocumentItem(Uri = fileUri)))

[<Fact>]
let ``GetProjectContexts returns single project for file in one project`` () =
    task {
        let! client = initializeLanguageServer None
        let content = "let x = 1"
        let fileOnDisk = setupSingleFileProject client content
        do! openDocument client fileOnDisk content 1

        let! result = getProjectContexts client fileOnDisk
        Assert.NotNull(result)
        Assert.NotNull(result.ProjectContexts)
        Assert.Equal(1, result.ProjectContexts.Length)
        Assert.Equal(0, result.DefaultIndex)
    }

[<Fact>]
let ``GetProjectContexts returns FSharp project kind`` () =
    task {
        let! client = initializeLanguageServer None
        let content = "let x = 1"
        let fileOnDisk = setupSingleFileProject client content
        do! openDocument client fileOnDisk content 1

        let! result = getProjectContexts client fileOnDisk
        let ctx = result.ProjectContexts[0]
        Assert.Equal(VSProjectKind.FSharp, ctx.Kind)
    }

[<Fact>]
let ``GetProjectContexts returns project label`` () =
    task {
        let! client = initializeLanguageServer None
        let content = "let x = 1"
        let fileOnDisk = setupSingleFileProject client content
        do! openDocument client fileOnDisk content 1

        let! result = getProjectContexts client fileOnDisk
        let ctx = result.ProjectContexts[0]
        Assert.False(String.IsNullOrWhiteSpace(ctx.Label), "Project context should have a non-empty label")
    }

[<Fact>]
let ``GetProjectContexts returns project id`` () =
    task {
        let! client = initializeLanguageServer None
        let content = "let x = 1"
        let fileOnDisk = setupSingleFileProject client content
        do! openDocument client fileOnDisk content 1

        let! result = getProjectContexts client fileOnDisk
        let ctx = result.ProjectContexts[0]
        Assert.False(String.IsNullOrWhiteSpace(ctx.Id), "Project context should have a non-empty id")
    }

[<Fact>]
let ``GetProjectContexts returns empty list for file not in any project`` () =
    task {
        let! client = initializeLanguageServer None
        let fileOnDisk = sourceFileOnDisk "let x = 1"
        do! openDocument client fileOnDisk "let x = 1" 1

        let! result = getProjectContexts client fileOnDisk
        Assert.NotNull(result)
        Assert.NotNull(result.ProjectContexts)
        Assert.Equal(0, result.ProjectContexts.Length)
    }

[<Fact>]
let ``GetProjectContexts returns multiple contexts for file in multiple projects`` () =
    task {
        let! client = initializeLanguageServer None
        let content = "module Shared\nlet x = 1"
        let fileOnDisk = sourceFileOnDisk content

        // Add the same file to two different projects
        let _pid1 = client.Workspace.Projects.AddOrUpdate(ProjectConfig.Empty(name = "ProjectA"), [ fileOnDisk.LocalPath ])
        let _pid2 = client.Workspace.Projects.AddOrUpdate(ProjectConfig.Empty(name = "ProjectB"), [ fileOnDisk.LocalPath ])

        do! openDocument client fileOnDisk content 1

        let! result = getProjectContexts client fileOnDisk
        Assert.NotNull(result)
        Assert.True(result.ProjectContexts.Length >= 2, $"Expected at least 2 project contexts but got {result.ProjectContexts.Length}")
        Assert.Equal(0, result.DefaultIndex)
    }

[<Fact>]
let ``GetProjectContexts returns distinct labels for different projects`` () =
    task {
        let! client = initializeLanguageServer None
        let content = "module Shared\nlet x = 1"
        let fileOnDisk = sourceFileOnDisk content

        let _pid1 = client.Workspace.Projects.AddOrUpdate(ProjectConfig.Empty(name = "ProjectA"), [ fileOnDisk.LocalPath ])
        let _pid2 = client.Workspace.Projects.AddOrUpdate(ProjectConfig.Empty(name = "ProjectB"), [ fileOnDisk.LocalPath ])

        do! openDocument client fileOnDisk content 1

        let! result = getProjectContexts client fileOnDisk
        let labels = result.ProjectContexts |> Array.map (fun c -> c.Label) |> Array.distinct
        Assert.Equal(result.ProjectContexts.Length, labels.Length)
    }

[<Fact>]
let ``GetProjectContexts returns distinct ids for different projects`` () =
    task {
        let! client = initializeLanguageServer None
        let content = "module Shared\nlet x = 1"
        let fileOnDisk = sourceFileOnDisk content

        let _pid1 = client.Workspace.Projects.AddOrUpdate(ProjectConfig.Empty(name = "ProjectA"), [ fileOnDisk.LocalPath ])
        let _pid2 = client.Workspace.Projects.AddOrUpdate(ProjectConfig.Empty(name = "ProjectB"), [ fileOnDisk.LocalPath ])

        do! openDocument client fileOnDisk content 1

        let! result = getProjectContexts client fileOnDisk
        let ids = result.ProjectContexts |> Array.map (fun c -> c.Id) |> Array.distinct
        Assert.Equal(result.ProjectContexts.Length, ids.Length)
    }

[<Fact>]
let ``GetProjectContexts default index is zero`` () =
    task {
        let! client = initializeLanguageServer None
        let content = "let x = 1"
        let fileOnDisk = setupSingleFileProject client content
        do! openDocument client fileOnDisk content 1

        let! result = getProjectContexts client fileOnDisk
        Assert.Equal(0, result.DefaultIndex)
    }
