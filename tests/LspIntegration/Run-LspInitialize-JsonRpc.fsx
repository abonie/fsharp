#!/usr/bin/env -S dotnet fsi

// Load shared helpers first so their #r references are available before opens
// Load shared helpers (resolved relative to this script directory)
#load "Common.fsx"
open Common

(***
    Run-LspInitialize-JsonRpc.fsx

    Reimplementation of PowerShell script Run-LspInitialize.ps1 in idiomatic F#.
    Responsibilities:
      * Parse command-line arguments
      * Locate and launch FSharp.Compiler.LanguageServer executable
      * Perform minimal LSP handshake: initialize -> (initialized) -> shutdown -> exit
      * Support verbosity, stderr dumping, timeout, and skipping initialized notification

        Usage (from repo root):
            dotnet fsi tests/LspIntegration/Run-LspInitialize-JsonRpc.fsx -- --configuration Debug --timeoutSeconds 15 --verbose --dumpStderr

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
open System.Diagnostics
open System.Threading
open System.Threading.Tasks
open Newtonsoft.Json
open Newtonsoft.Json.Linq
open StreamJsonRpc

// Modules from Common.fsx: Log, JsonEnvelope, runWithTimeout, startServer

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

// --------------------------- Execution ---------------------------

let run (opts:Options) : int =
    try
        match startServer opts.Configuration with
        | Error e -> Log.error e; 2
        | Ok (proc, rpc) ->
            use _p = proc
            use _rpc = rpc

            // Optional stderr tail
            let stderrTask =
                if opts.DumpStderr then
                    Task.Run(fun () ->
                        try
                            while not proc.HasExited do
                                let line = proc.StandardError.ReadLine()
                                if isNull line then Thread.Sleep 10 else eprintfn "[stderr] %s" line
                        with _ -> ())
                else Task.CompletedTask

            if opts.Verbose then
                let listener = new TextWriterTraceListener(Console.Out)
                rpc.TraceSource.Listeners.Add(listener) |> ignore
                rpc.TraceSource.Switch.Level <- SourceLevels.Off

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
                match runWithTimeout opts.TimeoutSeconds (fun () -> rpc.InvokeAsync<obj>("initialize", initParams)) with
                | Ok res ->
                    if opts.Verbose then
                        let rawJson = JsonConvert.SerializeObject(res, Formatting.None)
                        let envelope =
                            JsonConvert.SerializeObject(
                                JObject([| JProperty("jsonrpc","2.0"); JProperty("id",1); JProperty("result", JToken.Parse(rawJson)) |])
                            )
                        Log.protoIn envelope
                    Log.info "Received initialize response"
                    true
                | Error e -> Log.error ($"Initialize failed: {e}"); false

            if not initOk then 4 else
            // initialized notification
            if opts.SendInitialized then
                if opts.Verbose then
                    Log.protoOut (JsonEnvelope.notification "initialized" (ValueSome (JObject() :> JToken)))
                Log.info "Sending initialized notification"
                rpc.NotifyAsync("initialized", box {| |}) |> ignore

            // shutdown
            if opts.Verbose then Log.protoOut (JsonEnvelope.request 2 "shutdown" ValueNone)
            Log.info "Sending shutdown request"
            let shutdownOk =
                match runWithTimeout opts.TimeoutSeconds (fun () -> rpc.InvokeAsync<obj>("shutdown")) with
                | Ok res ->
                    if opts.Verbose then
                        let rawJson = JsonConvert.SerializeObject(res, Formatting.None)
                        let envelope =
                            JsonConvert.SerializeObject(
                                JObject([| JProperty("jsonrpc","2.0"); JProperty("id",2); JProperty("result", JToken.Parse(rawJson)) |])
                            )
                        Log.protoIn envelope
                    Log.info "Shutdown response received"; true
                | Error e when e.IndexOf("connection", StringComparison.OrdinalIgnoreCase) >= 0 ->
                    Log.warn ($"Shutdown response not received before connection closed; assuming success ({e})"); true
                | Error e -> Log.error ($"Shutdown failed: {e}"); false

            let exitCode = if shutdownOk then 0 else 5

            // exit notification
            if opts.Verbose then Log.protoOut (JsonEnvelope.notification "exit" ValueNone)
            Log.info "Sending exit notification"
            rpc.NotifyAsync("exit") |> ignore

            // Graceful wait
            if not (proc.WaitForExit (opts.TimeoutSeconds * 1000)) then
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
