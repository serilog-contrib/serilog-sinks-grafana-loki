using Serilog.Sinks.Http;

namespace Serilog.Sinks.Loki.Sinks.Loki
{
    public interface ILokiHttpClient : IHttpClient
    {
        void SetCredentials(LokiCredentials credentials);
    }
}