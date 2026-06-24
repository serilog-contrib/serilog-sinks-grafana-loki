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

#nowarn "9" // NativePtr.stackalloc: stack scratch for timestamp formatting (no heap allocation)

open System
open System.Buffers
open System.Buffers.Text
open System.Collections.Generic
open System.Text.Encodings.Web
open System.Text.Json
open Microsoft.FSharp.NativeInterop
open Serilog.Events
open Serilog.Formatting
open Serilog.Sinks.Grafana.Loki.Infrastructure

/// Relaxed JSON escaping for the bytes the sink emits. The default Utf8JsonWriter encoder is
/// HTML-safe and renders every '"', '<', '>', '&', '\'' and all non-ASCII as \uXXXX, which makes
/// the stored log line unreadable; the relaxed encoder escapes only what JSON mandates and emits
/// non-ASCII verbatim. Output stays valid JSON (and valid UTF-8), so consumers are unaffected.
[<AutoOpen>]
module private JsonWriterDefaults =
    let relaxedWriterOptions =
        JsonWriterOptions(Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping)

/// Reusable per-sink serialization scratch: the buffers and writers reused across every batch
/// (cleared/reset between uses), bundled so the serializer takes one value instead of several
/// same-typed positional args (easy to mis-order), and disposed as a unit by the sink.
[<Sealed>]
type internal SerializationBuffers() =
    let main = new PooledByteBufferWriter(4096)
    let body = new PooledByteBufferWriter(256)
    let message = new PooledByteBufferWriter(256)

    let bodyWriter =
        new Utf8JsonWriter(body :> IBufferWriter<byte>, relaxedWriterOptions)

    let messageWriter = new Utf8TextWriter(message)

    /// Envelope buffer holding the full push payload; read by LokiPushContent after serialize.
    member _.Main = main
    /// Per-event body scratch; holds one event's JSON body, cleared between events.
    member _.Body = body
    /// Reused writer for the event body (Reset between events).
    member _.BodyWriter = bodyWriter
    /// Reused writer for rendering each event's message to UTF-8.
    member _.MessageWriter = messageWriter

    interface IDisposable with
        member _.Dispose() =
            (bodyWriter :> IDisposable).Dispose()
            (messageWriter :> IDisposable).Dispose()
            (main :> IDisposable).Dispose()
            (body :> IDisposable).Dispose()
            (message :> IDisposable).Dispose()

/// Writes a Loki push payload directly to a PooledByteBufferWriter via Utf8JsonWriter.
/// No intermediate object graph, no string allocation for the JSON structure.
module internal Serialization =

    // Pre-encoded JSON property names — allocated once, reused for every batch.
    let private jStreams = JsonEncodedText.Encode "streams"
    let private jStream = JsonEncodedText.Encode "stream"
    let private jValues = JsonEncodedText.Encode "values"

    /// Serializes a complete batch of log events into mainBuffer as a Loki push payload:
    ///
    ///   { "streams": [ { "stream": { labels }, "values": [ [ "ts_ns", "body", { meta }? ], ... ] } ] }
    ///
    /// Events are grouped into streams by their label set; within each stream they are
    /// ordered by timestamp. Each entry carries an optional 3rd element of structured
    /// metadata (per-line, not part of grouping), written only when non-empty.
    /// buffers.Main / buffers.Body are cleared by the caller between ticks.
    let serialize
        (textFormatter: ITextFormatter)
        (labelComparer: IEqualityComparer<LogEvent>)
        (labelOf: LogEvent -> LabelSet)
        (writeMetadata: Utf8JsonWriter -> LogEvent -> unit)
        (buffers: SerializationBuffers)
        (batch: LogEvent seq)
        =

        let streams = groupIntoStreams labelComparer labelOf batch

        // Resolve the body-writing strategy once per batch (the formatter is fixed for the sink):
        // the built-in formatter writes JSON bytes straight to bodyBuffer via the reused
        // bodyJsonWriter; any other ITextFormatter goes through Utf8TextWriter. Null here means
        // "custom formatter" — keeps the isinst + GetType() check out of the per-event loop.
        let builtIn =
            match textFormatter with
            | :? LokiJsonTextFormatter as fmt when fmt.GetType() = typeof<LokiJsonTextFormatter> -> fmt
            | _ -> Unchecked.defaultof<LokiJsonTextFormatter>

        use jsonWriter =
            new Utf8JsonWriter(buffers.Main :> IBufferWriter<byte>, relaxedWriterOptions)

        // Reused stack scratch for the per-event Unix-nanosecond timestamp. Hoisted out of
        // the event loop so the localloc happens once per batch, not once per event. An
        // int64 renders to at most 20 ASCII bytes (19 digits + sign).
        let tsPtr = NativePtr.stackalloc<byte> 20
        let tsScratch = Span<byte>(NativePtr.toVoidPtr tsPtr, 20)

        jsonWriter.WriteStartObject()
        jsonWriter.WritePropertyName(jStreams)
        jsonWriter.WriteStartArray()

        for stream in streams do
            jsonWriter.WriteStartObject()

            jsonWriter.WritePropertyName(jStream)
            jsonWriter.WriteStartObject()

            for kvp in stream.Labels do
                jsonWriter.WriteString(kvp.Key, kvp.Value)

            jsonWriter.WriteEndObject()

            jsonWriter.WritePropertyName(jValues)
            jsonWriter.WriteStartArray()

            for event in stream.Events do
                jsonWriter.WriteStartArray()

                let mutable tsLen = 0

                Utf8Formatter.TryFormat(toUnixNanoseconds event.Timestamp, tsScratch, &tsLen)
                |> ignore

                jsonWriter.WriteStringValue(ReadOnlySpan<byte>(NativePtr.toVoidPtr tsPtr, tsLen))

                // Body: built-in fast path writes JSON straight to bodyBuffer (via the reused
                // writer); a custom ITextFormatter writes text through Utf8TextWriter. Either way
                // bodyBuffer then holds the UTF-8 body, which becomes the 2nd "values" element.
                if not (obj.ReferenceEquals(builtIn, null)) then
                    builtIn.FormatToBuffer(event, buffers.BodyWriter, buffers.MessageWriter)
                else
                    use textWriter = new Utf8TextWriter(buffers.Body)
                    textFormatter.Format(event, textWriter)

                jsonWriter.WriteStringValue(buffers.Body.WrittenSpan)
                buffers.Body.Clear()

                // Optional 3rd element: per-line structured metadata, written straight to the
                // writer (opened lazily, omitted when empty so default output stays [ ts, body ]).
                writeMetadata jsonWriter event

                jsonWriter.WriteEndArray()

            jsonWriter.WriteEndArray()

            jsonWriter.WriteEndObject()

        jsonWriter.WriteEndArray()
        jsonWriter.WriteEndObject()