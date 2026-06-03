namespace Serilog.Sinks.Grafana.Loki.Infrastructure

open System
open System.Buffers
open System.Text

/// TextWriter that encodes characters as UTF-8 directly into a PooledByteBufferWriter,
/// eliminating the intermediate string allocation that StringWriter would produce.
[<Sealed>]
type internal Utf8TextWriter(bufferWriter: PooledByteBufferWriter) =
    inherit IO.TextWriter()

    // Hold a typed IBufferWriter<byte> reference to avoid repeated upcasting.
    let writer = bufferWriter :> IBufferWriter<byte>

    override _.Encoding = Encoding.UTF8

    override _.Write(value: char) =
        if int value < 0x80 then
            // ASCII fast path — single byte, no allocation.
            let span = writer.GetSpan(1)
            span[0] <- byte value
            writer.Advance(1)
        else
            // Non-ASCII BMP: up to 3 bytes. Surrogate pairs need 4 but arrive as two
            // separate Write(char) calls, each of which will encode to a replacement
            // character; callers that produce surrogates should use Write(string) instead.
            let span = writer.GetSpan(3)
            let mutable c = value
            let written = Encoding.UTF8.GetBytes(ReadOnlySpan<char>(&c), span)
            writer.Advance(written)

    override _.Write(value: string) =
        if not (String.IsNullOrEmpty value) then
            let maxLen = Encoding.UTF8.GetMaxByteCount(value.Length)
            let span = writer.GetSpan(maxLen)
            let written = Encoding.UTF8.GetBytes(value.AsSpan(), span)
            writer.Advance(written)

    override _.Write(buffer: char[], index: int, count: int) =
        if count > 0 then
            let maxLen = Encoding.UTF8.GetMaxByteCount(count)
            let span = writer.GetSpan(maxLen)
            let written = Encoding.UTF8.GetBytes(ReadOnlySpan<char>(buffer, index, count), span)
            writer.Advance(written)

    override _.Write(value: ReadOnlySpan<char>) =
        if not value.IsEmpty then
            let maxLen = Encoding.UTF8.GetMaxByteCount(value.Length)
            let span = writer.GetSpan(maxLen)
            let written = Encoding.UTF8.GetBytes(value, span)
            writer.Advance(written)

    // Serilog formatters end log lines with WriteLine(); route through Write so
    // the newline is encoded into the same pooled buffer rather than flushed.
    override self.WriteLine(value: string) =
        self.Write(value)
        self.Write('\n')

    override self.WriteLine() =
        self.Write('\n')
