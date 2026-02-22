module LanguageServer.DiagnosticsProtocolTests

open System
open Xunit

open Microsoft.VisualStudio.LanguageServer.Protocol
open FSharp.Test.ProjectGeneration.WorkspaceHelpers
open FSharp.Compiler.CodeAnalysis.Workspace
open FSharp.Compiler.CodeAnalysis.ProjectSnapshot
open FSharp.Compiler.LanguageServer

open LanguageServer.ProtocolHelpers

#nowarn "57"

[<Theory>]
[<InlineData("let x = ")>]
[<InlineData("let x: int = \"hello\"")>]
[<InlineData("let f x = if x then")>]
let ``Erroneous code produces Error-severity diagnostics`` (content: string) =
    task {
        let! client = initializeLanguageServer None
        let fileOnDisk = setupSingleFileProject client content
        let! diagnostics = openAndPullDiagnostics client fileOnDisk content
        Assert.True(diagnostics.Length > 0, $"Expected at least one diagnostic for: {content}")
        Assert.True(diagnostics |> Array.exists (fun d -> d.Severity.HasValue && d.Severity.Value = DiagnosticSeverity.Error),
            "Expected at least one Error-severity diagnostic")
    }

[<Fact>]
let ``Diagnostics report warnings with Warning severity`` () =
    task {
        let! client = initializeLanguageServer None
        let content = "[<System.Obsolete>]\nlet f () = 1\nlet x = f ()"
        let fileOnDisk = setupSingleFileProject client content
        let! diagnostics = openAndPullDiagnostics client fileOnDisk content
        let warnings = diagnostics |> Array.filter (fun d -> d.Severity.HasValue && d.Severity.Value = DiagnosticSeverity.Warning)
        Assert.True(warnings.Length > 0, "Expected at least one warning diagnostic")
    }

[<Fact>]
let ``Diagnostics have FS-prefixed codes, LSP-prefixed messages, and valid ranges`` () =
    task {
        let! client = initializeLanguageServer None
        let fileOnDisk = setupSingleFileProject client notMutableCode
        let! diagnostics = openAndPullDiagnostics client fileOnDisk notMutableCode
        Assert.True(diagnostics.Length > 0, "Expected diagnostics for code with mutability error")
        Assert.All(diagnostics, fun d ->
            let code = d.Code.Value.Second
            Assert.StartsWith("FS", code)
            Assert.True(code.Length > 2 && code.Substring(2) |> Seq.forall System.Char.IsDigit,
                $"Error code '{code}' should have format 'FS' followed by digits")
            Assert.StartsWith("LSP:", d.Message)
            Assert.True(
                d.Range.Start.Line < d.Range.End.Line ||
                (d.Range.Start.Line = d.Range.End.Line && d.Range.Start.Character <= d.Range.End.Character),
                $"Invalid range: start ({d.Range.Start.Line},{d.Range.Start.Character}) should not be after end ({d.Range.End.Line},{d.Range.End.Character})"))
        let mutabilityError = diagnostics |> Array.tryFind (fun d -> d.Message.Contains("mutable"))
        Assert.True(mutabilityError.IsSome, "Expected a diagnostic mentioning 'mutable'")
        Assert.Equal(1, mutabilityError.Value.Range.Start.Line)
    }

[<Theory>]
[<InlineData("")>]
[<InlineData("module M\nlet x = 1\nlet y = x + 1")>]
let ``Valid content produces no diagnostics`` (content: string) =
    task {
        let! client = initializeLanguageServer None
        let fileOnDisk = setupSingleFileProject client content
        let! diagnostics = openAndPullDiagnostics client fileOnDisk content
        Assert.Equal(0, diagnostics.Length)
    }

[<Fact>]
let ``Diagnostics appear after introducing an error`` () =
    task {
        let! client = initializeLanguageServer None
        let fileOnDisk = setupSingleFileProject client cleanCode
        let! cleanDiags = openAndPullDiagnostics client fileOnDisk cleanCode
        Assert.Equal(0, cleanDiags.Length)

        do! changeDocument client fileOnDisk notMutableCode 2
        let! brokenReport = pullDiagnostics client fileOnDisk
        Assert.True(brokenReport.Items.Length > 0, "Expected diagnostics after introducing an error")
    }

