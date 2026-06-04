module Serilog.Sinks.Grafana.Loki.Tests.WireFormatTests

open System
open System.Diagnostics
open System.Net
open System.Net.Http
open System.Text
open System.Text.Json
open System.Collections.Generic
open System.Threading.Tasks
open Swensen.Unquote
open Xunit
open Serilog.Events
open Serilog.Parsing
open Serilog.Core
open Serilog.Sinks.Grafana.Loki
open Serilog.Sinks.Grafana.Loki.Tests.Helpers

// ── Helpers for trace-aware LogEvent construction ─────────────────────────────

let private traceParser = MessageTemplateParser()

/// Creates a LogEvent with an explicit non-default TraceId and SpanId
/// so that EnrichTraceId / EnrichSpanId can be unit-tested without a
/// running Activity (ActivitySource + listener setup is not required).
let private mkEventWithTrace () =
    let traceId = ActivityTraceId.CreateRandom()
    let spanId = ActivitySpanId.CreateRandom()

    traceId,
    spanId,
    LogEvent(DateTimeOffset.UtcNow, LogEventLevel.Information, null, traceParser.Parse(""), [], traceId, spanId)

// ── Fake HTTP infrastructure ──────────────────────────────────────────────────

/// Captured snapshot of a single HTTP request — all values extracted up-front
/// so the HttpRequestMessage can be safely disposed after SendAsync returns.
type CapturedRequest =
    { BodyBytes: byte[]
      AuthScheme: string option
      AuthParameter: string option
      TenantId: string option
      RequestUri: Uri }

[<Sealed>]
type FakeHttpHandler() =
    inherit HttpMessageHandler()
    let captured = ResizeArray<CapturedRequest>()

    member _.All = captured :> IReadOnlyList<CapturedRequest>
    member _.Last = captured[captured.Count - 1]
    member _.Count = captured.Count

    member _.LastBodyText = Encoding.UTF8.GetString(captured[captured.Count - 1].BodyBytes)

    member _.LastBodyJson = JsonDocument.Parse(captured[captured.Count - 1].BodyBytes)

    override _.SendAsync(req, ct) =
        task {
            let! bytes = req.Content.ReadAsByteArrayAsync(ct)
            let auth = req.Headers.Authorization

            let tenant =
                match req.Headers.TryGetValues("X-Scope-OrgID") with
                | true, vs -> vs |> Seq.tryHead
                | _ -> None

            captured.Add
                { BodyBytes = bytes
                  AuthScheme = if isNull auth then None else Some auth.Scheme
                  AuthParameter = if isNull auth then None else Some auth.Parameter
                  TenantId = tenant
                  RequestUri = req.RequestUri }

            return new HttpResponseMessage(HttpStatusCode.NoContent)
        }

// ── Test helpers ──────────────────────────────────────────────────────────────

/// Default factory: injects a fake HttpClient (sink does NOT own it, auth not applied).
/// Use for structure, label, and body tests.
let private makeSink (configure: LokiSinkOptions -> LokiSinkOptions) =
    let handler = new FakeHttpHandler()
    let client = new HttpClient(handler)

    let options =
        LokiSinkOptions.Defaults
        |> fun o ->
            { o with
                Uri = "http://localhost:3100"
                HttpClient = client }
        |> configure

    let sink = new LokiSink(options)
    handler, sink

/// Injects FakeHttpHandler as HttpMessageHandler — sink owns the client and applies
/// auth/tenant headers. Use for HTTP-behavior tests.
let private makeSinkWithHandler (configure: LokiSinkOptions -> LokiSinkOptions) =
    let handler = new FakeHttpHandler()

    let options =
        LokiSinkOptions.Defaults
        |> fun o ->
            { o with
                Uri = "http://localhost:3100"
                HttpMessageHandler = handler }
        |> configure

    let sink = new LokiSink(options)
    handler, sink

let private flush (sink: LokiSink) (events: LogEvent list) =
    // Serilog.Core.IBatchedLogEventSink.EmitBatchAsync takes IReadOnlyCollection; Array.ofList satisfies that.
    task { do! (sink :> IBatchedLogEventSink).EmitBatchAsync(Array.ofList events) }

// JSON navigation — all return plain values so they're safe inside test <@ ... @>
let private streamCount (doc: JsonDocument) =
    doc.RootElement.GetProperty("streams").GetArrayLength()

let private streamAt i (doc: JsonDocument) =
    doc.RootElement.GetProperty("streams")[i]

let private labelOf (key: string) (stream: JsonElement) =
    match stream.GetProperty("stream").TryGetProperty(key) with
    | true, v -> Some(v.GetString())
    | _ -> None

