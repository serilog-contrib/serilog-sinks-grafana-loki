using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Serilog.Sinks.Grafana.Loki.HttpClients;

namespace Serilog.Sinks.Grafana.Loki.Tests.TestHelpers
{
    internal class TestLokiHttpClient : LokiHttpClient
    {
        public HttpClient Client => HttpClient;

        public string Content { get; private set; }

        public string RequestUri { get; private set; }

        public override async Task<HttpResponseMessage> PostAsync(string requestUri, Stream contentStream)
        {
            using var sr = new StreamReader(contentStream, Encoding.UTF8);
            Content = await sr.ReadToEndAsync();
            RequestUri = requestUri;

            return new HttpResponseMessage();
        }
    }
}