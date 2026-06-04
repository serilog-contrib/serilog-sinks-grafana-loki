module Serilog.Sinks.Grafana.Loki.IntegrationTests.LokiIntegrationTests

open System
open System.Net.Http
open System.Text.Json
open System.Threading.Tasks
open Swensen.Unquote
open Xunit
open Serilog.Events
open Serilog.Parsing
open Serilog.Sinks.PeriodicBatching
open Serilog.Sinks.Grafana.Loki
open LokiContainerFixture
open LokiQueryClient

// ── Test helpers ──────────────────────────────────────────────────────────────

let private parser = MessageTemplateParser()

let private mkEvent level message props =
    let properties =
        props
        |> List.map (fun (k, v) -> LogEventProperty(k, ScalarValue(v) :> LogEventPropertyValue))

    LogEvent(DateTimeOffset.UtcNow, level, null, parser.Parse(message), properties)

let private nowNs () =
    (DateTimeOffset.UtcNow - DateTimeOffset.UnixEpoch).Ticks * 100L

/// Creates a LokiSink pointed at the container and immediately flushes a batch.
let private writeThenFlush (lokiUri: string) (configure: LokiSinkOptions -> LokiSinkOptions) events =
    task {
        let options =
            LokiSinkOptions.Defaults |> (fun o -> { o with Uri = lokiUri }) |> configure

        use sink = new LokiSink(options)
        do! (sink :> IBatchedLogEventSink).EmitBatchAsync(events)
    }

// ── Integration tests (require Docker) ───────────────────────────────────────
//
// All tests share one Loki container per class (IClassFixture).
// Each test uses a unique test-run label so results don't bleed between tests.

