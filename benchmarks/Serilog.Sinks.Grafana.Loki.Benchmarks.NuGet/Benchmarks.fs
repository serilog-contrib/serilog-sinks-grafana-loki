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
open System.Globalization
open System.IO
open System.Net
open System.Net.Http
open BenchmarkDotNet.Attributes
open Serilog
open Serilog.Core
open Serilog.Events
open Serilog.Formatting
open Serilog.Formatting.Display
open Serilog.Sinks.Grafana.Loki
open Benchmarks.Shared

/// In-process transport for the baseline sink (the published NuGet package): drains the
/// request body — which forces the streaming serialization in LokiPushContent to run —
/// then returns 204, exactly like Loki's success response. No sockets, fully
/// deterministic. Injected via the sink's `httpMessageHandler` option, so the sink owns
/// the HttpClient and runs its real path.
type private Fake204Handler() =
    inherit HttpMessageHandler()

    override _.SendAsync(request, cancellationToken) =
        task {
            if not (isNull request.Content) then
                do! request.Content.CopyToAsync(Stream.Null, cancellationToken)

            return new HttpResponseMessage(HttpStatusCode.NoContent)
        }

// ── Group 1: per-event body formatter (public ITextFormatter surface) ─────────────
// Measures the baseline package's public formatter. Like the Current project, this is
// a conservative view: the sink uses the internal byte-oriented path in production,
// which is measured end to end in Group 2.

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

// Shared setup for the two end-to-end groups below. By default the fake 204 transport
// keeps the measurement deterministic; set LOKI_BENCH_TARGET (e.g. http://localhost:3100)
// to push to a real Loki started via docker-compose instead.
module private SinkSetup =
    let private target = Environment.GetEnvironmentVariable "LOKI_BENCH_TARGET"
    let private useReal = not (String.IsNullOrWhiteSpace target)
    let private label: LokiLabel = { Key = "app"; Value = "benchmarks" }

    let buildEvents (payload: string) (count: int) =
        if payload = "Exception" then
            EventGen.buildWithException count
        else
            EventGen.buildSimple count

    /// A null textFormatter selects the sink's built-in LokiJsonTextFormatter.
    let buildLogger (textFormatter: ITextFormatter) =
        let cfg = LoggerConfiguration()

        let configured =
            if useReal then
                cfg.WriteTo.GrafanaLoki(
                    target,
                    labels = [| label |],
                    textFormatter = textFormatter,
                    batchSizeLimit = 1000,
                    queueLimit = 10_000_000,
                    period = Nullable(TimeSpan.FromHours 1.0)
                )
            else
                cfg.WriteTo.GrafanaLoki(
                    "http://localhost:9999",
                    labels = [| label |],
                    textFormatter = textFormatter,
                    httpMessageHandler = (new Fake204Handler() :> HttpMessageHandler),
                    batchSizeLimit = 1000,
                    queueLimit = 10_000_000,
                    period = Nullable(TimeSpan.FromHours 1.0)
                )

        configured.CreateLogger()

// ── Group 2: end-to-end sink push (real production serialization + batching) ──────
// Drives the public WriteTo.GrafanaLoki pipeline with the built-in formatter, which the
// sink serializes through its internal FormatToBuffer fast path.

[<Config(typeof<Config.SinkConfig>)>]
type SinkBenchmarks() =
    let mutable events: LogEvent[] = [||]
    let mutable logger: Logger = null

    [<Params(1000, 10000)>]
    member val EventCount = 1000 with get, set

    [<Params("Simple", "Exception")>]
    member val Payload = "Simple" with get, set

    [<IterationSetup>]
    member this.IterationSetup() =
        events <- SinkSetup.buildEvents this.Payload this.EventCount
        logger <- SinkSetup.buildLogger null

    [<Benchmark>]
    member _.Push() =
        for e in events do
            logger.Write(e)

        // Disposing the logger flushes the batching sink synchronously — this is where
        // the batch is serialized and POSTed, so it must be inside the measured region.
        logger.Dispose()

// ── Group 3: end-to-end sink push through a custom ITextFormatter ─────────────────
// Group 2 only ever exercises the built-in formatter. A user-supplied ITextFormatter
// takes a different route through the serializer, so without this group nothing in CI
// watches that path, and an allocation regression there is invisible.

[<Config(typeof<Config.SinkConfig>)>]
type CustomFormatterSinkBenchmarks() =
    // Serilog's stock output template: by far the most common custom formatter, and the
    // shape reported in #347. Invariant culture keeps rendering agent-independent.
    let formatter =
        MessageTemplateTextFormatter(
            "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}",
            CultureInfo.InvariantCulture
        )

    let mutable events: LogEvent[] = [||]
    let mutable logger: Logger = null

    [<Params(1000)>]
    member val EventCount = 1000 with get, set

    [<Params("Simple", "Exception")>]
    member val Payload = "Simple" with get, set

    [<IterationSetup>]
    member this.IterationSetup() =
        events <- SinkSetup.buildEvents this.Payload this.EventCount
        logger <- SinkSetup.buildLogger formatter

    [<Benchmark>]
    member _.Push() =
        for e in events do
            logger.Write(e)

        logger.Dispose()