module LanguageServer.ProjectContextsProtocolTests

open System
open Xunit

open Microsoft.VisualStudio.LanguageServer.Protocol
open FSharp.Test.ProjectGeneration.WorkspaceHelpers

open LanguageServer.ProtocolHelpers

#nowarn "57"

[<Fact>]
let ``GetProjectContexts returns single project for file in one project`` () =
    task {
        let! client = initializeLanguageServer None
        let fileOnDisk = setupSingleFileProject client cleanCode
        do! openDocument client fileOnDisk cleanCode 1

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
        let fileOnDisk = setupSingleFileProject client cleanCode
        do! openDocument client fileOnDisk cleanCode 1

        let! result = getProjectContexts client fileOnDisk
        let ctx = result.ProjectContexts[0]
        Assert.Equal(VSProjectKind.FSharp, ctx.Kind)
    }

[<Fact>]
let ``GetProjectContexts returns project label containing project file name`` () =
    task {
        let! client = initializeLanguageServer None
        let fileOnDisk = setupSingleFileProject client cleanCode
        do! openDocument client fileOnDisk cleanCode 1

        let! result = getProjectContexts client fileOnDisk
        let ctx = result.ProjectContexts[0]
        Assert.False(String.IsNullOrWhiteSpace(ctx.Label), "Project context should have a non-empty label")
        Assert.EndsWith(".fsproj", ctx.Label)
    }

[<Fact>]
let ``GetProjectContexts returns project id`` () =
    task {
        let! client = initializeLanguageServer None
        let fileOnDisk = setupSingleFileProject client cleanCode
        do! openDocument client fileOnDisk cleanCode 1

        let! result = getProjectContexts client fileOnDisk
        let ctx = result.ProjectContexts[0]
        Assert.False(String.IsNullOrWhiteSpace(ctx.Id), "Project context should have a non-empty id")
    }

[<Fact>]
let ``GetProjectContexts returns empty list for file not in any project`` () =
    task {
        let! client = initializeLanguageServer None
        let fileOnDisk = sourceFileOnDisk cleanCode
        do! openDocument client fileOnDisk cleanCode 1

        let! result = getProjectContexts client fileOnDisk
        Assert.NotNull(result)
        Assert.NotNull(result.ProjectContexts)
        Assert.Equal(0, result.ProjectContexts.Length)
        Assert.Equal(0, result.DefaultIndex)
    }

[<Fact>]
let ``GetProjectContexts returns multiple contexts with FSharp kind for file in multiple projects`` () =
    task {
        let! client = initializeLanguageServer None
        let fileOnDisk = setupMultiProjectFile client sharedModuleContent [ "ProjectA"; "ProjectB" ]

        do! openDocument client fileOnDisk sharedModuleContent 1

        let! result = getProjectContexts client fileOnDisk
        Assert.NotNull(result)
        Assert.True(result.ProjectContexts.Length >= 2, $"Expected at least 2 project contexts but got {result.ProjectContexts.Length}")
        Assert.Equal(0, result.DefaultIndex)
        Assert.All(result.ProjectContexts, fun c -> Assert.Equal(VSProjectKind.FSharp, c.Kind))
    }

[<Fact>]
let ``GetProjectContexts returns distinct labels and ids for different projects`` () =
    task {
        let! client = initializeLanguageServer None
        let fileOnDisk = setupMultiProjectFile client sharedModuleContent [ "ProjectA"; "ProjectB" ]

        do! openDocument client fileOnDisk sharedModuleContent 1

        let! result = getProjectContexts client fileOnDisk
        let labels = result.ProjectContexts |> Array.map (fun c -> c.Label) |> Array.distinct
        Assert.Equal(result.ProjectContexts.Length, labels.Length)
        let ids = result.ProjectContexts |> Array.map (fun c -> c.Id) |> Array.distinct
        Assert.Equal(result.ProjectContexts.Length, ids.Length)
    }

[<Fact>]
let ``GetProjectContexts returns results for file not opened via didOpen`` () =
    task {
        let! client = initializeLanguageServer None
        let fileOnDisk = setupSingleFileProject client cleanCode

        let! result = getProjectContexts client fileOnDisk
        Assert.NotNull(result)
        Assert.NotNull(result.ProjectContexts)
        Assert.Equal(1, result.ProjectContexts.Length)
        Assert.Equal(VSProjectKind.FSharp, result.ProjectContexts[0].Kind)
    }
