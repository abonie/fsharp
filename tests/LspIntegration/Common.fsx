// Common.fsx
// Shared helpers for LSP integration tests

open System
open System.IO
open System.Text
open System.Diagnostics
open System.Threading
open System.Threading.Tasks

// StreamJsonRpc references (paths adjusted for tests/LspIntegration location)
#r "../../artifacts/bin/FSharp.Compiler.LanguageServer/Debug/net8.0/StreamJsonRpc.dll"
#r "../../artifacts/bin/FSharp.Compiler.LanguageServer/Debug/net8.0/Microsoft.VisualStudio.Validation.dll"
#r "../../artifacts/bin/FSharp.Compiler.LanguageServer/Debug/net8.0/System.IO.Pipelines.dll"
#r "../../artifacts/bin/FSharp.Compiler.LanguageServer/Debug/net8.0/Newtonsoft.Json.dll"

open StreamJsonRpc
open Newtonsoft.Json
open Newtonsoft.Json.Linq

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

// --------------------------- Timeout Helper ---------------------------

let runWithTimeout (timeoutSeconds: int) (op: unit -> Task<'T>) : Result<'T,string> =
    try
        use cts = new CancellationTokenSource(TimeSpan.FromSeconds(float timeoutSeconds))
        let t = op()
        t.Wait(cts.Token)
        Ok t.Result
    with :? OperationCanceledException -> Error "Timed out"
       | ex -> Error ex.Message

// --------------------------- Temp File Helper ---------------------------

let makeTempFile (extension: string) (contents: string) : Uri * string =
    let tempDir = Path.GetTempPath()
    let fileName = $"lsp-test-{Guid.NewGuid()}.{extension.TrimStart('.')}"
    let filePath = Path.Combine(tempDir, fileName)
    File.WriteAllText(filePath, contents, Encoding.UTF8)
    let uri = Uri($"file:///{filePath.Replace('\\', '/')}")
    (uri, filePath)

// --------------------------- Server Startup Helper ---------------------------

let startServer (configuration: string) : Result<Process * JsonRpc, string> =
    try
        // Assume invocation from repo root for relative artifact paths
        let repoRoot = Directory.GetCurrentDirectory()
        let serverDir = Path.Combine(repoRoot, "artifacts", "bin", "FSharp.Compiler.LanguageServer", configuration, "net8.0")
        let exePath = Path.Combine(serverDir, "FSharp.Compiler.LanguageServer.exe")
        
        if not (File.Exists exePath) then
            Error ($"Language server not found at {exePath}. Build the project first.")
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
            if isNull proc then 
                Error "Failed to start process"
            else
                // Setup StreamJsonRpc
                let input = proc.StandardOutput.BaseStream
                let output = proc.StandardInput.BaseStream
                let formatter = new JsonMessageFormatter()
                let handler = new HeaderDelimitedMessageHandler(output, input, formatter)
                let rpc = new JsonRpc(handler :> IJsonRpcMessageHandler)
                rpc.StartListening()
                
                Ok (proc, rpc)
    with ex -> Error ex.Message