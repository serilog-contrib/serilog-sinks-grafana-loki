// Copyright 2020-2022 Mykhailo Shevchuk & Contributors
//
// Licensed under the MIT license;
// you may not use this file except in compliance with the License.
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See LICENSE file in the project root for full license information.

using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Serilog.Sinks.Grafana.Loki.Utils;

[SuppressMessage("ReSharper", "InconsistentNaming", Justification = "By design.")]
internal static class Encoding
{
    public static readonly System.Text.Encoding UTF8WithoutBom = new UTF8Encoding(false);
}