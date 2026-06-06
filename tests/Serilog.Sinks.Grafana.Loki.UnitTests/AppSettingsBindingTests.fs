module Serilog.Sinks.Grafana.Loki.Tests.AppSettingsBindingTests

open System
open System.Diagnostics
open System.IO
open System.Net
open System.Net.Sockets
open System.Text
open System.Text.Json
open System.Threading.Tasks
open Microsoft.Extensions.Configuration
open Serilog
open Swensen.Unquote
open Xunit

// These tests exercise the full Serilog.Settings.Configuration path: a logger built
// purely from JSON must discover the F# `GrafanaLoki` extension method and bind its
// flat arguments — including the `labels` / `propertiesAsLabels` arrays and the
// string-typed `period`. We prove it end-to-end by capturing the actual Loki push
// request on a loopback socket. If discovery or binding fails, no request arrives and
// the capture times out.

/// Starts a one-shot loopback HTTP server. Returns the bound port and a Task that
/// completes with the raw text (headers + body) of the first request received, after
/// replying 204 so the sink observes a successful delivery.
let private startCaptureServer () : int * Task<string> =
    let listener = new TcpListener(IPAddress.Loopback, 0)
    listener.Start()
    let port = (listener.LocalEndpoint :?> IPEndPoint).Port

    let capture =
        task {
            use! client = listener.AcceptTcpClientAsync()
            use stream = client.GetStream()
            use received = new MemoryStream()
            let buffer = Array.zeroCreate 8192
            let mutable headerEnd = -1
            let mutable contentLength = -1
            let mutable complete = false

            while not complete do
                let! n = stream.ReadAsync(buffer, 0, buffer.Length)

                if n = 0 then
                    complete <- true
                else
                    received.Write(buffer, 0, n)
                    let text = Encoding.UTF8.GetString(received.ToArray())

                    if headerEnd < 0 then
                        let idx = text.IndexOf("\r\n\r\n", StringComparison.Ordinal)

                        if idx >= 0 then
                            headerEnd <- idx + 4

                            for line in text.Substring(0, idx).Split("\r\n") do
                                if line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase) then
                                    contentLength <- int (line.Substring("Content-Length:".Length).Trim())

                    if headerEnd >= 0 then
                        let bodyLen = int received.Length - headerEnd

                        if contentLength < 0 || bodyLen >= contentLength then
                            complete <- true

            let response =
                "HTTP/1.1 204 No Content\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"
                |> Encoding.ASCII.GetBytes

            do! stream.WriteAsync(response, 0, response.Length)
            do! stream.FlushAsync()
            listener.Stop()
            return Encoding.UTF8.GetString(received.ToArray())
        }

    port, capture

let private configFromJson (json: string) : IConfiguration =
    ConfigurationBuilder().AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes json)).Build()

/// Extracts and parses the JSON push body from a captured raw HTTP request
/// (status line + headers + body). The body starts at the first '{' — HTTP headers
/// contain no braces.
let private parsePushBody (raw: string) : JsonDocument =
    JsonDocument.Parse(raw.Substring(raw.IndexOf('{')))

[<Fact>]
let ``appsettings: GrafanaLoki args (incl. credentials object) are discovered, bound, delivered`` () : Task =
    task {
        let port, capture = startCaptureServer ()

        let json =
            sprintf
                """
{
  "Serilog": {
    "Using": [ "Serilog.Sinks.Grafana.Loki" ],
    "MinimumLevel": { "Default": "Information" },
    "WriteTo": [
      {
        "Name": "GrafanaLoki",
        "Args": {
          "uri": "http://127.0.0.1:%d",
          "labels": [ { "key": "app", "value": "binding-test" } ],
          "propertiesAsLabels": [ "RequestPath" ],
          "handleLogLevelAsLabel": true,
          "batchSizeLimit": 10,
          "period": "00:00:01",
          "credentials": { "login": "binder", "password": "secret" }
        }
      }
    ]
  }
}"""
                port

        let logger =
            LoggerConfiguration().ReadFrom.Configuration(configFromJson json).CreateLogger()

        logger.Information("hello {RequestPath}", "/health")
        (logger :> IDisposable).Dispose() // flushes the batched sink

        let! body = capture.WaitAsync(TimeSpan.FromSeconds 10.0)

        test <@ body.Contains("POST /loki/api/v1/push") @> // uri bound + push path appended
        test <@ body.Contains("\"app\":\"binding-test\"") @> // labels[] bound from JSON array of objects
        test <@ body.Contains("\"RequestPath\":\"/health\"") @> // propertiesAsLabels[] bound from JSON string array
        test <@ body.Contains("\"level\":\"info\"") @> // handleLogLevelAsLabel bound (bool)

        // credentials bound from a JSON object -> the sink applies Basic auth
        let expectedAuth =
            "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("binder:secret"))

        test <@ body.Contains(expectedAuth) @>
    }

[<Fact>]
let ``appsettings: traceIdMode enum binds from its string name and emits structured metadata`` () : Task =
    task {
        let port, capture = startCaptureServer ()

        let json =
            sprintf
                """
{
  "Serilog": {
    "Using": [ "Serilog.Sinks.Grafana.Loki" ],
    "MinimumLevel": { "Default": "Information" },
    "WriteTo": [
      {
        "Name": "GrafanaLoki",
        "Args": {
          "uri": "http://127.0.0.1:%d",
          "labels": [ { "key": "app", "value": "md-test" } ],
          "traceIdMode": "StructuredMetadata",
          "propertiesAsStructuredMetadata": [ "OrderId" ]
        }
      }
    ]
  }
}"""
                port

        let logger =
            LoggerConfiguration().ReadFrom.Configuration(configFromJson json).CreateLogger()

        // A W3C activity so the emitted LogEvent carries a TraceId for the sink to route.
        use activity = (new Activity("appsettings-md")).SetIdFormat(ActivityIdFormat.W3C)
        activity.Start() |> ignore
        let expectedTrace = activity.TraceId.ToHexString()

        logger.Information("processing {OrderId}", 4242)
        activity.Stop()
        (logger :> IDisposable).Dispose() // flushes the batched sink

        let! raw = capture.WaitAsync(TimeSpan.FromSeconds 10.0)

        use doc = parsePushBody raw
        let streams = doc.RootElement.GetProperty("streams")
        let values = streams[0].GetProperty("values")
        let firstEntry = values[0]

        // Extract plain values up-front — JsonElement is a struct and cannot be
        // addressed inside an Unquote quotation.
        let elementCount = firstEntry.GetArrayLength()
        // [ ts, body, { metadata } ] — the 3rd element proves structured metadata was emitted
        test <@ elementCount = 3 @>

        let metadata = firstEntry[2]
        let mdTrace = metadata.GetProperty("TraceId").GetString()
        let mdOrder = metadata.GetProperty("OrderId").GetString()

        // traceIdMode bound from the string "StructuredMetadata"; OrderId from the array
        test <@ mdTrace = expectedTrace @>
        test <@ mdOrder = "4242" @>
    }