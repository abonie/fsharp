module FSharp.Compiler.LanguageServer.Executable

open System
open StreamJsonRpc

[<EntryPoint>]
let main _argv =

    let formatter = new SystemTextJsonFormatter()
    addVSExtensionJsonConverters formatter.JsonSerializerOptions

    let messageHandler =
        new HeaderDelimitedMessageHandler(Console.OpenStandardOutput(), Console.OpenStandardInput(), formatter)

    let jsonRpc = new JsonRpc(messageHandler)

    let _s =
        new FSharpLanguageServer(jsonRpc, formatter.JsonSerializerOptions, (LspLogger Console.Out.Write))

    jsonRpc.StartListening()

    async {
        while true do
            do! Async.Sleep 1000
    }
    |> Async.RunSynchronously

    0
