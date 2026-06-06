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
open System.Globalization
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
        | :? ScalarValue as sv when not (isNull sv.Value) ->
            // Use InvariantCulture so float/DateTime labels are locale-independent
            // and stream keys are deterministic across machines.
            match sv.Value with
            | :? IFormattable as f -> f.ToString(null, CultureInfo.InvariantCulture)
            | v -> v.ToString()
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
        let keys = globalLabels |> Map.keys |> Set.ofSeq
        if handleLevel then Set.add "level" keys else keys

    /// True when a property's sanitized key is reserved (collides with a global label or the
    /// synthetic 'level'), in which case the property contributes no label. Single source of this
    /// rule, shared by buildLabelSet (label output) and LabelEqualityComparer.ForLabels (stream
    /// identity) so the two cannot drift out of sync.
    let inline isReservedLabelKey (reservedKeys: Set<string>) (propName: string) =
        Set.contains (sanitizeLabelKey propName) reservedKeys

    /// Converts the LokiLabel array into an immutable Map, preserving last-write-wins
    /// when duplicate keys are present in the configuration.
    let buildGlobalLabelMap (labels: LokiLabel[]) : Map<string, string> =
        // Apply sanitizeLabelKey so global labels follow the same key rules as
        // property-derived labels (numeric-starting keys get "param" prefix).
        labels
        |> Array.fold (fun acc l -> Map.add (sanitizeLabelKey l.Key) l.Value acc) Map.empty

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
                | true, value when not (isReservedLabelKey reservedKeys propName) ->
                    Map.add (sanitizeLabelKey propName) (renderLabelValue value) acc
                | _ -> acc)
            base'

    // Structured metadata (per-line, not part of stream grouping) is now written directly
    // to the JSON writer by the sink's writeMetadata — see LokiSink.fs. No intermediate Map.

    // ── Test shim ─────────────────────────────────────────────────────────────
    // toUnixNanoseconds needs a direct precision test but is inline (can't be
    // called across assembly boundaries even with InternalsVisibleTo).
    // This non-inline wrapper exists solely for the UnitTests project.
    let timestampToNs (ts: DateTimeOffset) : int64 = toUnixNanoseconds ts