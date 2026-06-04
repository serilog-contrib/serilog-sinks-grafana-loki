namespace Serilog.Sinks.Grafana.Loki

open System
open System.Net.Http
open Serilog.Formatting

/// Configuration for the Grafana Loki sink.
/// Use LokiSinkOptions.Defaults for a fully-defaulted starting point,
/// then copy-update only the fields you need:
///   { LokiSinkOptions.Defaults with Uri = "http://localhost:3100" }
[<CLIMutable>]
type LokiSinkOptions = {

    // ── Required ──────────────────────────────────────────────────────────────
    /// Loki base URI, e.g. "http://localhost:3100".
    Uri: string

    // ── Labels ────────────────────────────────────────────────────────────────
    /// Static labels attached to every stream. Global labels take priority over
    /// property-derived labels when keys collide.
    Labels: LokiLabel[]

    /// Log event property names to promote to stream labels.
    PropertiesAsLabels: string[]

    /// When true (default), adds a 'level' label using Grafana log-level vocabulary.
    HandleLogLevelAsLabel: bool

    // ── Auth / routing ────────────────────────────────────────────────────────
    /// Basic-auth credentials. Leave null to disable authentication.
    /// For bearer token or other auth, configure the injected HttpClient directly.
    Credentials: LokiCredentials

    /// Value for the X-Scope-OrgID multi-tenancy header. Leave null to omit.
    Tenant: string

    // ── OpenTelemetry ─────────────────────────────────────────────────────────
    /// Write the log event's ActivityTraceId as a 'TraceId' field in the JSON body.
    EnrichTraceId: bool

    /// Write the log event's ActivitySpanId as a 'SpanId' field in the JSON body.
    EnrichSpanId: bool

    // ── Batching ──────────────────────────────────────────────────────────────
    /// Maximum events per HTTP POST. Maps to BatchingOptions.BatchSizeLimit.
    BatchSizeLimit: int

    /// Maximum events held in the in-memory queue before dropping.
    /// Maps to BatchingOptions.QueueLimit. Default 50 000.
    QueueLimit: int

    /// Flush interval. Maps to BatchingOptions.BufferingTimeLimit. Default 1 s.
    Period: TimeSpan

    /// Flush immediately on the first log event (helps detect misconfiguration at startup).
    EagerlyEmitFirstEvent: bool

    /// Drop a batch and stop retrying after this duration. Default 10 min.
    RetryTimeLimit: TimeSpan

    // ── Extension points ──────────────────────────────────────────────────────
    /// Per-event log body formatter. Defaults to LokiJsonTextFormatter.
    /// Null → use LokiJsonTextFormatter with EnrichTraceId/EnrichSpanId applied.
    TextFormatter: ITextFormatter

    /// Exception serializer. Null → use LokiExceptionFormatter.
    ExceptionFormatter: ILokiExceptionFormatter

    /// HttpClient used for all Loki requests.
    /// Null → the sink creates and owns its own client.
    /// Non-null → the sink never disposes the client; lifecycle is the caller's responsibility.
    HttpClient: HttpClient

    /// Optional HttpMessageHandler for the sink's internally-created HttpClient.
    /// Use this to inject retry handlers, compression, or test fakes while keeping the
    /// sink responsible for auth and client lifetime. Ignored when HttpClient is non-null.
    HttpMessageHandler: Net.Http.HttpMessageHandler

    /// Clock abstraction. Null → TimeProvider.System.
    TimeProvider: TimeProvider
}

[<AutoOpen>]
module LokiSinkOptionsDefaults =

    /// Returns a fully-defaulted LokiSinkOptions with an empty Uri.
    /// Typical usage:
    ///   { LokiSinkOptions.Defaults with Uri = "http://localhost:3100" }
    let [<Literal>] DefaultBatchSizeLimit    = 1_000
    let [<Literal>] DefaultQueueLimit        = 50_000

    type LokiSinkOptions with
        static member Defaults = {
            Uri                   = ""
            Labels                = [||]
            PropertiesAsLabels    = [||]
            HandleLogLevelAsLabel = true
            Credentials           = Unchecked.defaultof<LokiCredentials>
            Tenant                = null
            EnrichTraceId         = false
            EnrichSpanId          = false
            BatchSizeLimit        = DefaultBatchSizeLimit
            QueueLimit            = DefaultQueueLimit
            Period                = TimeSpan.FromSeconds 1.0
            EagerlyEmitFirstEvent = true
            RetryTimeLimit        = TimeSpan.FromMinutes 10.0
            TextFormatter         = Unchecked.defaultof<ITextFormatter>
            ExceptionFormatter    = Unchecked.defaultof<ILokiExceptionFormatter>
            HttpClient            = Unchecked.defaultof<HttpClient>
            HttpMessageHandler    = Unchecked.defaultof<Net.Http.HttpMessageHandler>
            TimeProvider          = Unchecked.defaultof<TimeProvider>
        }
