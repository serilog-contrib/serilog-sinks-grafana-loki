using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Serilog.Sinks.Grafana.Loki.HttpClients;
using Serilog.Sinks.Grafana.Loki.Utils;

namespace Serilog.Sinks.Grafana.Loki.Tests.TestHelpers
{
    internal class TestLokiHttpClient : LokiHttpClient
    {
        internal TestLokiHttpClient()
        {
        }

        internal TestLokiHttpClient(HttpClient httpClient)
            : base(httpClient)
        {
        }

        public HttpClient Client => HttpClient;

        public string Content { get; private set; } = null!;

        public string RequestUri { get; private set; } = null!;

        public override async Task<HttpResponseMessage> PostAsync(string requestUri, Stream contentStream)
        {
            using var streamReader = new StreamReader(contentStream, Encoding.UTF8WithoutBom);
            Content = await streamReader.ReadToEndAsync();
            RequestUri = requestUri;

            return new HttpResponseMessage();
        }
    }
}