using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace Serilog.Sinks.Grafana.Loki
{
    public class DefaultLokiHttpClient : ILokiHttpClient
    {
        protected readonly HttpClient HttpClient;

        public DefaultLokiHttpClient(HttpClient httpClient = null)
        {
            HttpClient = httpClient ?? new HttpClient();
        }

        public virtual Task<HttpResponseMessage> PostAsync(string requestUri, HttpContent content)
        {
            content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/json");
            return HttpClient.PostAsync(requestUri, content);
        }

        public virtual void SetCredentials(LokiCredentials credentials)
        {
            if (credentials == null || credentials.IsEmpty)
            {
                return;
            }

            var headers = HttpClient.DefaultRequestHeaders;

            if (headers.Any(h => h.Key == "Authorization"))
            {
                return;
            }

            var token = Base64Encode($"{credentials.Login}:{credentials.Password}");
            headers.Add("Authorization", token);
        }

        public virtual void Dispose() => HttpClient.Dispose();

        private static string Base64Encode(string str) => Convert.ToBase64String(Encoding.UTF8.GetBytes(str));
    }
}