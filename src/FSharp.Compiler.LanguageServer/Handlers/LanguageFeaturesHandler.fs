namespace FSharp.Compiler.LanguageServer.Handlers

open Microsoft.CommonLanguageServerProtocol.Framework
open Microsoft.VisualStudio.LanguageServer.Protocol
open Microsoft.VisualStudio.FSharp.Editor.CancellableTasks
open FSharp.Compiler.LanguageServer.Common
open FSharp.Compiler.LanguageServer
open System.Threading.Tasks
open System.Threading
open System.Collections.Generic
open Microsoft.VisualStudio.FSharp.Editor

#nowarn "57"

type LanguageFeaturesHandler() =
    interface IMethodHandler with
        member _.MutatesSolutionState = false

    interface IRequestHandler<
        DocumentDiagnosticParams,
        SumType<RelatedFullDocumentDiagnosticReport, RelatedUnchangedDocumentDiagnosticReport>,
        FSharpRequestContext
     > with
        [<LanguageServerEndpoint(Methods.TextDocumentDiagnosticName, LanguageServerConstants.DefaultLanguageName)>]
        member _.HandleRequestAsync
            (request: DocumentDiagnosticParams, context: FSharpRequestContext, cancellationToken: CancellationToken)
            =
            cancellableTask {

                let! fsharpDiagnosticReport = context.Workspace.Query.GetDiagnosticsForFile request.TextDocument.Uri

                let report =
                    FullDocumentDiagnosticReport(
                        Items = (fsharpDiagnosticReport.Diagnostics |> Array.map (_.ToLspDiagnostic())),
                        ResultId = fsharpDiagnosticReport.ResultId
                    )

                let relatedDocuments = Dictionary()

                relatedDocuments.Add(
                    request.TextDocument.Uri,
                    SumType<FullDocumentDiagnosticReport, UnchangedDocumentDiagnosticReport> report
                )

                return
                    SumType<RelatedFullDocumentDiagnosticReport, RelatedUnchangedDocumentDiagnosticReport>(
                        RelatedFullDocumentDiagnosticReport(RelatedDocuments = relatedDocuments)
                    )
            }
            |> CancellableTask.start cancellationToken

    interface IRequestHandler<SemanticTokensParams, SemanticTokens, FSharpRequestContext> with
        [<LanguageServerEndpoint(Methods.TextDocumentSemanticTokensFullName, LanguageServerConstants.DefaultLanguageName)>]
        member _.HandleRequestAsync(request: SemanticTokensParams, context: FSharpRequestContext, cancellationToken: CancellationToken) =
            cancellableTask {
                let! tokens = context.GetSemanticTokensForFile(request.TextDocument.Uri)
                return SemanticTokens(Data = tokens)
            }
            |> CancellableTask.start cancellationToken

    interface IRequestHandler<CompletionParams, SumType<CompletionList, CompletionItem[]>, FSharpRequestContext> with
        [<LanguageServerEndpoint(Methods.TextDocumentCompletionName, LanguageServerConstants.DefaultLanguageName)>]
        member _.HandleRequestAsync(request: CompletionParams, _context: FSharpRequestContext, cancellationToken: CancellationToken) =
            cancellableTask {
                // Get the document URI
                let _documentUri = request.TextDocument.Uri
                // Get position information
                let _position = request.Position

                // TODO: Query workspace for completions at position
                // This would use context.Workspace.Query.GetCompletionsAtPosition or similar
                // For now, we'll return a simple hard-coded completion list

                let completionItems = [|
                    CompletionItem(
                        Label = "example-completion",
                        Kind = CompletionItemKind.Function,
                        Detail = "Example completion item",
                        Documentation = SumType<string, MarkupContent>("This is a placeholder completion item")
                    )
                |]

                // Return as SumType (either CompletionList or CompletionItem[])
                // Here we choose to return the array variant
                return SumType<CompletionList, CompletionItem[]>(completionItems)
            }
            |> CancellableTask.start cancellationToken
