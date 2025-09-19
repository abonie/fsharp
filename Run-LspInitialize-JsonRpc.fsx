#!/usr/bin/env -S dotnet fsi

(***
    Run-LspInitialize-JsonRpc.fsx

    Reimplementation of PowerShell script Run-LspInitialize.ps1 in idiomatic F#.
    Responsibilities:
      * Parse command-line arguments
      * Locate and launch FSharp.Compiler.LanguageServer executable
      * Perform minimal LSP handshake: initialize -> (initialized) -> shutdown -> exit
      * Support verbosity, stderr dumping, timeout, and skipping initialized notification

        Usage (from repo root):
            dotnet fsi Run-LspInitialize-JsonRpc.fsx -- --configuration Debug --timeoutSeconds 15 --verbose --dumpStderr

    Flags:
      --configuration <Debug|Release> (default Debug)
      --timeoutSeconds <int>          (default 15)
      --noInitializedNotification     (omit the initialized notification)
      --verbose                       (log protocol traffic)
      --dumpStderr                    (mirror server stderr to console)

        Design notes:
            * Functional style with small pure helpers
            * Uses Newtonsoft.Json exclusively (aligns with StreamJsonRpc formatter)
            * Structured log prefixes for easy parsing
***)

open System
open System.IO
open System.Text
open System.Diagnostics
open System.Threading
open System.Threading.Tasks
// Added for StreamJsonRpc-based implementation
#r "artifacts/bin/FSharp.Compiler.LanguageServer/Debug/net8.0/StreamJsonRpc.dll"
#r "artifacts/bin/FSharp.Compiler.LanguageServer/Debug/net8.0/Microsoft.VisualStudio.Validation.dll"
#r "artifacts/bin/FSharp.Compiler.LanguageServer/Debug/net8.0/System.IO.Pipelines.dll"
#r "artifacts/bin/FSharp.Compiler.LanguageServer/Debug/net8.0/Newtonsoft.Json.dll"
open StreamJsonRpc
open Newtonsoft.Json
open Newtonsoft.Json.Linq

// --------------------------- Argument Parsing ---------------------------

[<CLIMutable>]
type Options = {
    Configuration : string
    TimeoutSeconds : int
    SendInitialized : bool
    Verbose : bool
    DumpStderr : bool
}

let defaultOptions = {
    Configuration = "Debug"
    TimeoutSeconds = 15
    SendInitialized = true
    Verbose = false
    DumpStderr = false
}

let (|Prefix|_|) (p:string) (s:string) = if s.StartsWith(p, StringComparison.OrdinalIgnoreCase) then Some(s[p.Length..]) else None

let parseArgs (argv: string array) : Result<Options,string> =
    let rec loop (opts:Options) i =
        if i >= argv.Length then Ok opts else
        match argv.[i] with
        | Prefix "--configuration=" v -> loop { opts with Configuration = v } (i+1)
        | "--configuration" when i+1 < argv.Length -> loop { opts with Configuration = argv.[i+1] } (i+2)
        | Prefix "--timeoutSeconds=" v ->
            match Int32.TryParse v with
            | true, n when n > 0 -> loop { opts with TimeoutSeconds = n } (i+1)
            | _ -> Error ($"Invalid timeoutSeconds: {v}")
        | "--timeoutSeconds" when i+1 < argv.Length ->
            match Int32.TryParse argv.[i+1] with
            | true, n when n > 0 -> loop { opts with TimeoutSeconds = n } (i+2)
            | _ -> Error ($"Invalid timeoutSeconds: {argv.[i+1]}")
        | "--noInitializedNotification" -> loop { opts with SendInitialized = false } (i+1)
        | "--verbose" -> loop { opts with Verbose = true } (i+1)
        | "--dumpStderr" -> loop { opts with DumpStderr = true } (i+1)
        | x -> Error ($"Unrecognized argument: {x}")
    loop defaultOptions 0

// --------------------------- Logging Helpers ---------------------------

