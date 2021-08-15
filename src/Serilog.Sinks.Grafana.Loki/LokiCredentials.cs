// Copyright 2020-2021 Mykhailo Shevchuk & Contributors
//
// Licensed under the MIT license;
// you may not use this file except in compliance with the License.
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See LICENSE file in the project root for full license information.

namespace Serilog.Sinks.Grafana.Loki
{
    /// <summary>
    /// Credentials used for Grafana Loki authorization
    /// </summary>
    public class LokiCredentials
    {
        /// <summary>
        /// Email or username
        /// </summary>
        public string Login { get; set; } = null!;

        /// <summary>
        /// Password
        /// </summary>
        public string Password { get; set; } = null!;

        internal bool IsEmpty => string.IsNullOrEmpty(Login) || string.IsNullOrEmpty(Password);
    }
}