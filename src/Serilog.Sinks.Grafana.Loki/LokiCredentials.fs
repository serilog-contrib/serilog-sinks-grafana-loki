namespace Serilog.Sinks.Grafana.Loki

/// Basic authentication credentials for Loki.
/// Leave null on LokiSinkOptions to disable authentication.
[<CLIMutable>]
type LokiCredentials = {
    Login: string
    Password: string
}