let private hasLabel (key: string) (stream: JsonElement) =
    stream.GetProperty("stream").TryGetProperty(key) |> fst

let private valueCount (stream: JsonElement) =
    stream.GetProperty("values").GetArrayLength()

let private timestampOf (stream: JsonElement) (i: int) =
    let entry = stream.GetProperty("values")[i]
    entry[0].GetString()

let private bodyStringOf (stream: JsonElement) (i: int) =
    let entry = stream.GetProperty("values")[i]
    entry[1].GetString()

let private bodyProp (key: string) (stream: JsonElement) (i: int) =
    use body = JsonDocument.Parse(bodyStringOf stream i)

    match body.RootElement.TryGetProperty(key) with
    | true, v ->
        match v.ValueKind with
        | JsonValueKind.String -> Some(v.GetString())
        | JsonValueKind.Object -> Some "<object>"
        | _ -> Some(v.ToString())
    | _ -> None

let private hasBodyProp (key: string) (stream: JsonElement) (i: int) =
    use body = JsonDocument.Parse(bodyStringOf stream i)
    body.RootElement.TryGetProperty(key) |> fst

// ── JSON structure ────────────────────────────────────────────────────────────

[<Fact>]
let ``json structure: single event produces one stream`` () : Task =
    task {
        let handler, sink = makeSink id
        use _ = sink
        do! flush sink [ mkInfo [] ]
        use doc = handler.LastBodyJson
        let count = streamCount doc
        test <@ count = 1 @>
    }

[<Fact>]
let ``json structure: stream has one values entry for one event`` () : Task =
    task {
        let handler, sink = makeSink id
        use _ = sink
        do! flush sink [ mkInfo [] ]
        use doc = handler.LastBodyJson
        let count = valueCount (streamAt 0 doc)
        test <@ count = 1 @>
    }

[<Fact>]
let ``json structure: timestamp is a numeric nanosecond string`` () : Task =
    task {
        let handler, sink = makeSink id
        use _ = sink
        do! flush sink [ mkInfo [] ]
        use doc = handler.LastBodyJson
        let ts = timestampOf (streamAt 0 doc) 0
        let isNumeric = ts |> Seq.forall Char.IsDigit
        let isLong = Int64.TryParse(ts) |> fst
        test <@ ts <> null && ts.Length >= 19 && isNumeric && isLong @>
    }

[<Fact>]
let ``json structure: body is a valid JSON object`` () : Task =
    task {
        let handler, sink = makeSink id
        use _ = sink
        do! flush sink [ mkInfo [] ]
        use doc = handler.LastBodyJson
        let bodyStr = bodyStringOf (streamAt 0 doc) 0
        use bodyDoc = JsonDocument.Parse(bodyStr)
        let isObject = bodyDoc.RootElement.ValueKind = JsonValueKind.Object
        test <@ isObject @>
    }

// ── Body content ──────────────────────────────────────────────────────────────

[<Fact>]
let ``body: contains Message and MessageTemplate`` () : Task =
    task {
        let handler, sink = makeSink id
        use _ = sink
        do! flush sink [ mkInfo [] ]
        use doc = handler.LastBodyJson
        let s = streamAt 0 doc
        let hasMsg = hasBodyProp "Message" s 0
        let hasTpl = hasBodyProp "MessageTemplate" s 0
        test <@ hasMsg && hasTpl @>
    }

[<Fact>]
let ``body: reserved property "Message" sanitized to "_Message"`` () : Task =
    task {
        let handler, sink = makeSink id
        use _ = sink
        do! flush sink [ mkInfo [ "Message", box "custom" ] ]
        use doc = handler.LastBodyJson
        let s = streamAt 0 doc
        let hasOriginal = hasBodyProp "Message" s 0 // always present as rendered message
        let hasSanitized = hasBodyProp "_Message" s 0 // custom property prefixed
        // The rendered "Message" is always written; the property named "Message" becomes "_Message"
        test <@ hasOriginal && hasSanitized @>
    }

[<Fact>]
let ``body: Exception field present when event has exception`` () : Task =
    task {
        let handler, sink = makeSink id
        use _ = sink
        let ex = InvalidOperationException("boom")
        
        let event =
            let template = MessageTemplateParser().Parse("")
            LogEvent(DateTimeOffset.UtcNow, LogEventLevel.Error, ex, template, [])

        do! flush sink [ event ]
        use doc = handler.LastBodyJson
        let s = streamAt 0 doc
        let hasEx = hasBodyProp "Exception" s 0
        test <@ hasEx @>
    }

