namespace Serilog.Sinks.Grafana.Loki

open System
open System.Runtime.CompilerServices
open System.Runtime.InteropServices
open Serilog
open Serilog.Configuration
open Serilog.Events
open Serilog.Sinks.PeriodicBatching

/// Registers the Grafana Loki sink with a LoggerConfiguration.
[<Extension>]
type LoggerConfigurationLokiExtensions private () =

    static let validateUri (uri: string) =
        if String.IsNullOrWhiteSpace uri then
            invalidArg "uri" "Loki URI must not be null or empty."
        match Uri.TryCreate(uri, UriKind.Absolute) with
        | false, _ ->
            invalidArg "uri" $"'{uri}' is not a valid absolute URI."
        | true, u when u.Scheme <> "http" && u.Scheme <> "https" ->
            invalidArg "uri" $"Loki URI scheme must be http or https, got '{u.Scheme}'."
        | _ -> ()

    static let buildBatchingOptions (options: LokiSinkOptions) =
        PeriodicBatchingSinkOptions(
            BatchSizeLimit        = options.BatchSizeLimit,
            Period                = options.Period,
            EagerlyEmitFirstEvent = options.EagerlyEmitFirstEvent,
            QueueLimit            = Nullable options.QueueLimit)

    static let wire
        (sinkConfig: LoggerSinkConfiguration)
        (options: LokiSinkOptions)
        (level: LogEventLevel) =

        validateUri options.Uri

        let sink    = new LokiSink(options)
        let batched = new PeriodicBatchingSink(sink, buildBatchingOptions options)
        sinkConfig.Sink(batched, level)

    // ── Primary overload — full options object ────────────────────────────────

    /// Writes log events to Grafana Loki using the provided options.
    [<Extension>]
    static member GrafanaLoki
        (   sinkConfig: LoggerSinkConfiguration,
            options: LokiSinkOptions,
            [<Optional; DefaultParameterValue(LevelAlias.Minimum)>]
            restrictedToMinimumLevel: LogEventLevel) =
        wire sinkConfig options restrictedToMinimumLevel

    // ── Convenience overload — URI only ───────────────────────────────────────

    /// Writes log events to Grafana Loki at the given URI using default options.
    /// All other settings can be tuned via the options object overload.
    [<Extension>]
    static member GrafanaLoki
        (   sinkConfig: LoggerSinkConfiguration,
            uri: string,
            [<Optional; DefaultParameterValue(LevelAlias.Minimum)>]
            restrictedToMinimumLevel: LogEventLevel) =
        wire sinkConfig { LokiSinkOptions.Defaults with Uri = uri } restrictedToMinimumLevel