[<Fact>]
let ``Diagnostics disappear after fixing an error`` () =
    task {
        let! client = initializeLanguageServer None
        let fileOnDisk = setupSingleFileProject client notMutableCode
        let! brokenDiags = openAndPullDiagnostics client fileOnDisk notMutableCode
        Assert.True(brokenDiags.Length > 0, "Expected diagnostics for code with mutability error")

        let fixedContent = "let mutable x = 1\nx <- 2"
        do! changeDocument client fileOnDisk fixedContent 2
        let! fixedReport = pullDiagnostics client fileOnDisk
        Assert.Equal(0, fixedReport.Items.Length)
    }

[<Fact>]
let ``Diagnostics revert to disk content after close`` () =
    task {
        let! client = initializeLanguageServer None
        let fileOnDisk = setupSingleFileProject client cleanCode
        do! openDocument client fileOnDisk cleanCode 1

        do! changeDocument client fileOnDisk notMutableCode 2
        let! editedReport = pullDiagnostics client fileOnDisk
        Assert.True(editedReport.Items.Length > 0, "Should have diagnostics after editing to broken state")

        do! closeDocument client fileOnDisk
        let! closedReport = pullDiagnostics client fileOnDisk
        Assert.Equal(0, closedReport.Items.Length)
    }

[<Fact>]
let ``Multiple diagnostics reported for multiple errors`` () =
    task {
        let! client = initializeLanguageServer None
        let content = "let x = 1\nx <- 2\nlet y = \"hello\"\ny <- \"world\""
        let fileOnDisk = setupSingleFileProject client content
        let! diagnostics = openAndPullDiagnostics client fileOnDisk content
        Assert.True(diagnostics.Length >= 2, $"Expected at least 2 diagnostics but got {diagnostics.Length}")
    }

[<Fact>]
let ``Unresolved identifier produces diagnostics`` () =
    task {
        let! client = initializeLanguageServer None
        let content = "let x = undefinedFunction 42"
        let fileOnDisk = setupSingleFileProject client content
        let! diagnostics = openAndPullDiagnostics client fileOnDisk content
        Assert.True(diagnostics.Length > 0, "Expected at least one diagnostic for erroneous code")
        Assert.True(diagnostics |> Array.exists (fun d -> d.Message.Contains("is not defined")),
            "Expected diagnostic about undefined identifier")
    }

[<Fact>]
let ``Diagnostics update correctly through multiple edits`` () =
    task {
        let! client = initializeLanguageServer None
        let fileOnDisk = setupSingleFileProject client cleanCode
        let! d1 = openAndPullDiagnostics client fileOnDisk cleanCode
        Assert.Equal(0, d1.Length)

        do! changeDocument client fileOnDisk notMutableCode 2
        let! r2 = pullDiagnostics client fileOnDisk
        Assert.True(r2.Items.Length > 0, "Expected diagnostics after introducing mutability error")

        do! changeDocument client fileOnDisk "let mutable x = 1\nx <- 2" 3
        let! r3 = pullDiagnostics client fileOnDisk
        Assert.Equal(0, r3.Items.Length)

        do! changeDocument client fileOnDisk "let x: int = \"hello\"" 4
        let! r4 = pullDiagnostics client fileOnDisk
        Assert.True(r4.Items.Length > 0, "Expected diagnostics after introducing a type mismatch")
    }

[<Fact>]
let ``Diagnostics for file not in any project`` () =
    task {
        let! client = initializeLanguageServer None
        let fileOnDisk = sourceFileOnDisk cleanCode
        do! openDocument client fileOnDisk cleanCode 1
        let! response = pullDiagnosticResponse client fileOnDisk
        let report = response.First
        Assert.NotNull(report.RelatedDocuments)
        Assert.NotNull(report.Items)
        Assert.Equal(0, report.Items.Length)
        Assert.True(report.RelatedDocuments.ContainsKey(fileOnDisk), "RelatedDocuments should contain the requested file even when it is not in a project")
    }

[<Fact>]
let ``Server with diagnostics disabled does not advertise diagnostic capabilities`` () =
    task {
        let config = { FSharpLanguageServerConfig.Default with EnabledFeatures = { Diagnostics = false } }
        let! client = createLanguageServer (FSharpWorkspace()) (Some config)
        Assert.Null(client.Capabilities.DiagnosticOptions)
    }

