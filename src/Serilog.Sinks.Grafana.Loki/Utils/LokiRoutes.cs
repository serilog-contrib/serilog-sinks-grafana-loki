// Copyright 2020 Mykhailo Shevchuk & Contributors
//
// Licensed under the MIT license;
// you may not use this file except in compliance with the License.
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See LICENSE file in the project root for full license information.

namespace Serilog.Sinks.Grafana.Loki.Utils
{
    internal static class LokiRoutes
    {
        private const string LogEntriesEndpoint = "/loki/api/v1/push";

        public static string BuildLogsEntriesRoute(string host) => $"{host.TrimEnd('/')}{LogEntriesEndpoint}";
    }
}