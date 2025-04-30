namespace FSharp.Compiler.LanguageServer.Handlers

open Microsoft.CommonLanguageServerProtocol.Framework
open Microsoft.VisualStudio.LanguageServer.Protocol
open Microsoft.VisualStudio.FSharp.Editor.CancellableTasks
open FSharp.Compiler.LanguageServer.Common
open FSharp.Compiler.LanguageServer
open System.Threading
open System.Collections.Generic
open FSharp.Compiler.EditorServices
open FSharp.Compiler.Symbols

#nowarn "57"

module internal CompletionHelpers =

    type LspCompletionItemKind = Microsoft.VisualStudio.LanguageServer.Protocol.CompletionItemKind
    type LspCompletionItem = Microsoft.VisualStudio.LanguageServer.Protocol.CompletionItem

    let fcsGlyphToLspCompletionKind (glyph: FSharpGlyph) =
        // Map based on FSharp.Compiler.EditorServices.FSharpGlyph
        // This mapping is approximate and might need refinement based on desired LSP behavior.
        match glyph with
        | FSharpGlyph.Class -> LspCompletionItemKind.Class
        | FSharpGlyph.Constant -> LspCompletionItemKind.Constant
        | FSharpGlyph.Delegate -> LspCompletionItemKind.Function
        | FSharpGlyph.Enum -> LspCompletionItemKind.Enum
        | FSharpGlyph.EnumMember -> LspCompletionItemKind.EnumMember
        | FSharpGlyph.Event -> LspCompletionItemKind.Event
        | FSharpGlyph.Exception -> LspCompletionItemKind.Class
        | FSharpGlyph.Field -> LspCompletionItemKind.Field
        | FSharpGlyph.Interface -> LspCompletionItemKind.Interface
        | FSharpGlyph.Method -> LspCompletionItemKind.Method
        | FSharpGlyph.OverridenMethod -> LspCompletionItemKind.Method
        | FSharpGlyph.Module -> LspCompletionItemKind.Module
        | FSharpGlyph.NameSpace -> LspCompletionItemKind.Module
        | FSharpGlyph.Property -> LspCompletionItemKind.Property
        | FSharpGlyph.Struct -> LspCompletionItemKind.Struct
        | FSharpGlyph.Typedef -> LspCompletionItemKind.Class
        | FSharpGlyph.Type -> LspCompletionItemKind.Class
        | FSharpGlyph.Union -> LspCompletionItemKind.Enum
        | FSharpGlyph.Variable -> LspCompletionItemKind.Variable
        | FSharpGlyph.ExtensionMethod -> LspCompletionItemKind.Method
        | FSharpGlyph.Error -> LspCompletionItemKind.Text
        | FSharpGlyph.TypeParameter -> LspCompletionItemKind.TypeParameter

    /// Extracts documentation from a ToolTipText structure
    let extractDocumentation (tooltipText: ToolTipText) : string =
        match tooltipText with
        | ToolTipText (ToolTipElement.Group (firstElementData :: _) :: _) ->
            match firstElementData.XmlDoc with
            | FSharpXmlDoc.None -> ""
            | FSharpXmlDoc.FromXmlText xmlDoc -> xmlDoc.GetXmlText()
            | FSharpXmlDoc.FromXmlFile _ -> ""
        | ToolTipText (ToolTipElement.CompositionError _ :: _) ->
            "" // No documentation for composition errors
        | _ ->
            // Fallback for other cases (e.g., ToolTipText [], ToolTipElement.None)
            ""

    /// Creates an LSP completion item from a compiler DeclarationListItem
    let fromDeclarationListItem (item: DeclarationListItem) : LspCompletionItem =
        LspCompletionItem(
            Label = item.NameInList,
            Kind = fcsGlyphToLspCompletionKind item.Glyph,
            InsertText = item.NameInCode,
            Documentation = SumType<string, MarkupContent>(extractDocumentation item.Description)
        )

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

    interface IRequestHandler<CompletionParams, SumType<CompletionList, CompletionHelpers.LspCompletionItem[]>, FSharpRequestContext> with
        [<LanguageServerEndpoint(Methods.TextDocumentCompletionName, LanguageServerConstants.DefaultLanguageName)>]
        member _.HandleRequestAsync(request: CompletionParams, context: FSharpRequestContext, cancellationToken: CancellationToken) =
            cancellableTask {
                let documentUri = request.TextDocument.Uri
                let position = request.Position
                // XXX LSP uses 0-based lines, FCS uses 1-based lines
                let fcsLine = position.Line + 1
                // LSP uses 0-based columns, FCS uses 0-based columns for GetPartialLongNameEx
                let fcsCol = position.Character

                let! sourceOpt = context.Workspace.Query.GetSource(documentUri)

                match sourceOpt with
                | None ->
                    context.Logger.LogWarning $"Could not get source for document: {documentUri}"
                    // Return empty completion list if source is not available
                    return SumType<CompletionList, CompletionHelpers.LspCompletionItem[]>(CompletionList(IsIncomplete = false, Items = [||]))
                | Some source ->
                    let lineText = source.GetLineString(fcsLine)

                    // GetPartialLongNameEx expects 0-based column
                    let partialLongName = QuickParse.GetPartialLongNameEx(lineText, fcsCol)

                    let! parseResultsOpt, checkResultsOpt =
                        context.Workspace.Query.GetParseAndCheckResultsForFile(documentUri)

                    match parseResultsOpt, checkResultsOpt with
                    | Some _parseResults, Some checkResults ->
                        // Use the helper function from Completion module
                        let completionItems =
                            checkResults.GetDeclarationListInfo(
                                parseResultsOpt,
                                fcsLine,
                                lineText,
                                partialLongName
                            ).Items
                            |> Array.map CompletionHelpers.fromDeclarationListItem

                        // Using CompletionList allows specifying IsIncomplete
                        let completionList =
                            CompletionList(IsIncomplete = false, Items = completionItems)

                        return SumType<CompletionList, CompletionHelpers.LspCompletionItem[]>(completionList)
                    | _ ->
                        context.Logger.LogWarning $"Could not get parse/check results for document: {documentUri}"
                        // Return empty completion list if parse/check results are not available
                        return SumType<CompletionList, CompletionHelpers.LspCompletionItem[]>(CompletionList(IsIncomplete = false, Items = [||]))
            }
            |> CancellableTask.start cancellationToken
