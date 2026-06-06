module Serilog.Sinks.Grafana.Loki.Tests.GroupingTests

open System
open System.Collections.Generic
open Swensen.Unquote
open Xunit
open Serilog.Events
open Serilog.Sinks.Grafana.Loki
open Serilog.Sinks.Grafana.Loki.Tests.Helpers

// Grouping is now driven by an IEqualityComparer<LogEvent> (stream identity) plus a labelOf
// that derives each stream's written label set from its head event. These helpers build the
// comparers/label functions the production sink would, so the tests exercise the real path.

/// Comparer that puts every event in one stream (no level, no property labels).
let private single =
    LabelEqualityComparer(false, [||]) :> IEqualityComparer<LogEvent>

/// Comparer that groups by log level (the default sink behaviour).
let private byLevel =
    LabelEqualityComparer(true, [||]) :> IEqualityComparer<LogEvent>

/// Comparer that groups by a single promoted property.
let private byProp name =
    LabelEqualityComparer(false, [| name |]) :> IEqualityComparer<LogEvent>

/// labelOf returning a fixed single-label set, regardless of the event.
let private fixedLabel key value : LogEvent -> LabelSet = fun _ -> Map.ofList [ key, value ]

/// labelOf deriving the synthetic level label (via buildLabelSet, since logLevelToLabel is inline).
let private levelLabel (ev: LogEvent) : LabelSet =
    buildLabelSet Map.empty (buildReservedKeys Map.empty true) [||] true ev

/// labelOf promoting a single property to a label.
let private propLabel name (ev: LogEvent) : LabelSet =
    buildLabelSet Map.empty (buildReservedKeys Map.empty false) [| name |] false ev

// ── Empty input ───────────────────────────────────────────────────────────────

[<Fact>]
let ``groupIntoStreams: empty batch produces no streams`` () =
    let streams = groupIntoStreams single (fixedLabel "app" "x") Seq.empty |> Seq.toList
    test <@ streams = [] @>

// ── Single stream ─────────────────────────────────────────────────────────────

[<Fact>]
let ``groupIntoStreams: events with same labels go into one stream`` () =
    let events = [ mkInfo []; mkInfo []; mkInfo [] ]
    let streams = groupIntoStreams byLevel levelLabel events |> Seq.toList
    test <@ streams.Length = 1 @>
    test <@ streams[0].Events.Length = 3 @>

[<Fact>]
let ``groupIntoStreams: single event produces one stream with one entry`` () =
    let event = mkInfo []
    let streams = groupIntoStreams single (fixedLabel "app" "x") [ event ] |> Seq.toList
    test <@ streams.Length = 1 @>
    test <@ streams[0].Events.Length = 1 @>

// ── Multiple streams ──────────────────────────────────────────────────────────

[<Fact>]
let ``groupIntoStreams: events with different labels go into separate streams`` () =
    let infoEvent = mkEvent LogEventLevel.Information []
    let errorEvent = mkEvent LogEventLevel.Error []

    let streams =
        groupIntoStreams byLevel levelLabel [ infoEvent; errorEvent ] |> Seq.toList

    test <@ streams.Length = 2 @>

[<Fact>]
let ``groupIntoStreams: correct events assigned to each stream`` () =
    let e1 = mkEvent LogEventLevel.Information []
    let e2 = mkEvent LogEventLevel.Error []
    let e3 = mkEvent LogEventLevel.Information []

    let streams =
        groupIntoStreams byLevel levelLabel [ e1; e2; e3 ]
        |> Seq.toList
        |> List.sortBy (fun s -> s.Labels["level"])

    let infoStream = streams |> List.find (fun s -> s.Labels["level"] = "info")
    let errorStream = streams |> List.find (fun s -> s.Labels["level"] = "error")

    test <@ infoStream.Events.Length = 2 @>
    test <@ errorStream.Events.Length = 1 @>

// ── Timestamp ordering within a stream ───────────────────────────────────────

[<Fact>]
let ``groupIntoStreams: events within a stream are sorted ascending by timestamp`` () =
    let t1 = DateTimeOffset(2024, 1, 1, 0, 0, 1, TimeSpan.Zero)
    let t2 = DateTimeOffset(2024, 1, 1, 0, 0, 2, TimeSpan.Zero)
    let t3 = DateTimeOffset(2024, 1, 1, 0, 0, 3, TimeSpan.Zero)

    // Enqueue out of order
    let events =
        [ mkEventAt t3 LogEventLevel.Information []
          mkEventAt t1 LogEventLevel.Information []
          mkEventAt t2 LogEventLevel.Information [] ]

    let stream = groupIntoStreams single (fixedLabel "app" "x") events |> Seq.exactlyOne
    let timestamps = stream.Events |> Seq.map (fun e -> e.Timestamp) |> Seq.toList

    test <@ timestamps = [ t1; t2; t3 ] @>

