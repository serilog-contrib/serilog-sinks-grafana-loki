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
open Serilog.Formatting
open Serilog.Parsing
open Serilog.Core
open Serilog.Sinks.Grafana.Loki
open Serilog.Sinks.Grafana.Loki.Tests.Helpers

// ── Helpers for trace-aware LogEvent construction ─────────────────────────────

let private traceParser = MessageTemplateParser()

/// Creates a LogEvent with an explicit non-default TraceId and SpanId
/// so that TraceIdMode / SpanIdMode routing can be unit-tested without a
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
    {
        BodyBytes: byte[]
        AuthScheme: string option
        AuthParameter: string option
        TenantId: string option
        RequestUri: Uri
    }

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
                {
                    BodyBytes = bytes
                    AuthScheme = if isNull auth then None else Some auth.Scheme
                    AuthParameter = if isNull auth then None else Some auth.Parameter
                    TenantId = tenant
                    RequestUri = req.RequestUri
                }

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
                HttpClient = client
            }
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
                HttpMessageHandler = handler
            }
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

let private entry (i: int) (stream: JsonElement) = stream.GetProperty("values")[i]

let private bodyDoc (e: JsonElement) : JsonDocument = JsonDocument.Parse(e[1].GetString())

let private propKindOf (key: string) (doc: JsonDocument) =
    match doc.RootElement.TryGetProperty(key) with
    | true, v -> v.ValueKind
    | _ -> JsonValueKind.Undefined

// Number of elements in a values entry: 2 ([ts, body]) or 3 ([ts, body, metadata]).
let private entryElementCount (stream: JsonElement) (i: int) =
    let entry = stream.GetProperty("values")[i]
    entry.GetArrayLength()

// Reads a key from the optional 3rd (structured-metadata) element of a values entry.
let private metadataProp (key: string) (stream: JsonElement) (i: int) =
    let e = stream.GetProperty("values")[i]

    if e.GetArrayLength() < 3 then
        None
    else
        match e[2].TryGetProperty(key) with
        | true, v -> Some(v.GetString())
        | _ -> None

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
let ``body: quotes, markup and non-ASCII are not unicode-escaped`` () : Task =
    task {
        let handler, sink = makeSink id
        use _ = sink
        do! flush sink [ mkInfo [ "Note", box "a>b <c> & \"q\" Привет" ] ]
        use doc = handler.LastBodyJson
        let body = bodyStringOf (streamAt 0 doc) 0
        // Relaxed escaping: '<' '>' '&' and non-ASCII stay verbatim and quotes use the JSON-standard
        // \", never \uXXXX. The default HTML-safe encoder would render all of these as \uXXXX.
        test <@ body.Contains("a>b <c> &") @>
        test <@ body.Contains("Привет") @>
        test <@ body.Contains("\\\"q\\\"") @>
        test <@ not (body.Contains("\\u0022")) @>
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
                    PropertiesAsLabels = [| "RequestPath" |]
                })

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
                    PropertiesAsLabels = [| "RequestPath" |]
                })

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
                    PropertiesAsLabels = [| "app" |]
                })

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
                    PropertiesAsLabels = [| "0" |]
                })

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
                    PropertiesAsLabels = [| "env" |]
                })

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
let ``http: base URI with path prefix and no trailing slash resolves push endpoint correctly`` () : Task =
    // RFC 3986 §5.2.2 strips everything right of the last '/' in the base path.
    // Without the trailing-slash fix, "http://proxy/gateway" resolves "loki/api/v1/push"
    // to "http://proxy/loki/api/v1/push" instead of the correct "http://proxy/gateway/loki/api/v1/push".
    task {
        let handler = new FakeHttpHandler()

        let options =
            { LokiSinkOptions.Defaults with
                Uri = "http://proxy/gateway"
                HttpMessageHandler = handler
            }

        use sink = new LokiSink(options)
        do! flush sink [ mkInfo [] ]
        test <@ handler.Last.RequestUri.AbsolutePath = "/gateway/loki/api/v1/push" @>
    }

