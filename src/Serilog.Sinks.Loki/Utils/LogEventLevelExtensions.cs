using Serilog.Events;

namespace Serilog.Sinks.Loki.Utils
{
    internal static class LogEventLevelExtensions
    {
        // TODO: After the release 7.0.0 Grafana will determine log level fatal, so mapping for that level will be redundant
        internal static string ToGrafanaLogLevel(this LogEventLevel level) =>
            level switch
            {
                LogEventLevel.Verbose => "trace",
                LogEventLevel.Debug => "debug",
                LogEventLevel.Information => "info",
                LogEventLevel.Warning => "warning",
                LogEventLevel.Error => "error",
                LogEventLevel.Fatal => "critical",
                _ => "unknown"
            };
    }
}