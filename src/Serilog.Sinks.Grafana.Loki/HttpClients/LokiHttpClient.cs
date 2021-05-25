// Copyright 2020-2021 Mykhailo Shevchuk & Contributors
//
// Licensed under the MIT license;
// you may not use this file except in compliance with the License.
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See LICENSE file in the project root for full license information.

using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;

namespace Serilog.Sinks.Grafana.Loki.HttpClients
{
    /// <summary>
    /// Default http client used for sending log events to Grafana Loki.
    /// </summary>
    public class LokiHttpClient : BaseLokiHttpClient
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="LokiHttpClient"/> class.
        /// </summary>
        /// <param name="httpClient">
        /// <see cref="HttpClient"/> be used for HTTP requests.
        /// </param>
        public LokiHttpClient(HttpClient httpClient = null)
            : base(httpClient)
        {
        }

        /// <inheritdoc/>
        public override async Task<HttpResponseMessage> PostAsync(string requestUri, Stream contentStream)
        {
            using var content = new StreamContent(contentStream);
            content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/json");
            return await HttpClient.PostAsync(requestUri, content).ConfigureAwait(false);
        }
    }
}