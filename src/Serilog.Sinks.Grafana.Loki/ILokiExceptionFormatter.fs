namespace Serilog.Sinks.Grafana.Loki

open System.Text.Json

/// Serializes exceptions into the Loki log entry JSON body.
/// Replace the default on LokiSinkOptions to scrub PII, change structure, or suppress detail.
[<AllowNullLiteral>]
type ILokiExceptionFormatter =
    abstract Format: writer: Utf8JsonWriter * ex: exn -> unit
