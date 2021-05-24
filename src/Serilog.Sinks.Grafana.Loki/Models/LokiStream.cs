// Copyright 2020-2021 Mykhailo Shevchuk & Contributors
//
// Licensed under the MIT license;
// you may not use this file except in compliance with the License.
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Serilog.Sinks.Grafana.Loki.Utils;

namespace Serilog.Sinks.Grafana.Loki.Models
{
    internal class LokiStream
    {
        [JsonPropertyName("stream")]
        public Dictionary<string, string> Labels { get; } = new();

        [JsonPropertyName("values")]
        public IList<IList<string>> Entries { get; set; } = new List<IList<string>>();

        public void AddLabel(string key, string value)
        {
            Labels[key] = value;
        }

        public void AddEntry(DateTimeOffset timestamp, string entry)
        {
            Entries.Add(new[] {timestamp.ToUnixNanosecondsString(), entry});
        }
    }
}