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

/// Basic authentication credentials for Loki.
/// Leave null on LokiSinkOptions to disable authentication.
// A class with [<AllowNullLiteral>] (not a record) so it can be a nullable optional
// parameter on the GrafanaLoki extension method: F# records are not null-admissible, so
// [<Optional; DefaultParameterValue(null)>] is rejected for them (FS0043). Settings.Configuration
// binds the public settable Login/Password from a JSON object.
[<AllowNullLiteral>]
type LokiCredentials() =
    member val Login: string = null with get, set
    member val Password: string = null with get, set