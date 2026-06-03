namespace Serilog.Sinks.Grafana.Loki

open System.Net
open System.Net.Http
open System.Net.Http.Headers
open Serilog.Sinks.Grafana.Loki.Infrastructure

/// HttpContent that streams a pre-serialized Loki push payload from a PooledByteBufferWriter
/// directly into the HTTP body — no additional copy or allocation.
[<Sealed>]
type internal LokiPushContent(buffer: PooledByteBufferWriter) =
    inherit HttpContent()

    do base.Headers.ContentType <- MediaTypeHeaderValue("application/json", CharSet = "utf-8")

    // Both overloads must be provided: the 2-parameter form is the original abstract;
    // the 3-parameter form (with CancellationToken) was added in .NET 6.
    override _.SerializeToStreamAsync(stream, _context) =
        stream.WriteAsync(buffer.WrittenMemory).AsTask()

    override _.SerializeToStreamAsync(stream, _context, cancellationToken) =
        stream.WriteAsync(buffer.WrittenMemory, cancellationToken).AsTask()

    override _.TryComputeLength(length: int64 byref) =
        length <- int64 buffer.WrittenCount
        true
