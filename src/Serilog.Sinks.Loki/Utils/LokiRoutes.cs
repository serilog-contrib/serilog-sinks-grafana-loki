namespace Serilog.Sinks.Loki.Utils
{
    internal static class LokiRoutes
    {
        private const string LogEntriesEndpoint = "/loki/api/v1/push";

        public static string BuildLogsEntriesRoute(string host) => string.Join(host.TrimEnd('/'), LogEntriesEndpoint);
    }
}