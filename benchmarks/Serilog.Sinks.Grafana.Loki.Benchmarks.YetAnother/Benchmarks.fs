// Copyright 2020-2026 Mykhailo Shevchuk & Contributors
//
// Licensed under the MIT license;
// you may not use this file except in compliance with the License.
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See LICENSE file in the project root for full license information.

namespace Benchmarks

open System
open System.IO
open System.Net
open System.Net.Http
open BenchmarkDotNet.Attributes
open Serilog
open Serilog.Core
open Serilog.Events
open Serilog.Sinks.Loki
open Benchmarks.Shared

/// In-process transport for Serilog.Sinks.Loki.YetAnother: drains the request body
/// (forcing its streaming serialization to run) and returns 204 like Loki on success.
/// Injected by wrapping it in the HttpClient the sink accepts — the same deterministic,
/// socketless transport used for the V8 and V9 benchmarks.
type private Fake204Handler() =
    inherit HttpMessageHandler()

    override _.SendAsync(request, cancellationToken) =
        task {
            if not (isNull request.Content) then
                do! request.Content.CopyToAsync(Stream.Null, cancellationToken)

            return new HttpResponseMessage(HttpStatusCode.NoContent)
        }

// ── End-to-end sink push (real production serialization + batching) ────────────────
// YetAnother exposes no public per-event formatter (its message writer is internal),
// so only the end-to-end group has a fair equivalent here. Same workload, same fake
// transport, same batching settings as the V8/V9 SinkBenchmarks.

[<Config(typeof<Config.SinkConfig>)>]
type SinkBenchmarks() =
    let target = Environment.GetEnvironmentVariable "LOKI_BENCH_TARGET"
    let useReal = not (String.IsNullOrWhiteSpace target)

    let mutable events: LogEvent[] = [||]
    let mutable logger: Logger = null
    // The sink does not own an injected HttpClient, so we create and dispose ours.
    let mutable client: HttpClient = null

    [<Params(1000, 10000)>]
    member val EventCount = 1000 with get, set

    [<Params("Simple", "Exception")>]
    member val Payload = "Simple" with get, set

    [<IterationSetup>]
    member this.IterationSetup() =
        events <-
            if this.Payload = "Exception" then
                EventGen.buildWithException this.EventCount
            else
                EventGen.buildSimple this.EventCount

        let uri = if useReal then target else "http://localhost:9999"

        let cfg =
            LokiSinkConfigurations(
                Url = Uri(uri),
                Labels = [| LokiLabel("app", "benchmarks") |],
                HandleLogLevelAsLabel = true
            )

        let lc = LoggerConfiguration()

        let configured =
            if useReal then
                lc.WriteTo.Loki(
                    cfg,
                    batchSizeLimit = 1000,
                    queueLimit = 10_000_000,
                    period = Nullable(TimeSpan.FromHours 1.0)
                )
            else
                client <- new HttpClient(new Fake204Handler())

                lc.WriteTo.Loki(
                    cfg,
                    batchSizeLimit = 1000,
                    queueLimit = 10_000_000,
                    period = Nullable(TimeSpan.FromHours 1.0),
                    httpClient = client
                )

        logger <- configured.CreateLogger()

    [<Benchmark>]
    member _.Push() =
        for e in events do
            logger.Write(e)

        // Disposing the logger flushes the batch synchronously (serialize + POST).
        logger.Dispose()

        if not (isNull client) then
            client.Dispose()
            client <- null