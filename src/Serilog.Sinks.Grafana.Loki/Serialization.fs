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

open System.Buffers
open System.Text.Json
open Serilog.Events
open Serilog.Formatting
open Serilog.Sinks.Grafana.Loki.Infrastructure

/// Writes a Loki push payload directly to a PooledByteBufferWriter via Utf8JsonWriter.
/// No intermediate object graph, no string allocation for the JSON structure.
module internal Serialization =

    // Pre-encoded JSON property names — allocated once, reused for every batch.
    let private jStreams = JsonEncodedText.Encode "streams"
    let private jStream = JsonEncodedText.Encode "stream"
    let private jValues = JsonEncodedText.Encode "values"

    /// Formats a single log event's body into bodyBuffer, then writes
    /// the UTF-8 bytes as a JSON string value into the open jsonWriter.
    ///
    /// When the formatter is LokiJsonTextFormatter we use its internal
    /// FormatToBuffer path — no TextWriter → string round-trip.
    /// For all other ITextFormatter implementations the Utf8TextWriter path is used.
    let private writeBody
        (textFormatter: ITextFormatter)
        (jsonWriter: Utf8JsonWriter)
        (bodyBuffer: PooledByteBufferWriter)
        (event: LogEvent)
        =

        match textFormatter with
        | :? LokiJsonTextFormatter as fmt when fmt.GetType() = typeof<LokiJsonTextFormatter> ->
            fmt.FormatToBuffer(event, bodyBuffer)
        | _ ->
            use textWriter = new Utf8TextWriter(bodyBuffer)
            textFormatter.Format(event, textWriter)

        jsonWriter.WriteStringValue(bodyBuffer.WrittenSpan)
        bodyBuffer.Clear()

    /// Serializes a complete batch of log events into mainBuffer as a Loki push payload:
    ///
    ///   { "streams": [ { "stream": { labels }, "values": [ [ "ts_ns", "body", { meta }? ], ... ] } ] }
    ///
    /// Events are grouped into streams by their label set; within each stream they are
    /// ordered by timestamp. Each entry carries an optional 3rd element of structured
    /// metadata (per-line, not part of grouping), written only when non-empty.
    /// Both buffers are cleared by the caller between ticks.
    let serialize
        (textFormatter: ITextFormatter)
        (labelOf: LogEvent -> LabelSet)
        (metadataOf: LogEvent -> Map<string, string>)
        (mainBuffer: PooledByteBufferWriter)
        (bodyBuffer: PooledByteBufferWriter)
        (batch: LogEvent seq)
        =

        let streams = groupIntoStreams labelOf batch

        use jsonWriter = new Utf8JsonWriter(mainBuffer :> IBufferWriter<byte>)

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
                jsonWriter.WriteStringValue(string (toUnixNanoseconds event.Timestamp))
                writeBody textFormatter jsonWriter bodyBuffer event

                // Optional 3rd element: per-line structured metadata. Omitted when empty
                // so default output stays a 2-element [ ts, body ] entry.
                let metadata = metadataOf event

                if not metadata.IsEmpty then
                    jsonWriter.WriteStartObject()

                    for kvp in metadata do
                        jsonWriter.WriteString(kvp.Key, kvp.Value)

                    jsonWriter.WriteEndObject()

                jsonWriter.WriteEndArray()

            jsonWriter.WriteEndArray()

            jsonWriter.WriteEndObject()

        jsonWriter.WriteEndArray()
        jsonWriter.WriteEndObject()