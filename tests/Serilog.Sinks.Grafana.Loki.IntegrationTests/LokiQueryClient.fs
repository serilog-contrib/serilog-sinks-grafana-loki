module Serilog.Sinks.Grafana.Loki.IntegrationTests.LokiQueryClient

open System
open System.Net.Http
open System.Text.Json
open System.Threading.Tasks

/// A single log entry returned by Loki's query API.
type LokiLogEntry = {
    TimestampNs : int64
    Line        : string
}

/// Parses Loki's query_range JSON response into a flat list of log entries.
/// Response shape: { data: { result: [ { stream: {}, values: [["ts","line"],...] } ] } }
let private parseQueryResponse (json: string) : LokiLogEntry list =
    use doc = JsonDocument.Parse(json)
    let result =
        doc.RootElement
           .GetProperty("data")
           .GetProperty("result")
    [ for stream in result.EnumerateArray() do
        for value in stream.GetProperty("values").EnumerateArray() do
            let entry = value[0]   // intermediate binding for chained index
            yield {
                TimestampNs = entry.GetString() |> Int64.Parse
                Line        = value[1].GetString()
            } ]

/// Queries Loki for logs matching labelSelector between startNs and endNs (nanoseconds).
let queryRange (lokiUri: string) (labelSelector: string) (startNs: int64) (endNs: int64) = task {
    use client = new HttpClient()
    let url =
        $"{lokiUri}/loki/api/v1/query_range" +
        $"?query={Uri.EscapeDataString(labelSelector)}" +
        $"&start={startNs}&end={endNs}" +
        "&limit=1000&direction=forward"
    let! json = client.GetStringAsync(url)
    return parseQueryResponse json
}

/// Polls Loki every 300 ms until logs appear or the attempt budget is exhausted.
/// Loki ingests asynchronously so writes are not immediately queryable.
let rec pollForLogs
    (lokiUri: string)
    (labelSelector: string)
    (startNs: int64)
    (endNs: int64)
    (attemptsLeft: int) : Task<LokiLogEntry list> = task {
    let! entries = queryRange lokiUri labelSelector startNs endNs
    if entries.IsEmpty && attemptsLeft > 0 then
        do! Task.Delay(300)
        return! pollForLogs lokiUri labelSelector startNs endNs (attemptsLeft - 1)
    else
        return entries
}

/// Waits up to ~5 seconds (16 × 300 ms) for logs matching the given label selector.
let waitForLogs (lokiUri: string) (labelSelector: string) (startNs: int64) =
    let endNs = DateTimeOffset.UtcNow.AddMinutes(1.0).ToUnixTimeMilliseconds() * 1_000_000L
    pollForLogs lokiUri labelSelector startNs endNs 16
