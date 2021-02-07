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
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace Serilog.Sinks.Grafana.Loki
{
    /// <summary>
    /// Default http client used for sending log events to Grafana Loki.
    /// </summary>
    public class DefaultLokiHttpClient : ILokiHttpClient
    {
        /// <summary>
        /// <see cref="HttpClient"/> used for requests
        /// </summary>
        protected readonly HttpClient HttpClient;

        /// <summary>
        /// Initializes a new instance of the <see cref="DefaultLokiHttpClient"/> class.
        /// </summary>
        /// <param name="httpClient">
        /// <see cref="HttpClient"/> be used for HTTP requests
        /// </param>
        public DefaultLokiHttpClient(HttpClient httpClient = null)
        {
            HttpClient = httpClient ?? new HttpClient();
        }

        /// <summary>
        /// This method is not used by default
        /// Please, use Serilog configuration section for configuring credentials
        /// </summary>
        /// <param name="configuration">
        /// The application configuration properties.
        /// </param>
        public virtual void Configure(IConfiguration configuration)
        {
        }

        /// <summary>
        /// Sends a POST request to the specified Uri as an asynchronous operation.
        /// </summary>
        /// <param name="requestUri">The Uri the request is sent to.</param>
        /// <param name="content">The HTTP request content sent to the server.</param>
        /// <returns>The task object representing the asynchronous operation.</returns>
        public virtual Task<HttpResponseMessage> PostAsync(string requestUri, HttpContent content)
        {
            content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/json");
            return HttpClient.PostAsync(requestUri, content);
        }

        /// <summary>
        /// Adds authorization header to all requests.
        /// </summary>
        /// <param name="credentials">
        /// <see cref="LokiCredentials"/> used for authorization.
        /// </param>
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

        /// <summary>
        /// Dispose method
        /// </summary>
        public virtual void Dispose() => HttpClient.Dispose();

        private static string Base64Encode(string str) => Convert.ToBase64String(Encoding.UTF8.GetBytes(str));
    }
}