// Copyright 2020 Mykhailo Shevchuk & Contributors
//
// Licensed under the MIT license;
// you may not use this file except in compliance with the License.
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See LICENSE file in the project root for full license information.

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