[<Fact>]
let ``http auth: Authorization header set when credentials provided`` () : Task =
    task {
        let creds = LokiCredentials(Login = "user", Password = "pass")
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

// ── TraceId / SpanId routing (Body vs StructuredMetadata) ─────────────────────

[<Fact>]
let ``trace: TraceId written to body when TraceIdMode=Body`` () : Task =
    task {
        let handler, sink =
            makeSinkWithHandler (fun o ->
                { o with
                    TraceIdMode = LokiFieldDestination.Body
                })

        use _ = sink
        let traceId, _, event = mkEventWithTrace ()
        let expectedTrace = traceId.ToHexString() // extract before quotation — struct capture not allowed in quotations
        do! flush sink [ event ]
        use doc = handler.LastBodyJson
        let s = streamAt 0 doc
        let bodyTr = bodyProp "TraceId" s 0
        let inMeta = metadataProp "TraceId" s 0
        test <@ bodyTr = Some expectedTrace && inMeta = None @>
    }

[<Fact>]
let ``trace: SpanId written to body when SpanIdMode=Body`` () : Task =
    task {
        let handler, sink =
            makeSinkWithHandler (fun o ->
                { o with
                    SpanIdMode = LokiFieldDestination.Body
                })

        use _ = sink
        let _, spanId, event = mkEventWithTrace ()
        let expectedSpan = spanId.ToHexString()
        do! flush sink [ event ]
        use doc = handler.LastBodyJson
        let s = streamAt 0 doc
        let bodySp = bodyProp "SpanId" s 0
        let inMeta = metadataProp "SpanId" s 0
        test <@ bodySp = Some expectedSpan && inMeta = None @>
    }

[<Fact>]
let ``trace: TraceId in structured metadata (not body) when TraceIdMode=StructuredMetadata`` () : Task =
    task {
        let handler, sink =
            makeSinkWithHandler (fun o ->
                { o with
                    TraceIdMode = LokiFieldDestination.StructuredMetadata
                })

        use _ = sink
        let traceId, _, event = mkEventWithTrace ()
        let expectedTrace = traceId.ToHexString()
        do! flush sink [ event ]
        use doc = handler.LastBodyJson
        let s = streamAt 0 doc
        let metaTr = metadataProp "TraceId" s 0
        let inBody = hasBodyProp "TraceId" s 0
        test <@ metaTr = Some expectedTrace && not inBody @>
    }

[<Fact>]
let ``trace: SpanId in structured metadata (not body) when SpanIdMode=StructuredMetadata`` () : Task =
    task {
        let handler, sink =
            makeSinkWithHandler (fun o ->
                { o with
                    SpanIdMode = LokiFieldDestination.StructuredMetadata
                })

        use _ = sink
        let _, spanId, event = mkEventWithTrace ()
        let expectedSpan = spanId.ToHexString()
        do! flush sink [ event ]
        use doc = handler.LastBodyJson
        let s = streamAt 0 doc
        let metaSp = metadataProp "SpanId" s 0
        let inBody = hasBodyProp "SpanId" s 0
        test <@ metaSp = Some expectedSpan && not inBody @>
    }

[<Fact>]
let ``trace: TraceId/SpanId absent from body and metadata when mode=None (default)`` () : Task =
    task {
        let handler, sink = makeSinkWithHandler id // both modes default to None
        use _ = sink
        let _, _, event = mkEventWithTrace () // event carries both ids but the sink writes neither
        do! flush sink [ event ]
        use doc = handler.LastBodyJson
        let s = streamAt 0 doc
        let traceInBody = hasBodyProp "TraceId" s 0
        let spanInBody = hasBodyProp "SpanId" s 0
        let elems = entryElementCount s 0
        test <@ not traceInBody && not spanInBody && elems = 2 @>
    }

// ── Structured metadata from properties ───────────────────────────────────────

[<Fact>]
let ``metadata: no 3rd element by default (nothing configured)`` () : Task =
    task {
        let handler, sink = makeSink id
        use _ = sink
        do! flush sink [ mkInfo [] ]
        use doc = handler.LastBodyJson
        let count = entryElementCount (streamAt 0 doc) 0
        test <@ count = 2 @>
    }

[<Fact>]
let ``metadata: property attached as structured metadata and retained in body`` () : Task =
    task {
        let handler, sink =
            makeSink (fun o ->
                { o with
                    PropertiesAsStructuredMetadata = [| "RequestId" |]
                })

        use _ = sink
        do! flush sink [ mkInfo [ "RequestId", box "abc-123" ] ]
        use doc = handler.LastBodyJson
        let s = streamAt 0 doc
        let inMeta = metadataProp "RequestId" s 0
        let inBody = hasBodyProp "RequestId" s 0
        test <@ inMeta = Some "abc-123" && inBody @>
    }

[<Fact>]
let ``metadata: numeric property key gets param prefix`` () : Task =
    task {
        let handler, sink =
            makeSink (fun o ->
                { o with
                    PropertiesAsStructuredMetadata = [| "0" |]
                })

        use _ = sink
        do! flush sink [ mkInfo [ "0", box "v" ] ]
        use doc = handler.LastBodyJson
        let s = streamAt 0 doc
        let raw = metadataProp "0" s 0
        let prefixed = metadataProp "param0" s 0
        test <@ raw = None && prefixed = Some "v" @>
    }

[<Fact>]
let ``metadata: differing metadata values do NOT split streams (unlike labels)`` () : Task =
    task {
        let handler, sink =
            makeSink (fun o ->
                { o with
                    PropertiesAsStructuredMetadata = [| "RequestId" |]
                })

        use _ = sink
        do! flush sink [ mkInfo [ "RequestId", box "a" ]; mkInfo [ "RequestId", box "b" ] ]
        use doc = handler.LastBodyJson
        // metadata is per-line, not part of grouping → both events stay in ONE stream
        let count = streamCount doc
        test <@ count = 1 @>
    }

[<Fact>]
let ``metadata: non-string property value is rendered as a string`` () : Task =
    // Loki's push API accepts only string metadata values, so a numeric property must be
    // stringified in the metadata object (unlike the typed JSON body).
    task {
        let handler, sink =
            makeSink (fun o ->
                { o with
                    PropertiesAsStructuredMetadata = [| "Count" |]
                })

        use _ = sink
        do! flush sink [ mkInfo [ "Count", box 42 ] ]
        use doc = handler.LastBodyJson
        let v = metadataProp "Count" (streamAt 0 doc) 0
        test <@ v = Some "42" @>
    }

[<Fact>]
let ``metadata: duplicate property name emits a single key`` () : Task =
    // A duplicate name in PropertiesAsStructuredMetadata must not produce a duplicate JSON key
    // (the names are de-duplicated at construction).
    task {
        let handler, sink =
            makeSink (fun o ->
                { o with
                    PropertiesAsStructuredMetadata = [| "RequestId"; "RequestId" |]
                })

        use _ = sink
        do! flush sink [ mkInfo [ "RequestId", box "abc" ] ]
        use doc = handler.LastBodyJson
        let metaObj = (entry 0 (streamAt 0 doc))[2]
        let keyCount = metaObj.EnumerateObject() |> Seq.length
        test <@ keyCount = 1 @>
    }

// ── validateUri ───────────────────────────────────────────────────────────────

let private tryGrafanaLoki (uri: string) =
    try
        Serilog.LoggerConfiguration().WriteTo.GrafanaLoki(uri = uri) |> ignore
        None
    with ex ->
        Some ex

[<Theory>]
[<InlineData(null: string)>]
[<InlineData("")>]
[<InlineData("   ")>]
let ``validateUri: null or whitespace uri throws ArgumentException`` (uri: string) =
    let ex = tryGrafanaLoki uri
    test <@ ex.IsSome && ex.Value :? ArgumentException @>

[<Fact>]
let ``validateUri: relative uri throws ArgumentException`` () =
    let ex = tryGrafanaLoki "localhost:3100"
    test <@ ex.IsSome && ex.Value :? ArgumentException @>

[<Fact>]
let ``validateUri: non-http scheme throws ArgumentException`` () =
    let ex = tryGrafanaLoki "ftp://localhost:3100"
    test <@ ex.IsSome && ex.Value :? ArgumentException @>

[<Fact>]
let ``validateUri: valid http uri succeeds`` () =
    let ex = tryGrafanaLoki "http://localhost:3100"
    test <@ ex.IsNone @>

// ── validateTenant ────────────────────────────────────────────────────────────

let private tryGrafanaLokiTenant (tenant: string) =
    try
        Serilog.LoggerConfiguration().WriteTo.GrafanaLoki(uri = "http://localhost:3100", tenant = tenant)
        |> ignore

        None
    with ex ->
        Some ex

[<Theory>]
[<InlineData(null: string)>]
[<InlineData("")>]
[<InlineData("tenant-1")>]
[<InlineData("Tenant_01.prod")>]
[<InlineData("t!x-y_z.a*b'c(d)e")>] // every special character Loki allows
[<InlineData("a..b")>] // '..' as a substring is legal; only the exact values '.'/'..' are not
let ``validateTenant: absent or valid tenant succeeds`` (tenant: string) =
    let ex = tryGrafanaLokiTenant tenant
    test <@ ex.IsNone @>

[<Theory>]
[<InlineData(".")>]
[<InlineData("..")>]
let ``validateTenant: path-traversal values throw ArgumentException`` (tenant: string) =
    let ex = tryGrafanaLokiTenant tenant
    test <@ ex.IsSome && ex.Value :? ArgumentException @>

[<Theory>]
[<InlineData("foo/bar")>]
[<InlineData("foo bar")>]
[<InlineData("foo,bar")>]
[<InlineData("тенант")>]
let ``validateTenant: invalid characters throw ArgumentException`` (tenant: string) =
    let ex = tryGrafanaLokiTenant tenant
    test <@ ex.IsSome && ex.Value :? ArgumentException @>

[<Fact>]
let ``validateTenant: 150 bytes is accepted, 151 is rejected`` () =
    test <@ (tryGrafanaLokiTenant (String('a', 150))).IsNone @>
    let ex = tryGrafanaLokiTenant (String('a', 151))
    test <@ ex.IsSome && ex.Value :? ArgumentException @>

// ── writeValue branches ───────────────────────────────────────────────────────

let private mkEventWithProp (key: string) (value: LogEventPropertyValue) =
    let parser = MessageTemplateParser()
    LogEvent(DateTimeOffset.UtcNow, LogEventLevel.Information, null, parser.Parse(""), [ LogEventProperty(key, value) ])

[<Fact>]
let ``body: null scalar value serialized as JSON null`` () : Task =
    task {
        let handler, sink = makeSink id
        use _ = sink
        let event = mkEventWithProp "nullProp" (ScalarValue(null))
        do! flush sink [ event ]
        use doc = handler.LastBodyJson
        use body = bodyDoc (entry 0 (streamAt 0 doc))
        let propKind = propKindOf "nullProp" body
        test <@ propKind = JsonValueKind.Null @>
    }

[<Fact>]
let ``body: non-finite double serialized as string not dropped`` () : Task =
    // Without the IsFinite guard, Utf8JsonWriter throws JsonException on nan/infinity.
    task {
        let handler, sink = makeSink id
        use _ = sink
        let event = mkEventWithProp "nanProp" (ScalarValue(Double.NaN))
        do! flush sink [ event ]
        use doc = handler.LastBodyJson
        use body = bodyDoc (entry 0 (streamAt 0 doc))
        let propKind = propKindOf "nanProp" body
        test <@ propKind = JsonValueKind.String @> // "NaN"
    }

[<Fact>]
let ``body: sequence value serialized as JSON array`` () : Task =
    task {
        let handler, sink = makeSink id
        use _ = sink

        let seqVal =
            SequenceValue [| ScalarValue(1) :> LogEventPropertyValue; ScalarValue(2) |]

        let event = mkEventWithProp "items" seqVal
        do! flush sink [ event ]
        use doc = handler.LastBodyJson
        use body = bodyDoc (entry 0 (streamAt 0 doc))
        let propKind = propKindOf "items" body
        test <@ propKind = JsonValueKind.Array @>
    }

[<Fact>]
let ``body: Guid scalar serialized as canonical D-format string`` () : Task =
    task {
        let handler, sink = makeSink id
        use _ = sink
        let g = Guid("12345678-1234-1234-1234-1234567890ab")
        let event = mkEventWithProp "id" (ScalarValue(g))
        do! flush sink [ event ]
        use doc = handler.LastBodyJson
        use body = bodyDoc (entry 0 (streamAt 0 doc))
        let v = body.RootElement.GetProperty("id")
        // Typed WriteStringValue(Guid) emits the same lowercase, hyphenated form as ToString()
        let isString = v.ValueKind = JsonValueKind.String
        let got = v.GetString()
        let expected = g.ToString()
        test <@ isString && got = expected @>
    }

[<Fact>]
let ``body: DateTime scalar serialized as round-trippable ISO 8601 string`` () : Task =
    task {
        let handler, sink = makeSink id
        use _ = sink
        let dt = DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc)
        let event = mkEventWithProp "ts" (ScalarValue(dt))
        do! flush sink [ event ]
        use doc = handler.LastBodyJson
        use body = bodyDoc (entry 0 (streamAt 0 doc))
        let v = body.RootElement.GetProperty("ts")
        // Culture-invariant ISO 8601 (not the old culture-dependent ToString); round-trips.
        let isString = v.ValueKind = JsonValueKind.String
        let roundtrips = v.GetDateTime() = dt
        test <@ isString && roundtrips @>
    }