module Log =
    let inline private stamp() = DateTime.UtcNow.ToString("HH:mm:ss.fff", Globalization.CultureInfo.InvariantCulture)
    let info  (m:string) = printfn "[info %s] %s"  (stamp()) m
    let warn  (m:string) = printfn "[warn %s] %s"  (stamp()) m
    let error (m:string) = eprintfn "[error %s] %s" (stamp()) m
    let protoOut (json:string) =
        let len = Encoding.UTF8.GetByteCount json
        printfn "[lsp-> %s %d] %s" (stamp()) len json
    let protoIn  (json:string) =
        let len = Encoding.UTF8.GetByteCount json
        printfn "[lsp<- %s %d] %s" (stamp()) len json

// --------------------------- JSON Envelope Helpers ---------------------------

module JsonEnvelope =
    let private toJson (props: JProperty seq) =
        JObject(props |> Seq.map id |> Seq.toArray).ToString(Formatting.None)

    let request (id:int) (methodName:string) (parameters: JToken voption) =
        seq {
            yield JProperty("jsonrpc","2.0")
            yield JProperty("id", id)
            yield JProperty("method", methodName)
            match parameters with
            | ValueSome p -> yield JProperty("params", p)
            | ValueNone -> ()
        } |> toJson

    let notification (methodName:string) (parameters: JToken voption) =
        seq {
            yield JProperty("jsonrpc","2.0")
            yield JProperty("method", methodName)
            match parameters with
            | ValueSome p -> yield JProperty("params", p)
            | ValueNone -> ()
        } |> toJson

// (Removed legacy manual wire protocol helpers; StreamJsonRpc now handles framing.)

// --------------------------- Execution ---------------------------

