// Copyright 2020-2026 Mykhailo Shevchuk & Contributors
//
// Licensed under the MIT license;
// you may not use this file except in compliance with the License.
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See LICENSE file in the project root for full license information.
namespace Serilog.Sinks.Grafana.Loki

open System
open System.Net.Http
open Serilog.Formatting

/// Configuration for the Grafana Loki sink.
///
/// F# usage — copy-update from Defaults:
///   { LokiSinkOptions.Defaults with Uri = "http://localhost:3100" }
///
/// C# usage — start from Defaults and mutate:
///   var opts = LokiSinkOptions.Defaults;
///   opts.Uri = "http://localhost:3100";
///   opts.BatchSizeLimit = 500;
///
/// Do NOT use `new LokiSinkOptions { ... }` from C# — unset fields will be
/// zero-initialised (BatchSizeLimit=0, Period=0s, etc.) which will cause errors.
[<CLIMutable>]
type LokiSinkOptions =
    {
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

    // ── Defaults — intrinsic member so it is visible to C# consumers ─────────────
    // Placed inline in the type body (not in a separate module extension) so that
    // C# can call LokiSinkOptions.Defaults directly.

    /// Default batch size limit (1 000 events per POST).
    static member DefaultBatchSizeLimit = 1_000

    /// Default queue limit (50 000 events in memory before dropping).
    static member DefaultQueueLimit = 50_000

    /// Returns a fully-defaulted LokiSinkOptions instance with an empty Uri.
    ///
    /// This is the correct starting point when building options:
    ///   F#: { LokiSinkOptions.Defaults with Uri = "http://localhost:3100" }
    ///   C#: var opts = LokiSinkOptions.Defaults; opts.Uri = "http://localhost:3100";
    static member Defaults =
        { Uri                   = ""
          Labels                = [||]
          PropertiesAsLabels    = [||]
          HandleLogLevelAsLabel = true
          Credentials           = Unchecked.defaultof<LokiCredentials>
          Tenant                = null
          EnrichTraceId         = false
          EnrichSpanId          = false
          BatchSizeLimit        = LokiSinkOptions.DefaultBatchSizeLimit
          QueueLimit            = LokiSinkOptions.DefaultQueueLimit
          Period                = TimeSpan.FromSeconds 1.0
          EagerlyEmitFirstEvent = true
          RetryTimeLimit        = TimeSpan.FromMinutes 10.0
          TextFormatter         = Unchecked.defaultof<ITextFormatter>
          ExceptionFormatter    = Unchecked.defaultof<ILokiExceptionFormatter>
          HttpClient            = Unchecked.defaultof<HttpClient>
          HttpMessageHandler    = Unchecked.defaultof<Net.Http.HttpMessageHandler>
          TimeProvider          = Unchecked.defaultof<TimeProvider> }
