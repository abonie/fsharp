module LanguageServer.ProjectContextsHandlerTests

open System
open Xunit

open Microsoft.VisualStudio.LanguageServer.Protocol

open FSharp.Test.ProjectGeneration.WorkspaceHelpers
open FSharp.Compiler.CodeAnalysis.Workspace
open FSharp.Compiler.CodeAnalysis.ProjectSnapshot

open LanguageServer.Protocol

#nowarn "57"

[<Fact>]
let ``ProjectContexts returns single context for file in one project`` () =
    task {
        let! client = initializeLanguageServer None
        let fileOnDisk = setupSingleFileProject client cleanCode
        do! openDocument client fileOnDisk cleanCode 1

        let request = VSGetProjectContextsParams(TextDocument = TextDocumentItem(Uri = fileOnDisk))

        let! result =
            client.JsonRpc.InvokeAsync<VSProjectContextList>("textDocument/_vs_getProjectContexts", request)

        Assert.NotNull(result)
        Assert.NotNull(result.ProjectContexts)
        Assert.Equal(1, result.ProjectContexts.Length)
        Assert.Equal(0, result.DefaultIndex)
        Assert.False(String.IsNullOrWhiteSpace(result.ProjectContexts[0].Label))
        Assert.False(String.IsNullOrWhiteSpace(result.ProjectContexts[0].Id))
    }

[<Fact>]
let ``ProjectContexts returns multiple contexts for shared file`` () =
    task {
        let! client = initializeLanguageServer None
        let sharedFile = sourceFileOnDisk cleanCode

        let _pid1 = client.Workspace.Projects.AddOrUpdate(ProjectConfig.Create(name = "projA"), [ sharedFile.LocalPath ])
        let _pid2 = client.Workspace.Projects.AddOrUpdate(ProjectConfig.Create(name = "projB"), [ sharedFile.LocalPath ])

        do! openDocument client sharedFile cleanCode 1

        let request = VSGetProjectContextsParams(TextDocument = TextDocumentItem(Uri = sharedFile))

        let! result =
            client.JsonRpc.InvokeAsync<VSProjectContextList>("textDocument/_vs_getProjectContexts", request)

        Assert.NotNull(result)
        Assert.NotNull(result.ProjectContexts)
        Assert.True(result.ProjectContexts.Length >= 2, $"Expected at least 2 project contexts but got {result.ProjectContexts.Length}")
        Assert.Equal(0, result.DefaultIndex)
    }

[<Fact>]
let ``ProjectContexts returns empty array for file not in any project`` () =
    task {
        let! client = initializeLanguageServer None
        let fileOnDisk = sourceFileOnDisk cleanCode
        do! openDocument client fileOnDisk cleanCode 1

        let request = VSGetProjectContextsParams(TextDocument = TextDocumentItem(Uri = fileOnDisk))

        let! result =
            client.JsonRpc.InvokeAsync<VSProjectContextList>("textDocument/_vs_getProjectContexts", request)

        Assert.NotNull(result)
        Assert.NotNull(result.ProjectContexts)
        Assert.Equal(0, result.ProjectContexts.Length)
        Assert.Equal(0, result.DefaultIndex)
    }
