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
open System.Runtime.CompilerServices
open System.Runtime.InteropServices
open Serilog.Configuration
open Serilog.Core
open Serilog.Events
open Serilog.Formatting

/// Registers the Grafana Loki sink with a LoggerConfiguration.
/// AbstractClass + Sealed produces the same IL as a C# static class (IsAbstract=true, IsSealed=true),
/// which is required for Serilog.Settings.Configuration to discover the extension method.
[<AbstractClass; Sealed; Extension>]
type LoggerConfigurationLokiExtensions =

    static let validateUri (uri: string) =
        if String.IsNullOrWhiteSpace uri then
            invalidArg "uri" "Loki URI must not be null or empty."

        match Uri.TryCreate(uri, UriKind.Absolute) with
        | false, _ -> invalidArg "uri" $"'{uri}' is not a valid absolute URI."
        | true, u when u.Scheme <> "http" && u.Scheme <> "https" ->
            invalidArg "uri" $"Loki URI scheme must be http or https, got '{u.Scheme}'."
        | _ -> ()

    static let wire (sinkConfig: LoggerSinkConfiguration) (options: LokiSinkOptions) (level: LogEventLevel) =
        validateUri options.Uri

        // Serilog 4.x native batching — BatchingOptions has RetryTimeLimit, PeriodicBatchingSinkOptions does not.
        let batchingOpts =
            BatchingOptions(
                BatchSizeLimit        = options.BatchSizeLimit,
                BufferingTimeLimit    = options.Period,
                EagerlyEmitFirstEvent = options.EagerlyEmitFirstEvent,
                QueueLimit            = Nullable options.QueueLimit,
                RetryTimeLimit        = options.RetryTimeLimit)

        let sink = new LokiSink(options)
        sinkConfig.Sink(sink, batchingOpts, level, Unchecked.defaultof<LoggingLevelSwitch>)

    /// <summary>
    /// Writes log events to Grafana Loki.
    /// </summary>
    /// <param name="sinkConfig">The logger sink configuration.</param>
    /// <param name="uri">Loki base URI, e.g. "http://localhost:3100". Required.</param>
    /// <param name="labels">Static labels attached to every stream.</param>
    /// <param name="propertiesAsLabels">Property names to promote to stream labels.</param>
    /// <param name="handleLogLevelAsLabel">Add a 'level' stream label (default: true).</param>
    /// <param name="credentialsLogin">Basic-auth login. Pair with credentialsPassword.</param>
    /// <param name="credentialsPassword">Basic-auth password.</param>
    /// <param name="tenant">X-Scope-OrgID multi-tenancy header value.</param>
    /// <param name="enrichTraceId">Write ActivityTraceId to the log body (default: false).</param>
    /// <param name="enrichSpanId">Write ActivitySpanId to the log body (default: false).</param>
    /// <param name="batchSizeLimit">Maximum events per HTTP POST (default: 1 000).</param>
    /// <param name="queueLimit">Maximum in-memory queue size before dropping (default: 50 000).</param>
    /// <param name="period">Flush interval (default: 1 s).</param>
    /// <param name="eagerlyEmitFirstEvent">Flush immediately on the first event (default: true).</param>
    /// <param name="retryTimeLimit">Stop retrying a failed batch after this duration (default: 10 min).</param>
    /// <param name="textFormatter">Per-event body formatter (default: LokiJsonTextFormatter).</param>
    /// <param name="exceptionFormatter">Exception serializer (default: LokiExceptionFormatter).</param>
    /// <param name="httpClient">Pre-built HttpClient. The sink never disposes an injected client.</param>
    /// <param name="httpMessageHandler">Handler for the sink's own HttpClient (compression, retry, etc.).</param>
    /// <param name="restrictedToMinimumLevel">Minimum log level (default: Verbose).</param>
    [<Extension>]
    static member GrafanaLoki(
        sinkConfig: LoggerSinkConfiguration,
        uri: string,
        // ── Labels ────────────────────────────────────────────────────────────
        [<Optional; DefaultParameterValue(null: LokiLabel[])>] labels: LokiLabel[],
        [<Optional; DefaultParameterValue(null: string[])>] propertiesAsLabels: string[],
        [<Optional; DefaultParameterValue(true)>] handleLogLevelAsLabel: bool,
        // ── Auth / routing ────────────────────────────────────────────────────
        [<Optional; DefaultParameterValue(null: string)>] credentialsLogin: string,
        [<Optional; DefaultParameterValue(null: string)>] credentialsPassword: string,
        [<Optional; DefaultParameterValue(null: string)>] tenant: string,
        // ── OpenTelemetry ─────────────────────────────────────────────────────
        [<Optional; DefaultParameterValue(false)>] enrichTraceId: bool,
        [<Optional; DefaultParameterValue(false)>] enrichSpanId: bool,
        // ── Batching ──────────────────────────────────────────────────────────
        [<Optional; DefaultParameterValue(1_000)>] batchSizeLimit: int,
        [<Optional; DefaultParameterValue(50_000)>] queueLimit: int,
        // String type is required: F# [<Optional>] TimeSpan and Nullable<TimeSpan> both compile with
        // default=Missing.Value which Serilog.Settings.Configuration cannot use when building the
        // reflection call. C# TimeSpan?=null emits default=null but F# cannot replicate this.
        // Null/empty = use sink default. Format: "hh:mm:ss" e.g. "00:00:02".
        [<Optional; DefaultParameterValue(null: string)>] period: string,
        [<Optional; DefaultParameterValue(true)>] eagerlyEmitFirstEvent: bool,
        // Null/empty = use sink default (10 min).
        [<Optional; DefaultParameterValue(null: string)>] retryTimeLimit: string,
        // ── Extension points ──────────────────────────────────────────────────
        [<Optional; DefaultParameterValue(null: ITextFormatter)>] textFormatter: ITextFormatter,
        [<Optional; DefaultParameterValue(null: ILokiExceptionFormatter)>] exceptionFormatter: ILokiExceptionFormatter,
        [<Optional; DefaultParameterValue(null: HttpClient)>] httpClient: HttpClient,
        [<Optional; DefaultParameterValue(null: Net.Http.HttpMessageHandler)>] httpMessageHandler: Net.Http.HttpMessageHandler,
        // ── Level ─────────────────────────────────────────────────────────────
        [<Optional; DefaultParameterValue(LevelAlias.Minimum)>] restrictedToMinimumLevel: LogEventLevel) =

        let credentials =
            if String.IsNullOrEmpty credentialsLogin then Unchecked.defaultof<LokiCredentials>
            else { Login = credentialsLogin; Password = credentialsPassword }

        let options =
            { Uri                   = uri
              Labels                = if isNull labels then [||] else labels
              PropertiesAsLabels    = if isNull propertiesAsLabels then [||] else propertiesAsLabels
              HandleLogLevelAsLabel = handleLogLevelAsLabel
              Credentials           = credentials
              Tenant                = tenant
              EnrichTraceId         = enrichTraceId
              EnrichSpanId          = enrichSpanId
              BatchSizeLimit        = batchSizeLimit
              QueueLimit            = queueLimit
              Period                = if String.IsNullOrEmpty period then TimeSpan.FromSeconds 1.0 else TimeSpan.Parse(period)
              EagerlyEmitFirstEvent = eagerlyEmitFirstEvent
              RetryTimeLimit        = if String.IsNullOrEmpty retryTimeLimit then TimeSpan.FromMinutes 10.0 else TimeSpan.Parse(retryTimeLimit)
              TextFormatter         = textFormatter
              ExceptionFormatter    = exceptionFormatter
              HttpClient            = httpClient
              HttpMessageHandler    = httpMessageHandler }

        wire sinkConfig options restrictedToMinimumLevel
