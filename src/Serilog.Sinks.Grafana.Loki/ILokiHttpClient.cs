// Copyright 2020 Mykhailo Shevchuk & Contributors
//
// Licensed under the MIT license;
// you may not use this file except in compliance with the License.
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See LICENSE file in the project root for full license information.

using Serilog.Sinks.Http;

namespace Serilog.Sinks.Grafana.Loki
{
    /// <summary>
    /// Interface responsible for posting HTTP events
    /// and handling authorization for Grafana Loki.
    /// Extends <see cref="IHttpClient"/>
    /// </summary>
    public interface ILokiHttpClient : IHttpClient
    {
        /// <summary>
        /// Adds authorization header to all requests.
        /// </summary>
        /// <param name="credentials">
        /// <see cref="LokiCredentials"/> used for authorization.
        /// </param>
        void SetCredentials(LokiCredentials credentials);
    }
}