// Copyright 2020-2021 Mykhailo Shevchuk & Contributors
//
// Licensed under the MIT license;
// you may not use this file except in compliance with the License.
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace Serilog.Sinks.Grafana.Loki.HttpClients
{
    /// <summary>
    /// Default http client used for sending log events to Grafana Loki.
    /// </summary>
    public class LokiHttpClient : ILokiHttpClient
    {
        /// <summary>
        /// <see cref="HttpClient"/> used for requests
        /// </summary>
        protected readonly HttpClient HttpClient;

        /// <summary>
        /// Initializes a new instance of the <see cref="LokiHttpClient"/> class.
        /// </summary>
        /// <param name="httpClient">
        /// <see cref="HttpClient"/> be used for HTTP requests
        /// </param>
        public LokiHttpClient(HttpClient httpClient = null)
        {
            HttpClient = httpClient ?? new HttpClient();
        }

        /// <inheritdoc/>
        public virtual async Task<HttpResponseMessage> PostAsync(string requestUri, Stream contentStream)
        {
            using var content = new StreamContent(contentStream);
            content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/json");
            return await HttpClient.PostAsync(requestUri, content).ConfigureAwait(false);
        }

        /// <inheritdoc/>
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

            var token = Base64Encode($"{credentials.Login}:{credentials.Password ?? string.Empty}");
            headers.Authorization = new AuthenticationHeaderValue("Basic", token);
        }

        /// <inheritdoc/>
        public virtual void Dispose() => HttpClient.Dispose();

        private static string Base64Encode(string str) => Convert.ToBase64String(Encoding.UTF8.GetBytes(str));
    }
}