[<Fact>]
let ``Diagnostics for specific file in multi-file project`` () =
    task {
        let! client = initializeLanguageServer None
        let content1 = "module A\nlet x = 1"
        let content2 = "module B\nlet y = A.x + 1"
        let file1 = sourceFileOnDisk content1
        let file2 = sourceFileOnDisk content2
        let _pid = client.Workspace.Projects.AddOrUpdate(ProjectConfig.Create(), [ file1.LocalPath; file2.LocalPath ])
        let! diagnostics = openAndPullDiagnostics client file2 content2
        Assert.Equal(0, diagnostics.Length)
        do! openDocument client file1 "module A" 1
        let! diagsAfterBreak = pullDiagnostics client file2
        Assert.True(diagsAfterBreak.Items.Length > 0, "Expected diagnostics in file2 after removing A.x from file1")
    }

[<Fact>]
let ``Diagnostic report contains a ResultId`` () =
    task {
        let! client = initializeLanguageServer None
        let fileOnDisk = setupSingleFileProject client cleanCode
        do! openDocument client fileOnDisk cleanCode 1
        let! report = pullDiagnostics client fileOnDisk
        Assert.NotNull(report.ResultId)
        Assert.False(String.IsNullOrWhiteSpace(report.ResultId))
    }

[<Fact>]
let ``Diagnostic response uses RelatedDocuments with file URI as key`` () =
    task {
        let! client = initializeLanguageServer None
        let fileOnDisk = setupSingleFileProject client cleanCode
        do! openDocument client fileOnDisk cleanCode 1
        let! response = pullDiagnosticResponse client fileOnDisk
        let relatedDocs = response.First.RelatedDocuments
        Assert.NotNull(relatedDocs)
        Assert.True(relatedDocs.Count > 0, "RelatedDocuments should contain at least one entry")
        Assert.True(relatedDocs.ContainsKey(fileOnDisk), "RelatedDocuments should have entry for requested file")
    }

[<Fact>]
let ``Diagnostics work correctly after open-close-reopen cycle`` () =
    task {
        let! client = initializeLanguageServer None
        let fileOnDisk = setupSingleFileProject client notMutableCode
        let! d1 = openAndPullDiagnostics client fileOnDisk notMutableCode
        Assert.True(d1.Length > 0, "Expected diagnostics for code with mutability error")

        do! closeDocument client fileOnDisk

        do! openDocument client fileOnDisk notMutableCode 1
        let! r2 = pullDiagnostics client fileOnDisk
        Assert.True(r2.Items.Length > 0, "Expected diagnostics after reopening file with errors")
        Assert.Equal(d1.Length, r2.Items.Length)
    }

[<Fact>]
let ``Diagnostics work for file with many errors`` () =
    task {
        let! client = initializeLanguageServer None
        let lines = [| for i in 1..10 -> $"let x{i} = undefinedVar{i}" |]
        let content = lines |> String.concat "\n"
        let fileOnDisk = setupSingleFileProject client content
        let! diagnostics = openAndPullDiagnostics client fileOnDisk content
        Assert.True(diagnostics.Length >= 5, $"Expected at least 5 diagnostics for 10 error lines but got {diagnostics.Length}")
    }

[<Fact>]
let ``All diagnostics have a valid LSP severity`` () =
    task {
        let! client = initializeLanguageServer None
        let fileOnDisk = setupSingleFileProject client notMutableCode
        let! diagnostics = openAndPullDiagnostics client fileOnDisk notMutableCode
        Assert.True(diagnostics.Length > 0, "Expected at least one diagnostic")
        Assert.All(diagnostics, fun d ->
            Assert.True(d.Severity.HasValue, "Every diagnostic should have a severity")
            let sev = d.Severity.Value
            Assert.True(
                sev = DiagnosticSeverity.Error || sev = DiagnosticSeverity.Warning ||
                sev = DiagnosticSeverity.Information || sev = DiagnosticSeverity.Hint,
                $"Unexpected severity value: {sev}"))
    }

[<Fact>]
let ``ResultId changes when diagnostics change`` () =
    task {
        let! client = initializeLanguageServer None
        let fileOnDisk = setupSingleFileProject client cleanCode
        do! openDocument client fileOnDisk cleanCode 1
        let! report1 = pullDiagnostics client fileOnDisk
        Assert.False(String.IsNullOrWhiteSpace(report1.ResultId), "First ResultId should not be empty")

        do! changeDocument client fileOnDisk notMutableCode 2
        let! report2 = pullDiagnostics client fileOnDisk
        Assert.False(String.IsNullOrWhiteSpace(report2.ResultId), "Second ResultId should not be empty")
        Assert.NotEqual<string>(report1.ResultId, report2.ResultId)
    }
