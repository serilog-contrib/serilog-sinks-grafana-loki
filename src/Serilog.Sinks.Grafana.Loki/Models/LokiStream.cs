using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Serilog.Sinks.Grafana.Loki.Utils;

namespace Serilog.Sinks.Grafana.Loki.Models
{
    internal class LokiStream
    {
        [JsonPropertyName("stream")]
        public Dictionary<string, string> Labels { get; } = new Dictionary<string, string>();

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