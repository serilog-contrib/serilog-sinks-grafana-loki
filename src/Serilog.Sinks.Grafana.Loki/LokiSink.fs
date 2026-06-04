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
open System.Collections.Generic
open System.Net.Http
open System.Net.Http.Headers
open System.Text
open Serilog.Core
open Serilog.Debugging
open Serilog.Events
open Serilog.Formatting
open Serilog.Sinks.Grafana.Loki.Infrastructure

/// IBatchedLogEventSink implementation.
/// Batching, queue management, back-pressure and retry are all delegated to
/// Serilog's native batching infrastructure (wired in LoggerConfigurationExtensions).
[<Sealed>]
type internal LokiSink(options: LokiSinkOptions) =

    // ── Resolved dependencies ─────────────────────────────────────────────────

    let pushUri =
        // RFC 3986 §5.2.2: relative resolution strips everything right of the last '/'
        // in the base path. Ensure the base always ends with '/' so "loki/api/v1/push"
        // appends correctly even when the user omits the trailing slash on a path prefix.
        let raw = options.Uri
        let base' = Uri(if raw.EndsWith('/') then raw else raw + "/")
        Uri(base', "loki/api/v1/push")

    let textFormatter: ITextFormatter =
        if isNull options.TextFormatter then
            let exFmt: ILokiExceptionFormatter =
                if isNull options.ExceptionFormatter then
                    LokiExceptionFormatter()
                else
                    options.ExceptionFormatter

            LokiJsonTextFormatter(exFmt, options.EnrichTraceId, options.EnrichSpanId)
        else
            options.TextFormatter

    let ownsClient = isNull options.HttpClient

    let httpClient: HttpClient =
        let client =
            if ownsClient then
                if isNull options.HttpMessageHandler then
                    new HttpClient()
                else
                    new HttpClient(options.HttpMessageHandler)
            else
                options.HttpClient

        // Apply Basic Auth only to a client we created — injected clients are pre-configured.
        // box coerces the record to obj so isNull works without [<AllowNullLiteral>].
        if ownsClient && not (isNull (box options.Credentials)) then
            let c = options.Credentials

            if not (String.IsNullOrEmpty c.Login) then
                let token =
                    $"{c.Login}:{c.Password}" |> Encoding.UTF8.GetBytes |> Convert.ToBase64String

                client.DefaultRequestHeaders.Authorization <- AuthenticationHeaderValue("Basic", token)

        if ownsClient && not (String.IsNullOrEmpty options.Tenant) then
            if not (client.DefaultRequestHeaders.TryAddWithoutValidation("X-Scope-OrgID", options.Tenant)) then
                SelfLog.WriteLine(
                    "Serilog.Sinks.GrafanaLoki: X-Scope-OrgID header could not be set for tenant '{0}'. The value may contain invalid characters.",
                    options.Tenant
                )

        client

    // ── Label pipeline — precomputed once at construction ─────────────────────

    let globalLabels =
        buildGlobalLabelMap (if isNull options.Labels then [||] else options.Labels)

    let reservedKeys = buildReservedKeys globalLabels options.HandleLogLevelAsLabel

    let propertiesAsLabels =
        if isNull options.PropertiesAsLabels then
            [||]
        else
            options.PropertiesAsLabels

    let labelOf (event: LogEvent) =
        buildLabelSet globalLabels reservedKeys propertiesAsLabels options.HandleLogLevelAsLabel event

    // ── Reusable per-tick buffers ─────────────────────────────────────────────

    let mainBuffer = new PooledByteBufferWriter(4096)
    let bodyBuffer = new PooledByteBufferWriter(256)

    // ── IBatchedLogEventSink ──────────────────────────────────────────────────

    interface IBatchedLogEventSink with

        member _.EmitBatchAsync(batch: IReadOnlyCollection<LogEvent>) =
            task {
                mainBuffer.Clear()
                bodyBuffer.Clear()

                try
                    Serialization.serialize textFormatter labelOf mainBuffer bodyBuffer batch
                with ex ->
                    SelfLog.WriteLine(
                        "Serilog.Sinks.GrafanaLoki: serialization failed for batch of {0} events: {1}",
                        batch.Count,
                        ex
                    )

                    raise ex

                use content = new LokiPushContent(mainBuffer)
                use! response = httpClient.PostAsync(pushUri, content)

                if not response.IsSuccessStatusCode then
                    let! body =
                        if isNull response.Content then
                            Threading.Tasks.Task.FromResult("<no response body>")
                        else
                            response.Content.ReadAsStringAsync()

                    SelfLog.WriteLine(
                        "Serilog.Sinks.GrafanaLoki: received {0} from Loki; body: {1}",
                        response.StatusCode,
                        body
                    )

                    response.EnsureSuccessStatusCode() |> ignore
            }

        member _.OnEmptyBatchAsync() = Threading.Tasks.Task.CompletedTask

    interface IDisposable with
        member _.Dispose() =
            if ownsClient then
                httpClient.Dispose()

            (mainBuffer :> IDisposable).Dispose()
            (bodyBuffer :> IDisposable).Dispose()