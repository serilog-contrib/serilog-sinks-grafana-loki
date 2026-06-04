// Copyright 2020-2026 Mykhailo Shevchuk & Contributors
//
// Licensed under the MIT license;
// you may not use this file except in compliance with the License.
//
namespace Serilog.Sinks.Grafana.Loki

/// Basic authentication credentials for Loki.
/// Leave null on LokiSinkOptions to disable authentication.
[<CLIMutable>]
type LokiCredentials = { Login: string; Password: string }
