module Serilog.Sinks.Grafana.Loki.Tests.Helpers

open System
open System.Buffers
open System.Text.Json
open Serilog.Events
open Serilog.Parsing
open Serilog.Sinks.Grafana.Loki
open Serilog.Sinks.Grafana.Loki.Infrastructure

// ── LogEvent factory ─────────────────────────────────────────────────────────

let private parser = MessageTemplateParser()

/// Creates a LogEvent with the given level and scalar properties.
let mkEvent (level: LogEventLevel) (props: (string * obj) list) =
    let properties =
        props
        |> List.map (fun (k, v) -> LogEventProperty(k, ScalarValue(v) :> LogEventPropertyValue))

    LogEvent(DateTimeOffset.UtcNow, level, null, parser.Parse(""), properties)

/// Creates a LogEvent at Information level.
let mkInfo props = mkEvent LogEventLevel.Information props

/// Creates a LogEvent at a given level with a specific timestamp.
let mkEventAt (ts: DateTimeOffset) level props =
    let properties =
        props
        |> List.map (fun (k, v) -> LogEventProperty(k, ScalarValue(v) :> LogEventPropertyValue))

    LogEvent(ts, level, null, parser.Parse(""), properties)

// ── JSON assertion helpers ────────────────────────────────────────────────────

/// Runs ILokiExceptionFormatter.Format and returns the resulting JSON as a
/// JsonDocument for structural assertions.
let serializeException (formatter: ILokiExceptionFormatter) (ex: exn) =
    use buffer = new PooledByteBufferWriter(512)
    use writer = new Utf8JsonWriter(buffer :> IBufferWriter<byte>)
    formatter.Format(writer, ex)
    writer.Flush()
    JsonDocument.Parse(buffer.WrittenMemory)

/// Returns a property value from a JsonElement, or None if absent.
let tryProp (name: string) (el: JsonElement) =
    match el.TryGetProperty(name) with
    | true, v -> Some v
    | _ -> None

/// Asserts a named string property equals the expected value.
let assertStringProp (name: string) (expected: string) (el: JsonElement) =
    match tryProp name el with
    | Some v -> v.GetString() = expected
    | None -> false