// ── Level label ───────────────────────────────────────────────────────────────

[<Fact>]
let ``level label: present by default (HandleLogLevelAsLabel = true)`` () : Task =
    task {
        let handler, sink = makeSink id
        use _ = sink
        do! flush sink [ mkInfo [] ]
        use doc = handler.LastBodyJson
        let has = hasLabel "level" (streamAt 0 doc)
        test <@ has @>
    }

[<Fact>]
let ``level label: absent when HandleLogLevelAsLabel = false`` () : Task =
    task {
        let handler, sink = makeSink (fun o -> { o with HandleLogLevelAsLabel = false })
        use _ = sink
        do! flush sink [ mkInfo [] ]
        use doc = handler.LastBodyJson
        let has = hasLabel "level" (streamAt 0 doc)
        test <@ not has @>
    }

[<Fact>]
let ``level label: Fatal maps to "fatal" not "critical"`` () : Task =
    task {
        let handler, sink = makeSink id
        use _ = sink
        do! flush sink [ mkEvent LogEventLevel.Fatal [] ]
        use doc = handler.LastBodyJson
        let lbl = labelOf "level" (streamAt 0 doc)
        test <@ lbl = Some "fatal" @>
    }

[<Theory>]
[<InlineData(0, "trace")>]
[<InlineData(1, "debug")>]
[<InlineData(2, "info")>]
[<InlineData(3, "warning")>]
[<InlineData(4, "error")>]
[<InlineData(5, "fatal")>]
let ``level label: all six Serilog levels map to Grafana vocabulary`` (levelInt: int) (expected: string) : Task =
    task {
        let level = enum<LogEventLevel> levelInt
        let handler, sink = makeSink id
        use _ = sink
        do! flush sink [ mkEvent level [] ]
        use doc = handler.LastBodyJson
        let lbl = labelOf "level" (streamAt 0 doc)
        test <@ lbl = Some expected @>
    }

// ── Label behaviour ───────────────────────────────────────────────────────────

[<Fact>]
let ``labels: global labels appear in stream labels`` () : Task =
    task {
        let globals = [| { Key = "app"; Value = "test-service" } |]
        let handler, sink = makeSink (fun o -> { o with Labels = globals })
        use _ = sink
        do! flush sink [ mkInfo [] ]
        use doc = handler.LastBodyJson
        let lbl = labelOf "app" (streamAt 0 doc)
        test <@ lbl = Some "test-service" @>
    }

[<Fact>]
let ``labels: property promoted to label appears in stream`` () : Task =
    task {
        let handler, sink =
            makeSink (fun o ->
                { o with
                    PropertiesAsLabels = [| "RequestPath" |] })

        use _ = sink
        do! flush sink [ mkInfo [ "RequestPath", box "/health" ] ]
        use doc = handler.LastBodyJson
        let lbl = labelOf "RequestPath" (streamAt 0 doc)
        test <@ lbl = Some "/health" @>
    }

[<Fact>]
let ``labels: promoted property stays in body even when promoted to label`` () : Task =
    task {
        let handler, sink =
            makeSink (fun o ->
                { o with
                    PropertiesAsLabels = [| "RequestPath" |] })

        use _ = sink
        do! flush sink [ mkInfo [ "RequestPath", box "/health" ] ]
        use doc = handler.LastBodyJson
        let s = streamAt 0 doc
        let inLbl = labelOf "RequestPath" s
        let inBody = hasBodyProp "RequestPath" s 0
        test <@ inLbl = Some "/health" && inBody @>
    }

[<Fact>]
let ``labels: global label wins over property with same key`` () : Task =
    task {
        let globals = [| { Key = "app"; Value = "global-value" } |]

        let handler, sink =
            makeSink (fun o ->
                { o with
                    Labels = globals
                    PropertiesAsLabels = [| "app" |] })

        use _ = sink
        do! flush sink [ mkInfo [ "app", box "property-value" ] ]
        use doc = handler.LastBodyJson
        let lbl = labelOf "app" (streamAt 0 doc)
        test <@ lbl = Some "global-value" @>
    }

[<Fact>]
let ``labels: numeric property key gets param prefix`` () : Task =
    task {
        let handler, sink =
            makeSink (fun o ->
                { o with
                    PropertiesAsLabels = [| "0" |] })

        use _ = sink
        do! flush sink [ mkInfo [ "0", box "val" ] ]
        use doc = handler.LastBodyJson
        let s = streamAt 0 doc
        let hasNum = hasLabel "0" s
        let hasPrefix = hasLabel "param0" s
        test <@ not hasNum && hasPrefix @>
    }

