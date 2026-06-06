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
[<AbstractClass; Sealed>]
type LoggerConfigurationLokiExtensions =

    static let validateUri (uri: string) =
        if String.IsNullOrWhiteSpace uri then
            invalidArg "uri" "Loki URI must not be null or empty."

        match Uri.TryCreate(uri, UriKind.Absolute) with
        | false, _ -> invalidArg "uri" $"'{uri}' is not a valid absolute URI."
        | true, u when u.Scheme <> "http" && u.Scheme <> "https" ->
            invalidArg "uri" $"Loki URI scheme must be http or https, got '{u.Scheme}'."
        | _ -> ()

    // Loki tenant ID rules (https://grafana.com/docs/loki/latest/operations/multi-tenancy/):
    // alphanumerics plus !-_.*'(), at most 150 bytes, and not the special values '.' or '..'
    // (tenant IDs become storage path segments server-side). Fail fast like validateUri —
    // an invalid tenant would otherwise surface only as rejected batches in SelfLog, or as
    // logs silently landing under the wrong tenant.
    static let validateTenant (tenant: string) =
        if not (String.IsNullOrEmpty tenant) then
            if tenant = "." || tenant = ".." then
                invalidArg "tenant" "Loki tenant ID must not be '.' or '..'."

            let isValidChar (c: char) =
                Char.IsAsciiLetterOrDigit c
                || c = '!'
                || c = '-'
                || c = '_'
                || c = '.'
                || c = '*'
                || c = '\''
                || c = '('
                || c = ')'

            if not (String.forall isValidChar tenant) then
                invalidArg
                    "tenant"
                    $"Loki tenant ID '{tenant}' contains invalid characters. Allowed: alphanumerics and !-_.*'()."

            // The allowed charset is ASCII-only, so Length equals the byte count Loki limits.
            if tenant.Length > 150 then
                invalidArg "tenant" $"Loki tenant ID must not be longer than 150 bytes, got {tenant.Length}."

    static let wire (sinkConfig: LoggerSinkConfiguration) (options: LokiSinkOptions) (level: LogEventLevel) =
        validateUri options.Uri
        validateTenant options.Tenant

        // Serilog 4.x native batching — BatchingOptions has RetryTimeLimit, PeriodicBatchingSinkOptions does not.
        let batchingOpts =
            BatchingOptions(
                BatchSizeLimit = options.BatchSizeLimit,
                BufferingTimeLimit = options.Period,
                EagerlyEmitFirstEvent = options.EagerlyEmitFirstEvent,
                QueueLimit = Nullable options.QueueLimit,
                RetryTimeLimit = options.RetryTimeLimit
            )

        let sink = new LokiSink(options)
        sinkConfig.Sink(sink, batchingOpts, level, Unchecked.defaultof<LoggingLevelSwitch>)

    /// <summary>
    /// Writes log events to Grafana Loki.
    /// </summary>
    /// <param name="sinkConfig">The logger sink configuration.</param>
    /// <param name="uri">Loki base URI, e.g. "http://localhost:3100". Required.</param>
    /// <param name="labels">Static labels attached to every stream.</param>
    /// <param name="propertiesAsLabels">Property names to promote to stream labels.</param>
    /// <param name="propertiesAsStructuredMetadata">Property names to attach as per-line Loki structured metadata (non-indexed; requires Loki 3.0+).</param>
    /// <param name="handleLogLevelAsLabel">Add a 'level' stream label (default: true).</param>
    /// <param name="credentials">Basic-auth credentials. From appsettings.json, an object with login/password.</param>
    /// <param name="tenant">X-Scope-OrgID multi-tenancy header value.</param>
    /// <param name="traceIdMode">Where to write ActivityTraceId: None (default), Body, or StructuredMetadata.</param>
    /// <param name="spanIdMode">Where to write ActivitySpanId: None (default), Body, or StructuredMetadata.</param>
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
    static member GrafanaLoki
        (
            sinkConfig: LoggerSinkConfiguration,
            uri: string,
            // ── Labels ────────────────────────────────────────────────────────────
            [<Optional; DefaultParameterValue(null: LokiLabel[])>] labels: LokiLabel[],
            [<Optional; DefaultParameterValue(null: string[])>] propertiesAsLabels: string[],
            [<Optional; DefaultParameterValue(null: string[])>] propertiesAsStructuredMetadata: string[],
            [<Optional; DefaultParameterValue(true)>] handleLogLevelAsLabel: bool,
            // ── Auth / routing ────────────────────────────────────────────────────
            [<Optional; DefaultParameterValue(null: LokiCredentials)>] credentials: LokiCredentials,
            [<Optional; DefaultParameterValue(null: string)>] tenant: string,
            // ── OpenTelemetry ─────────────────────────────────────────────────────
            [<Optional; DefaultParameterValue(LokiFieldDestination.None)>] traceIdMode: LokiFieldDestination,
            [<Optional; DefaultParameterValue(LokiFieldDestination.None)>] spanIdMode: LokiFieldDestination,
            // ── Batching ──────────────────────────────────────────────────────────
            [<Optional; DefaultParameterValue(1_000)>] batchSizeLimit: int,
            [<Optional; DefaultParameterValue(50_000)>] queueLimit: int,
            // Nullable<TimeSpan> + a struct-default DefaultParameterValue emits the same
            // [opt]+HasDefault(nullref) metadata as C#'s `TimeSpan? = null`, so
            // Serilog.Settings.Configuration binds it: omitted -> null -> sink default;
            // a supplied "hh:mm:ss" string is converted to TimeSpan by the settings binder.
            [<Optional; DefaultParameterValue(Nullable<TimeSpan>())>] period: Nullable<TimeSpan>,
            [<Optional; DefaultParameterValue(true)>] eagerlyEmitFirstEvent: bool,
            // Omitted -> sink default (10 min).
            [<Optional; DefaultParameterValue(Nullable<TimeSpan>())>] retryTimeLimit: Nullable<TimeSpan>,
            // ── Extension points ──────────────────────────────────────────────────
            [<Optional; DefaultParameterValue(null: ITextFormatter)>] textFormatter: ITextFormatter,
            [<Optional; DefaultParameterValue(null: ILokiExceptionFormatter)>] exceptionFormatter:
                ILokiExceptionFormatter,
            [<Optional; DefaultParameterValue(null: HttpClient)>] httpClient: HttpClient,
            [<Optional; DefaultParameterValue(null: HttpMessageHandler)>] httpMessageHandler: HttpMessageHandler,
            // ── Level ─────────────────────────────────────────────────────────────
            [<Optional; DefaultParameterValue(LevelAlias.Minimum)>] restrictedToMinimumLevel: LogEventLevel
        ) =

        let options =
            {
                Uri = uri
                Labels = if isNull labels then [||] else labels
                PropertiesAsLabels =
                    if isNull propertiesAsLabels then
                        [||]
                    else
                        propertiesAsLabels
                PropertiesAsStructuredMetadata =
                    if isNull propertiesAsStructuredMetadata then
                        [||]
                    else
                        propertiesAsStructuredMetadata
                HandleLogLevelAsLabel = handleLogLevelAsLabel
                Credentials = credentials
                Tenant = tenant
                TraceIdMode = traceIdMode
                SpanIdMode = spanIdMode
                BatchSizeLimit = batchSizeLimit
                QueueLimit = queueLimit
                Period =
                    if period.HasValue then
                        period.Value
                    else
                        LokiSinkOptions.Defaults.Period
                EagerlyEmitFirstEvent = eagerlyEmitFirstEvent
                RetryTimeLimit =
                    if retryTimeLimit.HasValue then
                        retryTimeLimit.Value
                    else
                        LokiSinkOptions.Defaults.RetryTimeLimit
                TextFormatter = textFormatter
                ExceptionFormatter = exceptionFormatter
                HttpClient = httpClient
                HttpMessageHandler = httpMessageHandler
            }

        wire sinkConfig options restrictedToMinimumLevel