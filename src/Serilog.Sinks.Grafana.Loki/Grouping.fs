namespace Serilog.Sinks.Grafana.Loki

open Serilog.Events

/// Groups log events into Loki streams by their computed label set.
[<AutoOpen>]
module internal Grouping =

    /// A Loki stream: a unique label set and its events ordered by timestamp.
    type LokiStream = {
        Labels: LabelSet
        Events: LogEvent list
    }

    /// Assigns a LabelSet to every event then groups into streams.
    /// Within each stream events are sorted ascending by Timestamp so Loki
    /// does not reject out-of-order entries.
    let groupIntoStreams
        (labelOf: LogEvent -> LabelSet)
        (events: LogEvent seq)
        : LokiStream seq =

        events
        |> Seq.groupBy labelOf
        |> Seq.map (fun (labels, evs) ->
            {   Labels = labels
                Events = evs |> Seq.sortBy (fun e -> e.Timestamp) |> List.ofSeq })
