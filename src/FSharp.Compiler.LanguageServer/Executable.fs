module FSharp.Compiler.LanguageServer.Executable

open System
open StreamJsonRpc

[<EntryPoint>]
let main _argv =

    let outputHandler = new HeaderDelimitedMessageHandler(Console.OpenStandardOutput(), Console.OpenStandardInput())
    let jsonRpc = new JsonRpc(outputHandler)

    let _s = new FSharpLanguageServer(jsonRpc, (LspLogger Console.Error.Write))

    jsonRpc.StartListening()

    // Wait for the JSON-RPC connection to complete (i.e., when the client disconnects)
    jsonRpc.Completion.Wait()

    0
