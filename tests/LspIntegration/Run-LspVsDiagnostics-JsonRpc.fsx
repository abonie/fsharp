#!/usr/bin/env -S dotnet fsi

(***
    Run-LspVsDiagnostics-JsonRpc.fsx

    Tests the internal Visual Studio LSP endpoint: textDocument/_vs_diagnostic
    Flow: initialize (with VS extended capabilities) -> (initialized) -> didOpen -> textDocument/_vs_diagnostic -> shutdown -> exit

    Usage (from repo root):
        dotnet fsi tests/LspIntegration/Run-LspVsDiagnostics-JsonRpc.fsx -- --configuration Debug --timeoutSeconds 15 --verbose

    Flags:
      --configuration <Debug|Release> (default Debug)
      --timeoutSeconds <int>          (default 15)
      --noInitializedNotification     (omit the initialized notification)
      --verbose                       (log protocol traffic)
      --dumpStderr                    (mirror server stderr to console)

    Exit codes:
      0  success
      2  server not found
      3  start failure
      4  initialize failed
      10 unexpected missing diagnostic report / missing `kind`
      11 diagnostic request timeout
      12 diagnostic report shape mismatch
      99 unhandled exception
***)

#load "Common.fsx"
open Common

open System
open System.IO
open System.Diagnostics
open System.Threading
open System.Threading.Tasks
open Newtonsoft.Json
open Newtonsoft.Json.Linq
open StreamJsonRpc

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

// Diagnostic report validator (reused logic)
let validateDiagnosticReport (report: JToken) : Result<string * int * int * int, string> =
    let kind = report.["kind"]
    if isNull kind then
        Error "Missing 'kind' field in diagnostic report"
    else
        let kindStr = kind.ToString()
        match kindStr with
        | "full" ->
            let items = report.["items"]
            if isNull items then
                Error "Missing 'items' field in full diagnostic report"
            else
                let itemsArray = items :?> JArray
                let itemCount = itemsArray.Count
                let mutable errorCount = 0
                let mutable warningCount = 0
                for item in itemsArray do
                    let severity = item.["severity"]
                    if not (isNull severity) then
                        match severity.ToObject<int>() with
                        | 1 -> errorCount <- errorCount + 1
                        | 2 -> warningCount <- warningCount + 1
                        | _ -> ()
                Ok (kindStr, itemCount, errorCount, warningCount)
        | "unchanged" ->
            let resultId = report.["resultId"]
            if isNull resultId then Error "Missing 'resultId' field in unchanged diagnostic report" else Ok (kindStr,0,0,0)
        | _ -> Error ($"Unknown diagnostic report kind: {kindStr}")

