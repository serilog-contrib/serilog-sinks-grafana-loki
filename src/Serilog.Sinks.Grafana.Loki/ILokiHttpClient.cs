using Serilog.Sinks.Http;

namespace Serilog.Sinks.Grafana.Loki
{
    public interface ILokiHttpClient : IHttpClient
    {
        void SetCredentials(LokiCredentials credentials);
    }
}