// Copyright 2020-2022 Mykhailo Shevchuk & Contributors
//
// Licensed under the MIT license;
// you may not use this file except in compliance with the License.
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See LICENSE file in the project root for full license information.

using Serilog.Events;

namespace Serilog.Sinks.Grafana.Loki.Utils;

internal static class LogEventLevelExtensions
{
    // TODO: After the release 7.0.0 Grafana will determine log level fatal, so mapping for that level will be redundant
    internal static string ToGrafanaLogLevel(this LogEventLevel level) =>
        level switch
        {
            LogEventLevel.Verbose => "trace",
            LogEventLevel.Debug => "debug",
            LogEventLevel.Information => "info",
            LogEventLevel.Warning => "warning",
            LogEventLevel.Error => "error",
            LogEventLevel.Fatal => "critical",
            _ => "unknown"
        };
}