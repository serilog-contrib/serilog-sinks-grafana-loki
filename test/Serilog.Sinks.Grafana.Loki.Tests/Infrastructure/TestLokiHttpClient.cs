using System.Net.Http;
using System.Threading.Tasks;

namespace Serilog.Sinks.Grafana.Loki.Tests.Infrastructure
{
    internal class TestLokiHttpClient : DefaultLokiHttpClient
    {
        public HttpClient Client => HttpClient;

        public string Content { get; private set; }

        public string RequestUri { get; private set; }

        public override async Task<HttpResponseMessage> PostAsync(string requestUri, HttpContent content)
        {
            Content = await content.ReadAsStringAsync();
            RequestUri = requestUri;

            return new HttpResponseMessage();
        }
    }
}