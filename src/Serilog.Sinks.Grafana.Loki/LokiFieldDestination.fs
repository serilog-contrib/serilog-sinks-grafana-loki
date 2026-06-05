// Copyright 2020-2026 Mykhailo Shevchuk & Contributors
//
// Licensed under the MIT license;
// you may not use this file except in compliance with the License.
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See LICENSE file in the project root for full license information.

namespace Serilog.Sinks.Grafana.Loki

/// Destination for a synthesized log field (currently TraceId and SpanId).
/// Controls whether the value is omitted, written into the JSON log line body,
/// or attached as Loki structured metadata. Binds from its name (e.g.
/// "StructuredMetadata") when configured via appsettings.json.
type LokiFieldDestination =
    /// Do not emit the field. This is the default.
    | None = 0

    /// Write the field into the JSON log line body (e.g. a "TraceId" property).
    | Body = 1

    /// Attach the field as Loki structured metadata: a per-line, non-indexed
    /// key/value pair queryable without a parser stage and without the cardinality
    /// cost of a label. Requires Loki 3.0+ (or 2.9 with allow_structured_metadata enabled).
    | StructuredMetadata = 2