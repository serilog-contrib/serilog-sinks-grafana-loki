module Serilog.Sinks.Grafana.Loki.Tests.AppSettingsBindingTests

open System
open System.IO
open System.Net
open System.Net.Sockets
open System.Text
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