type LokiIntegrationTests(loki: LokiFixture) =

    /// Unique run ID so parallel test runs don't interfere.
    let runId = Guid.NewGuid().ToString("N")[..7]

    let testLabel key =
        [| { Key = "testrun"; Value = runId }; { Key = "test"; Value = key } |]

    interface IClassFixture<LokiFixture>

    // ── 1. Basic end-to-end ──────────────────────────────────────────────────

    [<Fact>]
    member _.``e2e: log event is queryable in Loki after flush``() : Task =
        task {
            let startNs = nowNs ()
            let selector = $"{{testrun=\"{runId}\",test=\"e2e-basic\"}}"

            do!
                writeThenFlush
                    loki.Uri
                    (fun o ->
                        { o with
                            Labels = testLabel "e2e-basic" })
                    [ mkEvent LogEventLevel.Information "Hello from integration test" [] ]

            let! entries = waitForLogs loki.Uri selector startNs
            test <@ entries.Length = 1 @>
        }

    // ── 2. Labels queryable ──────────────────────────────────────────────────

    [<Fact>]
    member _.``labels: stream is reachable via label selector``() : Task =
        task {
            let startNs = nowNs ()
            let appVal = $"app-{runId}"
            let selector = $"{{app=\"{appVal}\"}}"

            do!
                writeThenFlush
                    loki.Uri
                    (fun o ->
                        { o with
                            Labels = [| { Key = "app"; Value = appVal } |] })
                    [ mkEvent LogEventLevel.Information "label query test" [] ]

            let! entries = waitForLogs loki.Uri selector startNs
            test <@ entries.Length >= 1 @>
        }

    // ── 3. Body survives roundtrip ───────────────────────────────────────────

    [<Fact>]
    member _.``body: log line body is valid JSON with Message field``() : Task =
        task {
            let startNs = nowNs ()
            let selector = $"{{testrun=\"{runId}\",test=\"body-roundtrip\"}}"

            do!
                writeThenFlush
                    loki.Uri
                    (fun o ->
                        { o with
                            Labels = testLabel "body-roundtrip" })
                    [ mkEvent LogEventLevel.Information "roundtrip message" [] ]

            let! entries = waitForLogs loki.Uri selector startNs
            test <@ entries.Length = 1 @>
            use body = JsonDocument.Parse(entries[0].Line)
            let hasMsg = body.RootElement.TryGetProperty("Message") |> fst
            test <@ hasMsg @>
        }

    // ── 4. Exception body survives roundtrip ─────────────────────────────────

    [<Fact>]
    member _.``body: exception field present after roundtrip``() : Task =
        task {
            let startNs = nowNs ()
            let selector = $"{{testrun=\"{runId}\",test=\"exception-roundtrip\"}}"
            let ex = InvalidOperationException("boom")

            let event =
                LogEvent(DateTimeOffset.UtcNow, LogEventLevel.Error, ex, parser.Parse("error"), [])

            do!
                writeThenFlush
                    loki.Uri
                    (fun o ->
                        { o with
                            Labels = testLabel "exception-roundtrip" })
                    [ event ]

            let! entries = waitForLogs loki.Uri selector startNs
            test <@ entries.Length = 1 @>
            use body = JsonDocument.Parse(entries[0].Line)
            let hasEx = body.RootElement.TryGetProperty("Exception") |> fst
            test <@ hasEx @>
        }

    // ── 5. Second batch appends ──────────────────────────────────────────────

    [<Fact>]
    member _.``streaming: two consecutive flushes both appear in Loki``() : Task =
        task {
            let startNs = nowNs ()
            let selector = $"{{testrun=\"{runId}\",test=\"two-batches\"}}"
            let labels = testLabel "two-batches"

            do!
                writeThenFlush
                    loki.Uri
                    (fun o -> { o with Labels = labels })
                    [ mkEvent LogEventLevel.Information "batch 1" [] ]

            do!
                writeThenFlush
                    loki.Uri
                    (fun o -> { o with Labels = labels })
                    [ mkEvent LogEventLevel.Information "batch 2" [] ]

            let! entries = waitForLogs loki.Uri selector startNs
            test <@ entries.Length = 2 @>
        }

    // ── 6. Fatal label value ─────────────────────────────────────────────────

    [<Fact>]
    member _.``labels: Fatal level stored as "fatal" (V9 change from "critical")``() : Task =
        task {
            let startNs = nowNs ()
            let selector = $"{{testrun=\"{runId}\",test=\"fatal-label\",level=\"fatal\"}}"

            do!
                writeThenFlush
                    loki.Uri
                    (fun o ->
                        { o with
                            Labels = testLabel "fatal-label" })
                    [ mkEvent LogEventLevel.Fatal "fatal event" [] ]

            let! entries = waitForLogs loki.Uri selector startNs
            // If level mapped to "critical" the selector wouldn't match; length > 0 proves "fatal"
            test <@ entries.Length = 1 @>
        }

    // ── 7. Large batch ───────────────────────────────────────────────────────

    [<Fact>]
    member _.``throughput: batch of 200 events all arrive in Loki``() : Task =
        task {
            let startNs = nowNs ()
            let selector = $"{{testrun=\"{runId}\",test=\"large-batch\"}}"

            let events =
                [ for i in 1..200 -> mkEvent LogEventLevel.Information $"event {i}" [] ]

            do!
                writeThenFlush
                    loki.Uri
                    (fun o ->
                        { o with
                            Labels = testLabel "large-batch" })
                    events

            let! entries = waitForLogs loki.Uri selector startNs
            test <@ entries.Length = 200 @>
        }

    // ── 8. Gzip via DelegatingHandler ────────────────────────────────────────

    [<Fact>]
    member _.``gzip: compressed request accepted by Loki``() : Task =
        task {
            let startNs = nowNs ()
            let selector = $"{{testrun=\"{runId}\",test=\"gzip\"}}"

            // GzipHandler compresses the request body before sending.
            let gzip = new GzipRequestHandler(new HttpClientHandler())

            do!
                writeThenFlush
                    loki.Uri
                    (fun o ->
                        { o with
                            Labels = testLabel "gzip"
                            HttpMessageHandler = gzip })
                    [ mkEvent LogEventLevel.Information "gzip test" [] ]

            let! entries = waitForLogs loki.Uri selector startNs
            test <@ entries.Length = 1 @>
        }

// ── GzipRequestHandler ───────────────────────────────────────────────────────
// A minimal DelegatingHandler that gzip-compresses the request body.
// Demonstrates the pattern for users who want compression without a
// separate LokiGzipHttpClient class (the V8 approach we dropped).

and GzipRequestHandler(inner: HttpMessageHandler) =
    inherit DelegatingHandler(inner)

    // Protected base.SendAsync cannot be called from inside a task CE (F# closure restriction).
    // Wrapping it in a regular method makes it callable from within the computation expression.
    member private self.Delegate(req, ct) = base.SendAsync(req, ct)

    override self.SendAsync(request, ct) =
        task {
            if not (isNull request.Content) then
                let! bytes = request.Content.ReadAsByteArrayAsync(ct)
                use ms = new IO.MemoryStream()
                use gz = new IO.Compression.GZipStream(ms, IO.Compression.CompressionLevel.Fastest)
                do! gz.WriteAsync(bytes, 0, bytes.Length, ct)
                gz.Close()
                let compressed = ms.ToArray()
                request.Content <- new ByteArrayContent(compressed)

                request.Content.Headers.ContentType <-
                    Net.Http.Headers.MediaTypeHeaderValue("application/json", CharSet = "utf-8")

                request.Content.Headers.Add("Content-Encoding", "gzip")

            return! self.Delegate(request, ct)
        }
