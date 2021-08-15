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
using System.Net.Http;
using System.Threading.Tasks;
using Serilog.Sinks.Grafana.Loki.HttpClients;

namespace Serilog.Sinks.Grafana.Loki
{
    /// <summary>
    /// Interface responsible for posting HTTP events
    /// and handling authorization for Grafana Loki.
    /// </summary>
    /// <seealso cref="LokiHttpClient"/>
    public interface ILokiHttpClient : IDisposable
    {
        /// <summary>
        /// Sends a POST request to the specified Uri as an asynchronous operation.
        /// </summary>
        /// <param name="requestUri">The Uri the request is sent to.</param>
        /// <param name="contentStream">The stream containing the content of the request.</param>
        /// <returns>The task object representing the asynchronous operation.</returns>
        Task<HttpResponseMessage> PostAsync(string requestUri, Stream contentStream);

        /// <summary>
        /// Adds authorization header to all requests.
        /// </summary>
        /// <param name="credentials">
        ///     <see cref="LokiCredentials"/> used for authorization.
        /// </param>
        void SetCredentials(LokiCredentials? credentials);
    }
}