[<Fact>]
let ``body: DateTimeOffset scalar serialized as round-trippable ISO 8601 string`` () : Task =
    task {
        let handler, sink = makeSink id
        use _ = sink
        let dto = DateTimeOffset(2024, 1, 2, 3, 4, 5, TimeSpan.FromHours 2.0)
        let event = mkEventWithProp "ts" (ScalarValue(dto))
        do! flush sink [ event ]
        use doc = handler.LastBodyJson
        use body = bodyDoc (entry 0 (streamAt 0 doc))
        let v = body.RootElement.GetProperty("ts")
        let isString = v.ValueKind = JsonValueKind.String
        let roundtrips = v.GetDateTimeOffset() = dto
        test <@ isString && roundtrips @>
    }

// ── multi-event batch: reused writer/buffer must not leak between events ──────

[<Fact>]
let ``body: each event in a multi-event batch serializes its own body`` () : Task =
    // Guards the reused bodyJsonWriter / message / body buffers: three events in ONE stream
    // (same level, no property labels), each with a distinct rendered message and property.
    // If reset/clear between events regressed, event N>0 would leak bytes from event N-1.
    task {
        let handler, sink = makeSink id
        use _ = sink
        let tmpl = traceParser.Parse("event {Seq}")

        let at i =
            DateTimeOffset(2024, 1, 1, 0, 0, i, TimeSpan.Zero)

        let events =
            [
                for i in 0..2 ->
                    LogEvent(at i, LogEventLevel.Information, null, tmpl, [ LogEventProperty("Seq", ScalarValue(i)) ])
            ]

        do! flush sink events
        use doc = handler.LastBodyJson
        let s = streamAt 0 doc
        let streams = streamCount doc
        let values = valueCount s
        // Sorted ascending by timestamp, so entry i is event i.
        let msg0 = bodyProp "Message" s 0
        let msg1 = bodyProp "Message" s 1
        let msg2 = bodyProp "Message" s 2
        let seq0 = bodyProp "Seq" s 0
        let seq1 = bodyProp "Seq" s 1
        let seq2 = bodyProp "Seq" s 2
        test <@ streams = 1 && values = 3 @>
        test <@ msg0 = Some "event 0" && msg1 = Some "event 1" && msg2 = Some "event 2" @>
        test <@ seq0 = Some "0" && seq1 = Some "1" && seq2 = Some "2" @>
    }

