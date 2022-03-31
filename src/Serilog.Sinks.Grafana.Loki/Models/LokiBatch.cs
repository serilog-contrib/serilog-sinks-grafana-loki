// Copyright 2020-2022 Mykhailo Shevchuk & Contributors
//
// Licensed under the MIT license;
// you may not use this file except in compliance with the License.
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Serilog.Sinks.Grafana.Loki.Models;

internal class LokiBatch
{
    [JsonPropertyName("streams")]
    public IList<LokiStream> Streams { get; } = new List<LokiStream>();

    [JsonIgnore]
    public bool IsNotEmpty => Streams.Count > 0;

    public LokiStream CreateStream()
    {
        var stream = new LokiStream();
        Streams.Add(stream);
        return stream;
    }

    public string Serialize() => JsonSerializer.Serialize(this);
}