let run (opts:Options) : int =
    try
        let repoRoot = Directory.GetCurrentDirectory()
        let serverDir = Path.Combine(repoRoot, "artifacts", "bin", "FSharp.Compiler.LanguageServer", opts.Configuration, "net8.0")
        let exePath = Path.Combine(serverDir, "FSharp.Compiler.LanguageServer.exe")
        if not (File.Exists exePath) then
            Log.error ($"Language server not found at {exePath}. Build the project first."); 2
        else
            Log.info ($"Starting server: {exePath}")
            let psi = ProcessStartInfo()
            psi.FileName <- exePath
            psi.UseShellExecute <- false
            psi.RedirectStandardInput <- true
            psi.RedirectStandardOutput <- true
            psi.RedirectStandardError <- true
            psi.StandardOutputEncoding <- Encoding.UTF8
            psi.StandardErrorEncoding <- Encoding.UTF8
            let proc = Process.Start psi
            if isNull proc then Log.error "Failed to start process"; 3 else
            use _p = proc
            let stderrTask =
                if opts.DumpStderr then
                    Task.Run(fun () ->
                        try
                            while not proc.HasExited do
                                let line = proc.StandardError.ReadLine()
                                if isNull line then Thread.Sleep 10 else eprintfn "[stderr] %s" line
                        with _ -> ())
                else Task.CompletedTask

            // Setup StreamJsonRpc
            let input = proc.StandardOutput.BaseStream
            let output = proc.StandardInput.BaseStream
            let formatter = new JsonMessageFormatter()
            let handler = new HeaderDelimitedMessageHandler(output, input, formatter)
            use rpc = new JsonRpc(handler :> IJsonRpcMessageHandler)
            if opts.Verbose then
                let listener = new TextWriterTraceListener(Console.Out)
                rpc.TraceSource.Listeners.Add(listener) |> ignore
                rpc.TraceSource.Switch.Level <- SourceLevels.Off
            rpc.StartListening()

            let timeoutSec = float opts.TimeoutSeconds
            let runWithTimeout (op: unit -> Task<'T>) : Result<'T,string> =
                try
                    use cts = new CancellationTokenSource(TimeSpan.FromSeconds timeoutSec)
                    let t = op()
                    t.Wait(cts.Token)
                    Ok t.Result
                with :? OperationCanceledException -> Error "Timed out"
                   | ex -> Error ex.Message

            let initParams =
                {| processId = Process.GetCurrentProcess().Id
                   rootUri = (null : string)
                   capabilities = box {| |}
                   workspaceFolders = null
                   clientInfo = {| name = "FsxLspClient"; version = "0.1" |} |}
            if opts.Verbose then
                Log.protoOut (JsonEnvelope.request 1 "initialize" (ValueSome (JToken.FromObject initParams)))
            Log.info "Sending initialize request"
            let initOk =
                match runWithTimeout (fun () -> rpc.InvokeAsync<obj>("initialize", initParams)) with
                | Ok res ->
                    if opts.Verbose then
                        let raw =
                            match res with
                            | :? JToken as jt -> jt.ToString(Formatting.None)
                            | _ -> JsonConvert.SerializeObject(res, Formatting.None)
                        // Wrap back into a response envelope for parity with legacy script
                        let envelope = JsonConvert.SerializeObject(JObject([| JProperty("jsonrpc","2.0"); JProperty("id",1); JProperty("result", JToken.Parse(raw)) |]))
                        Log.protoIn envelope
                    Log.info "Received initialize response"
                    true
                | Error e ->
                    Log.error ($"Initialize failed: {e}")
                    false
            if not initOk then
                4
            else
                if opts.SendInitialized then
                    if opts.Verbose then
                        Log.protoOut (JsonEnvelope.notification "initialized" (ValueSome (JObject() :> JToken)))
                    Log.info "Sending initialized notification"
                    rpc.NotifyAsync("initialized", box {| |}) |> ignore
                if opts.Verbose then
                    Log.protoOut (JsonEnvelope.request 2 "shutdown" ValueNone)
                Log.info "Sending shutdown request"
                let shutdownOk =
                    match runWithTimeout (fun () -> rpc.InvokeAsync<obj>("shutdown")) with
                    | Ok res ->
                        if opts.Verbose then
                            let raw =
                                match res with
                                | :? JToken as jt -> jt.ToString(Formatting.None)
                                | _ -> JsonConvert.SerializeObject(res, Formatting.None)
                            let envelope = JsonConvert.SerializeObject(JObject([| JProperty("jsonrpc","2.0"); JProperty("id",2); JProperty("result", JToken.Parse(raw)) |]))
                            Log.protoIn envelope
                        Log.info "Shutdown response received"
                        true
                    | Error e when e.IndexOf("connection", StringComparison.OrdinalIgnoreCase) >= 0 ->
                        Log.warn ($"Shutdown response not received before connection closed; assuming success ({e})")
                        true
                    | Error e ->
                        Log.error ($"Shutdown failed: {e}")
                        false
                let exitCode = if shutdownOk then 0 else 5
                if opts.Verbose then
                    Log.protoOut (JsonEnvelope.notification "exit" ValueNone)
                Log.info "Sending exit notification"
                rpc.NotifyAsync("exit") |> ignore
                rpc.Dispose()
                let waitMs = int (TimeSpan.FromSeconds timeoutSec).TotalMilliseconds
                if not (proc.WaitForExit waitMs) then
                    Log.warn "Server did not exit in time; killing"
                    try proc.Kill(true) with _ -> ()
                stderrTask.Wait(500) |> ignore
                Log.info "Done"
                exitCode
    with ex -> Log.error ($"Unhandled exception: {ex.Message}"); 99

// --------------------------- Entry Point ---------------------------

let main argv =
    match parseArgs argv with
    | Ok opts -> run opts
    | Error e ->
        Log.error e
        Log.info "Use --configuration <cfg> --timeoutSeconds <n> [--noInitializedNotification] [--verbose] [--dumpStderr]"
        1
// Auto-invoke when running as a script (FSI) where no entry assembly is present.
do
    // Always invoke main when used as script; extract arguments after script filename
    let allArgs = Environment.GetCommandLineArgs()
    let idx = allArgs |> Array.tryFindIndex (fun s -> s.EndsWith("Run-LspInitialize-JsonRpc.fsx", StringComparison.OrdinalIgnoreCase))
    let passed =
        match idx with
        | Some i when i + 1 < allArgs.Length ->
            // Skip a standalone "--" if present
            let tail = allArgs[(i+1)..]
            if tail.Length > 0 && tail[0] = "--" then tail[1..] else tail
        | _ -> [||]
    let exitCode = main passed
    Log.info (sprintf "Script completed with exit code %d" exitCode)