// ── custom ITextFormatter path ────────────────────────────────────────────────

type private FixedBodyFormatter(text: string) =
    interface ITextFormatter with
        member _.Format(_, output) = output.Write(text)

[<Fact>]
let ``body: custom ITextFormatter goes through Utf8TextWriter path`` () : Task =
    // When a non-LokiJsonTextFormatter is used, Serialization.fs routes through
    // Utf8TextWriter. Verify the custom body survives unchanged in the Loki payload.
    task {
        let handler, sink =
            makeSink (fun o ->
                { o with
                    TextFormatter = FixedBodyFormatter("CUSTOM_BODY")
                })

        use _ = sink
        do! flush sink [ mkInfo [] ]
        use doc = handler.LastBodyJson
        let body = bodyStringOf (streamAt 0 doc) 0
        test <@ body = "CUSTOM_BODY" @>
    }

// ── HTTP error response path ──────────────────────────────────────────────────

type private ErrorHttpHandler(statusCode: HttpStatusCode) =
    inherit HttpMessageHandler()

    override _.SendAsync(_, _) =
        Task.FromResult(new HttpResponseMessage(statusCode))

[<Fact>]
let ``http error: non-success response causes EmitBatchAsync to throw`` () : Task =
    task {
        let options =
            { LokiSinkOptions.Defaults with
                Uri = "http://localhost:3100"
                HttpClient = new HttpClient(new ErrorHttpHandler(HttpStatusCode.InternalServerError))
            }

        use sink = new LokiSink(options)

        let! ex =
            Assert.ThrowsAsync<HttpRequestException>(fun () ->
                (sink :> IBatchedLogEventSink).EmitBatchAsync(Array.ofList [ mkInfo [] ]))

        test <@ not (isNull ex) @>
    }