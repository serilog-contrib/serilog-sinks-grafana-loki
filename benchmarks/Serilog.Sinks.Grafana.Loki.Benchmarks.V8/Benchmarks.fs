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
open Serilog.Sinks.Grafana.Loki
open Benchmarks.Shared

/// In-process transport for the V8 sink: implements V8's ILokiHttpClient injection
/// point, drains the serialized content stream and returns 204 (Loki's success code).
/// No sockets, fully deterministic. The V8 sink has already run LokiBatchFormatter to
/// produce the stream by the time PostAsync is called, so serialization is fully measured.
type private Fake204LokiHttpClient() =
    interface ILokiHttpClient with
        member _.PostAsync(_requestUri, contentStream) =
            task {
                do! contentStream.CopyToAsync(Stream.Null)
                return new HttpResponseMessage(HttpStatusCode.NoContent)
            }

        member _.SetCredentials(_credentials) = ()
        member _.SetTenant(_tenant) = ()

    interface IDisposable with
        member _.Dispose() = ()

// ── Group 1: per-event body formatter (public ITextFormatter surface) ─────────────
// V8's LokiJsonTextFormatter is what its sink uses per event (StringWriter -> string),
// so this is V8's true per-event path. Compared against V9's public formatter.

[<Config(typeof<Config.MicroConfig>)>]
type FormatterBenchmarks() =
    let formatter = LokiJsonTextFormatter()
    let mutable simple: LogEvent[] = [||]
    let mutable withException: LogEvent[] = [||]

    [<Params(1000)>]
    member val EventCount = 1000 with get, set

    [<GlobalSetup>]
    member this.Setup() =
        simple <- EventGen.buildSimple this.EventCount
        withException <- EventGen.buildWithException this.EventCount

    member private _.FormatAll(events: LogEvent[]) =
        let mutable total = 0

        for e in events do
            use writer = new StringWriter()
            formatter.Format(e, writer)
            total <- total + writer.GetStringBuilder().Length

        total

    [<Benchmark>]
    member this.Format_Simple() = this.FormatAll(simple)

    [<Benchmark>]
    member this.Format_Exception() = this.FormatAll(withException)

// ── Group 2: end-to-end sink push (real production serialization + batching) ──────
// Drives the public WriteTo.GrafanaLoki pipeline. By default the fake 204 transport
// keeps the measurement deterministic; set LOKI_BENCH_TARGET (e.g. http://localhost:3100)
// to push to a real Loki started via docker-compose instead.

[<Config(typeof<Config.SinkConfig>)>]
type SinkBenchmarks() =
    let target = Environment.GetEnvironmentVariable "LOKI_BENCH_TARGET"
    let useReal = not (String.IsNullOrWhiteSpace target)
    let label = LokiLabel(Key = "app", Value = "benchmarks")

    let mutable events: LogEvent[] = [||]
    let mutable logger: Logger = null

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

        let cfg = LoggerConfiguration()

        let configured =
            if useReal then
                cfg.WriteTo.GrafanaLoki(
                    target,
                    labels = [| label |],
                    batchPostingLimit = 1000,
                    queueLimit = Nullable 10_000_000,
                    period = Nullable(TimeSpan.FromHours 1.0)
                )
            else
                cfg.WriteTo.GrafanaLoki(
                    "http://localhost:9999",
                    labels = [| label |],
                    httpClient = (new Fake204LokiHttpClient() :> ILokiHttpClient),
                    batchPostingLimit = 1000,
                    queueLimit = Nullable 10_000_000,
                    period = Nullable(TimeSpan.FromHours 1.0)
                )

        logger <- configured.CreateLogger()

    [<Benchmark>]
    member _.Push() =
        for e in events do
            logger.Write(e)

        // Disposing the logger flushes the batching sink synchronously — this is where
        // the batch is serialized and POSTed, so it must be inside the measured region.
        logger.Dispose()