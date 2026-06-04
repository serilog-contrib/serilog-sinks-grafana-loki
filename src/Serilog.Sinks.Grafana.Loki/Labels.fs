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
open Serilog.Events

/// Internal label-derivation pipeline.
/// Functions marked `inline` are inlined at every call site — no virtual dispatch,
/// no allocation overhead — important since these run for every event in every batch.
[<AutoOpen>]
module internal Labels =

    /// Label set for a single Loki stream: an immutable ordered map of key → value.
    type LabelSet = Map<string, string>

    // ── Per-event hot-path helpers ────────────────────────────────────────────

    /// Loki requires label keys to start with a letter or underscore.
    /// Positional message template tokens like {0} produce numeric keys — prefix them.
    let inline sanitizeLabelKey (key: string) =
        if key.Length > 0 && Char.IsDigit(key[0]) then
            "param" + key
        else
            key

    /// Maps Serilog levels to Grafana log-level vocabulary.
    let inline logLevelToLabel (level: LogEventLevel) =
        match level with
        | LogEventLevel.Verbose -> "trace"
        | LogEventLevel.Debug -> "debug"
        | LogEventLevel.Information -> "info"
        | LogEventLevel.Warning -> "warning"
        | LogEventLevel.Error -> "error"
        | LogEventLevel.Fatal -> "fatal"
        | _ -> "unknown"

    /// Renders a property value to a plain string suitable for a label value.
    /// Scalars are rendered directly; compound values fall back to Serilog's default
    /// rendering with surrounding quotes stripped.
    let inline renderLabelValue (value: LogEventPropertyValue) =
        match value with
        | :? ScalarValue as sv when not (isNull sv.Value) -> sv.Value.ToString()
        | _ -> value.ToString().Trim('"')

    /// Unix epoch timestamp in nanoseconds as required by the Loki push API.
    /// 1 tick = 100 ns, so Ticks * 100 = nanoseconds.
    let inline toUnixNanoseconds (ts: DateTimeOffset) : int64 =
        (ts - DateTimeOffset.UnixEpoch).Ticks * 100L

    // ── Label set construction ────────────────────────────────────────────────
    // Precomputed once per sink instance; passed into buildLabelSet per-event.

    /// Builds the immutable set of keys that user properties cannot override:
    /// all global label keys plus the synthetic 'level' key when enabled.
    let buildReservedKeys (globalLabels: Map<string, string>) (handleLevel: bool) : Set<string> =
        let keys = globalLabels |> Map.fold (fun acc k _ -> Set.add k acc) Set.empty
        if handleLevel then Set.add "level" keys else keys

    /// Converts the LokiLabel array into an immutable Map, preserving last-write-wins
    /// when duplicate keys are present in the configuration.
    let buildGlobalLabelMap (labels: LokiLabel[]) : Map<string, string> =
        labels |> Array.fold (fun acc l -> Map.add l.Key l.Value acc) Map.empty

    /// Derives the LabelSet for a single log event.
    ///
    /// Priority (highest → lowest):
    ///   1. Global labels  (from sink configuration)
    ///   2. Synthetic 'level' label  (when HandleLogLevelAsLabel = true)
    ///   3. Property-derived labels  (from PropertiesAsLabels)
    ///
    /// Properties whose sanitized key matches a reserved key are silently skipped.
    let buildLabelSet
        (globalLabels: Map<string, string>)
        (reservedKeys: Set<string>)
        (propertiesAsLabels: string[])
        (handleLevel: bool)
        (event: LogEvent)
        : LabelSet =

        // Start from globals, optionally inject the level label.
        let base' =
            if handleLevel then
                Map.add "level" (logLevelToLabel event.Level) globalLabels
            else
                globalLabels

        // Fold property-as-label entries on top, skipping reserved keys.
        propertiesAsLabels
        |> Array.fold
            (fun acc propName ->
                match event.Properties.TryGetValue(propName) with
                | true, value ->
                    let key = sanitizeLabelKey propName

                    if Set.contains key reservedKeys then
                        acc
                    else
                        Map.add key (renderLabelValue value) acc
                | _ -> acc)
            base'

    // ── Test shim ─────────────────────────────────────────────────────────────
    // toUnixNanoseconds needs a direct precision test but is inline (can't be
    // called across assembly boundaries even with InternalsVisibleTo).
    // This non-inline wrapper exists solely for the UnitTests project.
    let timestampToNs (ts: DateTimeOffset) : int64 = toUnixNanoseconds ts
