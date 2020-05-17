using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Serilog.Sinks.Grafana.Loki.Models
{
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
}