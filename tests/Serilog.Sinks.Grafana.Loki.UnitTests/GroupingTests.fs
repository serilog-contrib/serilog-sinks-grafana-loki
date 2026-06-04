module Serilog.Sinks.Grafana.Loki.Tests.GroupingTests

open System
open Swensen.Unquote
open Xunit
open Serilog.Events
open Serilog.Sinks.Grafana.Loki
open Serilog.Sinks.Grafana.Loki.Tests.Helpers

// All grouping tests use a trivial labelOf that returns a fixed or property-derived label set,
// isolating grouping behaviour from label derivation logic (tested in LabelsTests.fs).

let private fixedLabel key value : LogEvent -> LabelSet = fun _ -> Map.ofList [ key, value ]

// Use buildLabelSet to derive the level label — avoids calling the inline
// logLevelToLabel directly across assembly boundaries.
let private levelLabel (ev: LogEvent) : LabelSet =
    buildLabelSet Map.empty (buildReservedKeys Map.empty true) [||] true ev

// ── Empty input ───────────────────────────────────────────────────────────────

[<Fact>]
let ``groupIntoStreams: empty batch produces no streams`` () =
    let streams = groupIntoStreams (fixedLabel "app" "x") Seq.empty |> Seq.toList
    test <@ streams = [] @>

// ── Single stream ─────────────────────────────────────────────────────────────

[<Fact>]
let ``groupIntoStreams: events with same labels go into one stream`` () =
    let events = [ mkInfo []; mkInfo []; mkInfo [] ]
    let streams = groupIntoStreams (fixedLabel "app" "svc") events |> Seq.toList
    test <@ streams.Length = 1 @>
    test <@ streams[0].Events.Length = 3 @>

[<Fact>]
let ``groupIntoStreams: single event produces one stream with one entry`` () =
    let event = mkInfo []
    let streams = groupIntoStreams (fixedLabel "app" "x") [ event ] |> Seq.toList
    test <@ streams.Length = 1 @>
    test <@ streams[0].Events.Length = 1 @>

// ── Multiple streams ──────────────────────────────────────────────────────────

[<Fact>]
let ``groupIntoStreams: events with different labels go into separate streams`` () =
    let infoEvent = mkEvent LogEventLevel.Information []
    let errorEvent = mkEvent LogEventLevel.Error []
    let streams = groupIntoStreams levelLabel [ infoEvent; errorEvent ] |> Seq.toList
    test <@ streams.Length = 2 @>

[<Fact>]
let ``groupIntoStreams: correct events assigned to each stream`` () =
    let e1 = mkEvent LogEventLevel.Information []
    let e2 = mkEvent LogEventLevel.Error []
    let e3 = mkEvent LogEventLevel.Information []

    let streams =
        groupIntoStreams levelLabel [ e1; e2; e3 ]
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

    let stream = groupIntoStreams (fixedLabel "app" "x") events |> Seq.exactlyOne
    let timestamps = stream.Events |> List.map (fun e -> e.Timestamp)

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

    let streams = groupIntoStreams levelLabel events |> Seq.toList
    let infoStream = streams |> List.find (fun s -> s.Labels["level"] = "info")

    test <@ infoStream.Events[0].Timestamp = t1 @>
    test <@ infoStream.Events[1].Timestamp = t2 @>

// ── Label set identity ────────────────────────────────────────────────────────

[<Fact>]
let ``groupIntoStreams: stream labels match the computed label set`` () =
    let event = mkInfo []

    let stream =
        groupIntoStreams (fixedLabel "env" "staging") [ event ] |> Seq.exactlyOne

    test <@ stream.Labels["env"] = "staging" @>

[<Fact>]
let ``groupIntoStreams: three distinct label sets produce three streams`` () =
    let lf i : LogEvent -> LabelSet = fun _ -> Map.ofList [ "i", string i ]

    let events =
        [ mkInfo [] // label i=0
          mkInfo [] // label i=1
          mkInfo [] ] // label i=2
    // Give each event a distinct label
    let mutable idx = 0

    let streams =
        groupIntoStreams
            (fun _ ->
                let i = idx
                idx <- idx + 1
                Map.ofList [ "i", string i ])
            events
        |> Seq.toList

    test <@ streams.Length = 3 @>
