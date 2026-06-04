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

open System.Text.Json

/// Serializes exceptions into the Loki log entry JSON body.
/// Replace the default on LokiSinkOptions to scrub PII, change structure, or suppress detail.
[<AllowNullLiteral>]
type ILokiExceptionFormatter =
    abstract Format: writer: Utf8JsonWriter * ex: exn -> unit
