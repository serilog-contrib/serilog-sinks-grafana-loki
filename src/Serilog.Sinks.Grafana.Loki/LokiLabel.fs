namespace Serilog.Sinks.Grafana.Loki

/// A label applied to every log stream emitted by the sink.
[<CLIMutable>]
type LokiLabel = {
    Key: string
    Value: string
}
