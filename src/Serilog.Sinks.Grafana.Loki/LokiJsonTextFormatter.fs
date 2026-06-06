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
open System.Buffers
open System.Text.Json
open Serilog.Events
open Serilog.Formatting
open Serilog.Sinks.Grafana.Loki.Infrastructure

/// Formats log events as a JSON object written to the Loki log entry body.
/// Subclass and override Format or SanitizePropertyName to customize output.
/// Exception serialization is delegated to ILokiExceptionFormatter.
type LokiJsonTextFormatter(exceptionFormatter: ILokiExceptionFormatter, enrichTraceId: bool, enrichSpanId: bool) =

    // ── Statics — must precede constructors and member definitions in F# ──────

    static let pMessage = JsonEncodedText.Encode "Message"
    static let pMessageTemplate = JsonEncodedText.Encode "MessageTemplate"
    static let pException = JsonEncodedText.Encode "Exception"
    static let pTraceId = JsonEncodedText.Encode "TraceId"
    static let pSpanId = JsonEncodedText.Encode "SpanId"

    // Names that collide with top-level JSON keys; prefixed with '_' when seen as properties.
    static let reserved =
        Collections.Generic.HashSet<string>(
            [| "Message"; "MessageTemplate"; "Exception"; "TraceId"; "SpanId" |],
            StringComparer.Ordinal
        )

    // Recursive value renderer — not inline because it calls itself.
    // Uses direct isinst + cast (`:? T as x`) which compiles to allocation-free IL.
    // Complete active patterns allocate their result DU, and [<Struct>] on complete
    // active pattern functions is not supported in the current F# compiler, so the
    // direct match is the correct zero-allocation approach here.
    static let rec writeValue (writer: Utf8JsonWriter) (value: LogEventPropertyValue) =
        match value with
        | :? ScalarValue as sv ->
            match sv.Value with
            | null -> writer.WriteNullValue()
            | :? bool as b -> writer.WriteBooleanValue(b)
            | :? byte as n -> writer.WriteNumberValue(int n)
            | :? int16 as n -> writer.WriteNumberValue(int n)
            | :? int as n -> writer.WriteNumberValue(n)
            | :? int64 as n -> writer.WriteNumberValue(n)
            | :? uint16 as n -> writer.WriteNumberValue(int n)
            | :? uint32 as n -> writer.WriteNumberValue(n)
            | :? uint64 as n -> writer.WriteNumberValue(n)
            | :? float32 as f -> writer.WriteNumberValue(float f)
            | :? float as f ->
                if Double.IsFinite f then
                    writer.WriteNumberValue(f)
                else
                    writer.WriteStringValue(f.ToString())
            | :? decimal as d -> writer.WriteNumberValue(d)
            // Typed overloads format straight to UTF-8 with no intermediate string.
            // Guid → the same 'D' (lowercase, hyphenated) form ToString() produced.
            // DateTime/DateTimeOffset → ISO 8601 (round-trippable, culture-invariant). Note this
            // differs from label rendering: renderLabelValue uses the general invariant format
            // ("MM/dd/yyyy HH:mm:ss"), so the same DateTime can read differently as a label vs body.
            | :? Guid as g -> writer.WriteStringValue(g)
            | :? DateTime as dt -> writer.WriteStringValue(dt)
            | :? DateTimeOffset as dto -> writer.WriteStringValue(dto)
            | v -> writer.WriteStringValue(v.ToString())
        | :? SequenceValue as sv ->
            writer.WriteStartArray()

            for elem in sv.Elements do
                writeValue writer elem

            writer.WriteEndArray()
        | :? StructureValue as sv ->
            writer.WriteStartObject()

            if not (isNull sv.TypeTag) then
                writer.WriteString("$type", sv.TypeTag)

            for prop in sv.Properties do
                writer.WritePropertyName(prop.Name)
                writeValue writer prop.Value

            writer.WriteEndObject()
        | :? DictionaryValue as dv ->
            writer.WriteStartObject()

            for kvp in dv.Elements do
                let key =
                    if isNull kvp.Key.Value then
                        "null"
                    else
                        kvp.Key.Value.ToString()

                writer.WritePropertyName(key)
                writeValue writer kvp.Value

            writer.WriteEndObject()
        | _ -> writer.WriteStringValue(value.ToString())

    do
        if isNull exceptionFormatter then
            nullArg "exceptionFormatter"

    // ── Additional constructors ───────────────────────────────────────────────

    new(exceptionFormatter: ILokiExceptionFormatter) = LokiJsonTextFormatter(exceptionFormatter, false, false)

    new() = LokiJsonTextFormatter(LokiExceptionFormatter(), false, false)

    // ── Internal fast path ────────────────────────────────────────────────────
    // Serialization.fs calls this directly when the formatter is the built-in one,
    // writing JSON bytes straight into the body buffer without a TextWriter round-trip.

    member internal self.FormatToBuffer(logEvent: LogEvent, jsonWriter: Utf8JsonWriter, messageWriter: Utf8TextWriter) =
        // jsonWriter targets the body buffer; the sink reuses one writer across events (Reset
        // clears its state) while the public Format passes a throwaway. The caller clears the
        // backing buffer; Flush at the end pushes pending bytes so the caller can read WrittenSpan.
        jsonWriter.Reset()
        jsonWriter.WriteStartObject()

        // Render the message straight to UTF-8 in the caller's reusable scratch writer, then
        // emit those bytes as the JSON string value. Avoids the StringWriter + StringBuilder +
        // System.String that LogEvent.RenderMessage(IFormatProvider) allocates per event.
        messageWriter.Clear()
        logEvent.RenderMessage(messageWriter, Globalization.CultureInfo.InvariantCulture)
        jsonWriter.WriteString(pMessage, messageWriter.WrittenSpan)

        jsonWriter.WriteString(pMessageTemplate, logEvent.MessageTemplate.Text)

        if not (isNull logEvent.Exception) then
            jsonWriter.WritePropertyName(pException)
            exceptionFormatter.Format(jsonWriter, logEvent.Exception)

        if enrichTraceId then
            let t = logEvent.TraceId

            if t.HasValue then
                jsonWriter.WriteString(pTraceId, t.Value.ToHexString())

        if enrichSpanId then
            let s = logEvent.SpanId

            if s.HasValue then
                jsonWriter.WriteString(pSpanId, s.Value.ToHexString())

        for kvp in logEvent.Properties do
            jsonWriter.WritePropertyName(self.SanitizePropertyName(kvp.Key))
            writeValue jsonWriter kvp.Value

        jsonWriter.WriteEndObject()
        jsonWriter.Flush()

    // ── Virtualizable public surface ──────────────────────────────────────────

    /// Returns a JSON-safe property name. Reserved names are prefixed with '_'.
    abstract member SanitizePropertyName: name: string -> string

    default _.SanitizePropertyName(name: string) =
        if reserved.Contains name then "_" + name else name

    /// Formats logEvent as a JSON object into output.
    abstract member Format: logEvent: LogEvent * output: IO.TextWriter -> unit

    default self.Format(logEvent: LogEvent, output: IO.TextWriter) =
        use buffer = new PooledByteBufferWriter(256)
        // Throwaway writer + scratch for the message render. The internal sink path passes reused
        // instances instead; the public path stays allocation-light but fully thread-safe.
        use jsonWriter = new Utf8JsonWriter(buffer :> IBufferWriter<byte>)
        use messageBuffer = new PooledByteBufferWriter(128)
        use messageWriter = new Utf8TextWriter(messageBuffer)
        self.FormatToBuffer(logEvent, jsonWriter, messageWriter)
        output.Write(Text.Encoding.UTF8.GetString(buffer.WrittenSpan))

    interface ITextFormatter with
        member this.Format(logEvent, output) = this.Format(logEvent, output)