[<Fact>]
let ``groupIntoStreams: ordering is per-stream, not global`` () =
    let t1 = DateTimeOffset(2024, 1, 1, 0, 0, 1, TimeSpan.Zero)
    let t2 = DateTimeOffset(2024, 1, 1, 0, 0, 2, TimeSpan.Zero)
    let t3 = DateTimeOffset(2024, 1, 1, 0, 0, 3, TimeSpan.Zero)

    // Error at t3, two Info at t2 and t1 — they should be in their own sorted streams
    let events =
        [ mkEventAt t2 LogEventLevel.Information []
          mkEventAt t3 LogEventLevel.Error []
          mkEventAt t1 LogEventLevel.Information [] ]

    let streams = groupIntoStreams byLevel levelLabel events |> Seq.toList
    let infoStream = streams |> List.find (fun s -> s.Labels["level"] = "info")

    test <@ infoStream.Events[0].Timestamp = t1 @>
    test <@ infoStream.Events[1].Timestamp = t2 @>

// ── Label set identity ────────────────────────────────────────────────────────

[<Fact>]
let ``groupIntoStreams: stream labels match the computed label set`` () =
    let event = mkInfo []

    let stream =
        groupIntoStreams single (fixedLabel "env" "staging") [ event ] |> Seq.exactlyOne

    test <@ stream.Labels["env"] = "staging" @>

[<Fact>]
let ``groupIntoStreams: three distinct label values produce three streams`` () =
    // Three events whose promoted "i" property differs → three streams.
    let events = [ mkInfo [ "i", box 0 ]; mkInfo [ "i", box 1 ]; mkInfo [ "i", box 2 ] ]

    let streams = groupIntoStreams (byProp "i") (propLabel "i") events |> Seq.toList

    test <@ streams.Length = 3 @>

// ── LabelEqualityComparer (stream identity) ───────────────────────────────────

[<Fact>]
let ``LabelEqualityComparer: events differing only in a non-label property are equal`` () =
    let cmp = byLevel
    let a = mkInfo [ "x", box 1 ] // x is not a label property
    let b = mkInfo [ "x", box 2 ]
    let equal = cmp.Equals(a, b)
    let sameHash = cmp.GetHashCode(a) = cmp.GetHashCode(b)
    test <@ equal && sameHash @>

[<Fact>]
let ``LabelEqualityComparer: events differing in a label property are not equal`` () =
    let cmp = byProp "env"
    let a = mkInfo [ "env", box "prod" ]
    let b = mkInfo [ "env", box "dev" ]
    let equal = cmp.Equals(a, b)
    test <@ not equal @>

[<Fact>]
let ``LabelEqualityComparer: events with the same label value are equal`` () =
    let cmp = byProp "env"
    let a = mkInfo [ "env", box "prod" ]
    let b = mkInfo [ "env", box "prod" ]
    let equal = cmp.Equals(a, b)
    let sameHash = cmp.GetHashCode(a) = cmp.GetHashCode(b)
    test <@ equal && sameHash @>

[<Fact>]
let ``LabelEqualityComparer: multi-property identity including an absent key`` () =
    // Two label properties: covers the absent-in-one vs absent-in-both branches of Equals.
    let cmp =
        LabelEqualityComparer(false, [| "a"; "b" |]) :> IEqualityComparer<LogEvent>

    let ab1 = mkInfo [ "a", box 1; "b", box 1 ]
    let ab2 = mkInfo [ "a", box 1; "b", box 2 ] // b differs
    let aOnly1 = mkInfo [ "a", box 1 ] // b absent
    let aOnly2 = mkInfo [ "a", box 1 ] // b absent (both)

    let differingValueNotEqual = cmp.Equals(ab1, ab2)
    let absentVsPresentNotEqual = cmp.Equals(ab1, aOnly1)
    let absentInBothEqual = cmp.Equals(aOnly1, aOnly2)
    let absentInBothSameHash = cmp.GetHashCode(aOnly1) = cmp.GetHashCode(aOnly2)

    test <@ not differingValueNotEqual @>
    test <@ not absentVsPresentNotEqual @>
    test <@ absentInBothEqual && absentInBothSameHash @>