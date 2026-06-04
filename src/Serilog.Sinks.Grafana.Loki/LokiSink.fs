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
open Serilog.Debugging
open Serilog.Events
open Serilog.Formatting
open Serilog.Sinks.PeriodicBatching
open Serilog.Sinks.Grafana.Loki.Infrastructure

/// IBatchedLogEventSink implementation.
/// Batching, queue management, back-pressure and retry are all delegated to
/// Serilog's PeriodicBatchingSink (wired in LoggerConfigurationExtensions).
[<Sealed>]
type internal LokiSink(options: LokiSinkOptions) =

    // ── Resolved dependencies ─────────────────────────────────────────────────

    let pushUri =
        let base' = Uri(options.Uri)
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

        if not (String.IsNullOrEmpty options.Tenant) then
            client.DefaultRequestHeaders.TryAddWithoutValidation("X-Scope-OrgID", options.Tenant)
            |> ignore

        client

    // ── Label pipeline — precomputed once at construction ─────────────────────

    let globalLabels = buildGlobalLabelMap options.Labels
    let reservedKeys = buildReservedKeys globalLabels options.HandleLogLevelAsLabel

    let labelOf (event: LogEvent) =
        buildLabelSet globalLabels reservedKeys options.PropertiesAsLabels options.HandleLogLevelAsLabel event

    // ── Reusable per-tick buffers ─────────────────────────────────────────────

    let mainBuffer = new PooledByteBufferWriter(4096)
    let bodyBuffer = new PooledByteBufferWriter(256)

    // ── IBatchedLogEventSink ──────────────────────────────────────────────────

    interface IBatchedLogEventSink with

        member _.EmitBatchAsync(batch: IEnumerable<LogEvent>) =
            task {
                mainBuffer.Clear()
                bodyBuffer.Clear()

                Serialization.serialize textFormatter labelOf mainBuffer bodyBuffer batch

                use content = new LokiPushContent(mainBuffer)
                let! response = httpClient.PostAsync(pushUri, content)

                if not response.IsSuccessStatusCode then
                    let! body = response.Content.ReadAsStringAsync()

                    SelfLog.WriteLine(
                        "Received failed result {0} when posting events to Loki: {1}",
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