// ── Stream grouping ───────────────────────────────────────────────────────────

[<Fact>]
let ``grouping: events with different label values produce separate streams`` () : Task =
    task {
        let handler, sink =
            makeSink (fun o ->
                { o with
                    PropertiesAsLabels = [| "env" |] })

        use _ = sink
        do! flush sink [ mkInfo [ "env", box "prod" ]; mkInfo [ "env", box "dev" ] ]
        use doc = handler.LastBodyJson
        let count = streamCount doc
        test <@ count = 2 @>
    }

[<Fact>]
let ``grouping: events with same labels group into one stream`` () : Task =
    task {
        let handler, sink = makeSink id
        use _ = sink
        do! flush sink [ mkInfo []; mkInfo []; mkInfo [] ]
        use doc = handler.LastBodyJson
        let streams = streamCount doc
        let values = valueCount (streamAt 0 doc)
        test <@ streams = 1 && values = 3 @>
    }

// ── HTTP behaviour ────────────────────────────────────────────────────────────

[<Fact>]
let ``http: request targets /loki/api/v1/push`` () : Task =
    task {
        let handler, sink = makeSinkWithHandler id
        use _ = sink
        do! flush sink [ mkInfo [] ]
        let path = handler.Last.RequestUri.AbsolutePath
        test <@ path = "/loki/api/v1/push" @>
    }

[<Fact>]
let ``http auth: Authorization header set when credentials provided`` () : Task =
    task {
        let creds = { Login = "user"; Password = "pass" }
        let handler, sink = makeSinkWithHandler (fun o -> { o with Credentials = creds })
        use _ = sink
        do! flush sink [ mkInfo [] ]
        let scheme = handler.Last.AuthScheme
        let param = handler.Last.AuthParameter
        // base64("user:pass") = "dXNlcjpwYXNz"
        let expected = Convert.ToBase64String(Encoding.UTF8.GetBytes("user:pass"))
        test <@ scheme = Some "Basic" && param = Some expected @>
    }

[<Fact>]
let ``http auth: no Authorization header when no credentials`` () : Task =
    task {
        let handler, sink = makeSinkWithHandler id // Defaults has null Credentials
        use _ = sink
        do! flush sink [ mkInfo [] ]
        let scheme = handler.Last.AuthScheme
        test <@ scheme = None @>
    }

[<Fact>]
let ``http tenant: X-Scope-OrgID header set when Tenant configured`` () : Task =
    task {
        let handler, sink = makeSinkWithHandler (fun o -> { o with Tenant = "my-tenant" })
        use _ = sink
        do! flush sink [ mkInfo [] ]
        let tenant = handler.Last.TenantId
        test <@ tenant = Some "my-tenant" @>
    }

// ── TraceId / SpanId enrichment ───────────────────────────────────────────────

[<Fact>]
let ``body: TraceId written when EnrichTraceId=true and event carries a TraceId`` () : Task =
    task {
        let handler, sink = makeSinkWithHandler (fun o -> { o with EnrichTraceId = true })
        use _ = sink
        let traceId, _, event = mkEventWithTrace ()
        let expectedTrace = traceId.ToHexString() // extract before quotation — struct capture not allowed in quotations
        do! flush sink [ event ]
        use doc = handler.LastBodyJson
        let bodyTr = bodyProp "TraceId" (streamAt 0 doc) 0
        test <@ bodyTr = Some expectedTrace @>
    }

[<Fact>]
let ``body: SpanId written when EnrichSpanId=true and event carries a SpanId`` () : Task =
    task {
        let handler, sink = makeSinkWithHandler (fun o -> { o with EnrichSpanId = true })
        use _ = sink
        let _, spanId, event = mkEventWithTrace ()
        let expectedSpan = spanId.ToHexString()
        do! flush sink [ event ]
        use doc = handler.LastBodyJson
        let bodySp = bodyProp "SpanId" (streamAt 0 doc) 0
        test <@ bodySp = Some expectedSpan @>
    }

[<Fact>]
let ``body: TraceId absent when EnrichTraceId=false (default)`` () : Task =
    task {
        let handler, sink = makeSinkWithHandler id // EnrichTraceId defaults to false
        use _ = sink
        let _, _, event = mkEventWithTrace () // event has a TraceId but sink won't write it
        do! flush sink [ event ]
        use doc = handler.LastBodyJson
        let hasTrace = hasBodyProp "TraceId" (streamAt 0 doc) 0
        test <@ not hasTrace @>
    }