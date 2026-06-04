// Copyright 2020-2026 Mykhailo Shevchuk & Contributors
//
// Licensed under the MIT license;
// you may not use this file except in compliance with the License.
//
namespace Serilog.Sinks.Grafana.Loki

/// A label applied to every log stream emitted by the sink.
[<CLIMutable>]
type LokiLabel = { Key: string; Value: string }
