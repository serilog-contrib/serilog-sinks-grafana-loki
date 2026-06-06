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
open Serilog.Events

/// Groups log events into Loki streams by their label set.
[<AutoOpen>]
module internal Grouping =

    /// A Loki stream: a unique label set and its events ordered by timestamp.
    type LokiStream =
        { Labels: LabelSet; Events: LogEvent[] }

    /// Equality over only the label-defining fields of an event: its level (when level is a
    /// label) and the values of the properties promoted to labels. Lets events be grouped into
    /// streams without building a Map per event — hash and equality read straight off the event
    /// and, unlike F#'s generic structural comparison, never box.
    ///
    /// `labelProps` must already be filtered to the property names that actually contribute a
    /// label (reserved keys removed), so any two events that compare equal here are guaranteed
    /// to render to the same label set. Grouping by the raw LogEventPropertyValue (rather than
    /// its rendered string) can, in the rare case where two different values render to the same
    /// string, emit two stream objects with identical labels — Loki merges those by label set,
    /// so the payload is harmless, just marginally larger.
    type LabelEqualityComparer(handleLevel: bool, labelProps: string[]) =
        do
            if isNull labelProps then
                nullArg (nameof labelProps)

        /// Builds a comparer for a label configuration, filtering `propertiesAsLabels` down to the
        /// names that actually contribute a label (reserved keys removed) — the precondition the
        /// instance constructor assumes. Constructing through this factory makes that precondition
        /// unforgeable and keeps it in lockstep with buildLabelSet via the shared isReservedLabelKey.
        static member ForLabels(handleLevel: bool, propertiesAsLabels: string[], reservedKeys: Set<string>) =
            let effective =
                if isNull propertiesAsLabels then
                    [||]
                else
                    propertiesAsLabels
                    |> Array.filter (fun p -> not (isReservedLabelKey reservedKeys p))

            LabelEqualityComparer(handleLevel, effective)

        interface IEqualityComparer<LogEvent> with

            member _.GetHashCode(e: LogEvent) =
                let mutable h = HashCode()

                if handleLevel then
                    h.Add e.Level

                for name in labelProps do
                    match e.Properties.TryGetValue name with
                    | true, value ->
                        h.Add name
                        h.Add value
                    | _ -> ()

                h.ToHashCode()

            member _.Equals(x: LogEvent, y: LogEvent) =
                if obj.ReferenceEquals(x, y) then
                    true
                elif obj.ReferenceEquals(x, null) || obj.ReferenceEquals(y, null) then
                    false
                elif handleLevel && x.Level <> y.Level then
                    false
                else
                    // Equal iff every label property is present-with-equal-value (or absent) in
                    // both. Explicit loop + single-TryGetValue matches keep this tuple/closure-free.
                    let mutable equal = true
                    let mutable i = 0

                    while equal && i < labelProps.Length do
                        let name = labelProps[i]

                        equal <-
                            match x.Properties.TryGetValue name with
                            | true, vx ->
                                match y.Properties.TryGetValue name with
                                | true, vy -> vx.Equals vy
                                | _ -> false
                            | _ ->
                                match y.Properties.TryGetValue name with
                                | true, _ -> false
                                | _ -> true

                        i <- i + 1

                    equal

    /// Orders events by ascending timestamp. CompareTo (not the generic `compare`) keeps
    /// the comparison free of boxing; the lambda captures nothing, so the F# compiler emits
    /// a single cached delegate rather than one per stream.
    let private byTimestamp (a: LogEvent) (b: LogEvent) = a.Timestamp.CompareTo b.Timestamp

    /// Groups events into streams using `comparer` for stream identity — no per-event Map — then
    /// derives each stream's label set once, from its head (first-seen) event, via `labelOf`.
    /// Events within a stream are sorted ascending by Timestamp so Loki does not reject
    /// out-of-order entries.
    let groupIntoStreams
        (comparer: IEqualityComparer<LogEvent>)
        (labelOf: LogEvent -> LabelSet)
        (events: LogEvent seq)
        : LokiStream seq =

        let groups = Dictionary<LogEvent, ResizeArray<LogEvent>>(comparer)

        for e in events do
            match groups.TryGetValue e with
            | true, bucket -> bucket.Add e
            | _ ->
                let bucket = ResizeArray<LogEvent>()
                bucket.Add e
                groups.Add(e, bucket)

        let streams = ResizeArray<LokiStream>(groups.Count)

        for kvp in groups do
            // One array per stream, sorted in place — same cost as before, but the label set is
            // now built once here (labelOf on the head) rather than once per event during grouping.
            let arr = kvp.Value.ToArray()
            Array.sortInPlaceWith byTimestamp arr

            streams.Add
                { Labels = labelOf kvp.Key
                  Events = arr }

        streams :> LokiStream seq