let run (opts: Options) : int =
    try
        match startServer opts.Configuration with
        | Error msg -> Log.error msg; 2
        | Ok (proc, rpc) ->
            use _p = proc
            use _r = rpc

            let stderrTask =
                if opts.DumpStderr then
                    Task.Run(fun () ->
                        try
                            while not proc.HasExited do
                                let line = proc.StandardError.ReadLine()
                                if isNull line then Thread.Sleep 10 else eprintfn "[stderr] %s" line
                        with _ -> ())
                else Task.CompletedTask

            // Workspace/test file setup (same as standard diagnostics script)
            let repoRoot = Directory.GetCurrentDirectory()
            let workspaceDir = Path.Combine(repoRoot, "tests", "LspIntegration", "DiagnosticWorkspace")
            let filePath = Path.Combine(workspaceDir, "DiagnosticSample.fs")
            let fileUri = Uri($"file:///{filePath.Replace('\\','/')}")
            let rootDir = workspaceDir
            let rootUriStr = Uri($"file:///{rootDir.Replace('\\','/').TrimEnd('/')}/").ToString().TrimEnd('/')
            let workspaceName = Path.GetFileName(rootDir.TrimEnd(Path.DirectorySeparatorChar))

            // Add VS extended capabilities
            let initParams =
                {| processId = Process.GetCurrentProcess().Id
                   rootUri = rootUriStr
                   capabilities =
                       box
                           {| workspace = {| workspaceFolders = true |}
                              textDocument =
                                  {| diagnostic =
                                         {| dynamicRegistration = false
                                            relatedDocumentSupport = false |} |}
                              _vs_supportsDiagnosticRequests = true
                              _vs_diagnosticProvider = {| _vs_diagnosticKinds = [| "syntax" |] |}
                           |}
                   workspaceFolders = [| {| uri = rootUriStr; name = workspaceName |} |]
                   clientInfo = {| name = "FsxLspVsDiagnosticsClient"; version = "0.1" |} |}

            if opts.Verbose then
                Log.protoOut (JsonEnvelope.request 1 "initialize" (ValueSome (JToken.FromObject initParams)))

            Log.info "Sending initialize request"
            let initOk =
                match runWithTimeout opts.TimeoutSeconds (fun () -> rpc.InvokeAsync<obj>("initialize", initParams)) with
                | Ok res ->
                    if opts.Verbose then
                        let raw = match res with | :? JToken as jt -> jt.ToString(Formatting.None) | _ -> JsonConvert.SerializeObject(res, Formatting.None)
                        let envelope = JsonConvert.SerializeObject(JObject([| JProperty("jsonrpc","2.0"); JProperty("id",1); JProperty("result", JToken.Parse(raw)) |]))
                        Log.protoIn envelope
                    Log.info "Received initialize response"; true
                | Error e -> Log.error ($"Initialize failed: {e}"); false

            if not initOk then 4 else

            if opts.SendInitialized then
                if opts.Verbose then Log.protoOut (JsonEnvelope.notification "initialized" (ValueSome (JObject() :> JToken)))
                Log.info "Sending initialized notification"
                rpc.NotifyAsync("initialized", box {| |}) |> ignore

            Log.info ($"Using workspace file: {filePath}")
            try
                // didOpen
                let fileText = File.ReadAllText filePath
                let didOpenParams = {| textDocument = {| uri = fileUri.ToString(); languageId = "fsharp"; version = 1; text = fileText |} |}
                if opts.Verbose then Log.protoOut (JsonEnvelope.notification "textDocument/didOpen" (ValueSome (JToken.FromObject didOpenParams)))
                Log.info "Sending textDocument/didOpen notification"
                rpc.NotifyAsync("textDocument/didOpen", didOpenParams) |> ignore
                Thread.Sleep 800

                // VS diagnostic request
                let diagnosticParams = {| textDocument = {| uri = fileUri.ToString() |} |}
                if opts.Verbose then Log.protoOut (JsonEnvelope.request 2 "textDocument/_vs_diagnostic" (ValueSome (JToken.FromObject diagnosticParams)))
                Log.info "Sending textDocument/_vs_diagnostic request"
                let diagnosticOk =
                    match runWithTimeout opts.TimeoutSeconds (fun () -> rpc.InvokeAsync<obj>("textDocument/_vs_diagnostic", diagnosticParams)) with
                    | Ok res ->
                        if opts.Verbose then
                            let raw = match res with | :? JToken as jt -> jt.ToString(Formatting.None) | _ -> JsonConvert.SerializeObject(res, Formatting.None)
                            let envelope = JsonConvert.SerializeObject(JObject([| JProperty("jsonrpc","2.0"); JProperty("id",2); JProperty("result", JToken.Parse(raw)) |]))
                            Log.protoIn envelope
                        let resultObj = JToken.Parse(JsonConvert.SerializeObject(res))
                        match validateDiagnosticReport resultObj with
                        | Ok (kind, itemCount, errorCount, warningCount) ->
                            Log.info ($"VS DiagnosticsReport kind={kind} items={itemCount} errors={errorCount} warnings={warningCount}")
                            true
                        | Error e -> Log.error ($"Diagnostic report validation failed: {e}"); false
                    | Error e when e.IndexOf("timeout", StringComparison.OrdinalIgnoreCase) >= 0 -> Log.error ($"Diagnostic request timed out: {e}"); false
                    | Error e -> Log.error ($"Diagnostic request failed: {e}"); false
                if not diagnosticOk then 12 else

                // shutdown
                if opts.Verbose then Log.protoOut (JsonEnvelope.request 3 "shutdown" ValueNone)
                Log.info "Sending shutdown request"
                let shutdownOk =
                    match runWithTimeout opts.TimeoutSeconds (fun () -> rpc.InvokeAsync<obj>("shutdown")) with
                    | Ok res ->
                        if opts.Verbose then
                            let raw = match res with | :? JToken as jt -> jt.ToString(Formatting.None) | _ -> JsonConvert.SerializeObject(res, Formatting.None)
                            let envelope = JsonConvert.SerializeObject(JObject([| JProperty("jsonrpc","2.0"); JProperty("id",3); JProperty("result", JToken.Parse(raw)) |]))
                            Log.protoIn envelope
                        Log.info "Shutdown response received"; true
                    | Error e when e.IndexOf("connection", StringComparison.OrdinalIgnoreCase) >= 0 -> Log.warn ($"Shutdown before connection closed ({e})"); true
                    | Error e -> Log.error ($"Shutdown failed: {e}"); false
                if opts.Verbose then Log.protoOut (JsonEnvelope.notification "exit" ValueNone)
                Log.info "Sending exit notification"
                rpc.NotifyAsync("exit") |> ignore
                let waitMs = int (TimeSpan.FromSeconds(float opts.TimeoutSeconds)).TotalMilliseconds
                if not (proc.WaitForExit waitMs) then
                    Log.warn "Server did not exit in time; killing"; try proc.Kill(true) with _ -> ()
                stderrTask.Wait(300) |> ignore
                if shutdownOk then 0 else 5
            finally ()
    with ex -> Log.error ($"Unhandled exception: {ex.Message}"); 99

let main argv =
    match parseArgs argv with
    | Ok opts -> run opts
    | Error e -> Log.error e; Log.info "Use --configuration <cfg> --timeoutSeconds <n> [--noInitializedNotification] [--verbose] [--dumpStderr]"; 1

// Auto invoke
let _ =
    let allArgs = Environment.GetCommandLineArgs()
    let idx = allArgs |> Array.tryFindIndex (fun s -> s.EndsWith("Run-LspVsDiagnostics-JsonRpc.fsx", StringComparison.OrdinalIgnoreCase))
    let passed =
        match idx with
        | Some i when i + 1 < allArgs.Length ->
            let tail = allArgs[(i+1)..]
            if tail.Length > 0 && tail[0] = "--" then tail[1..] else tail
        | _ -> [||]
    let exitCode = main passed
    Log.info (sprintf "Script completed with exit code %d" exitCode)
