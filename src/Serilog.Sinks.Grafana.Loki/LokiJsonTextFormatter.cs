using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text;
using Serilog.Events;
using Serilog.Formatting;
using Serilog.Formatting.Json;
using Serilog.Parsing;
using Serilog.Sinks.Grafana.Loki.Utils;

namespace Serilog.Sinks.Grafana.Loki
{
    /// <summary>
    /// Used to serialize a log event to a json format that loki 2.0 can parse using the json parser ( | json ), more information can be found here https://grafana.com/blog/2020/10/28/loki-2.0-released-transform-logs-as-youre-querying-them-and-set-up-alerts-within-loki/
    /// </summary>
    [SuppressMessage("ReSharper", "PossibleMultipleEnumeration", Justification = "Reviewed")]
    public class LokiJsonTextFormatter : ITextFormatter, ILabelAwareTextFormatter
    {
        private readonly JsonValueFormatter _valueFormatter;

        /// <summary>
        /// Initializes a new instance of the <see cref="LokiJsonTextFormatter"/> class.
        /// </summary>
        public LokiJsonTextFormatter()
        {
            _valueFormatter = new JsonValueFormatter(typeTagName: "$type");
        }

        /// <summary>
        /// Format the log event into the output.
        /// </summary>
        /// <param name="logEvent">The event to format.</param>
        /// <param name="output">The output.</param>
        /// <param name="labels">List of labels that should not be written as json fields</param>
        public void Format(LogEvent logEvent, TextWriter output, IEnumerable<string> labels)
        {
            if (logEvent == null)
            {
                throw new ArgumentNullException(nameof(logEvent));
            }

            if (output == null)
            {
                throw new ArgumentNullException(nameof(output));
            }

            output.Write("{\"Message\":");
            JsonValueFormatter.WriteQuotedJsonString(logEvent.MessageTemplate.Render(logEvent.Properties), output);

            output.Write(",\"MessageTemplate\":");
            JsonValueFormatter.WriteQuotedJsonString(logEvent.MessageTemplate.Text, output);

            var tokensWithFormat = logEvent.MessageTemplate.Tokens
                .OfType<PropertyToken>()
                .Where(pt => pt.Format != null);

            // Better not to allocate an array in the 99.9% of cases where this is false
            if (tokensWithFormat.Any())
            {
                output.Write(",\"Renderings\":[");
                var delim = string.Empty;
                foreach (var r in tokensWithFormat)
                {
                    output.Write(delim);
                    delim = ",";
                    var space = new StringWriter();
                    r.Render(logEvent.Properties, space);
                    JsonValueFormatter.WriteQuotedJsonString(space.ToString(), output);
                }

                output.Write(']');
            }

            output.Write(",\"level\":\"");
            output.Write(logEvent.Level.ToGrafanaLogLevel());
            output.Write('\"');

            if (logEvent.Exception != null)
            {
                output.Write(",\"Exception\":");
                JsonValueFormatter.WriteQuotedJsonString(logEvent.Exception.ToString(), output);
            }

            foreach (var property in logEvent.Properties)
            {
                var name = property.Key;
                if (labels.Contains(name))
                {
                    continue;
                }

                output.Write(',');
                JsonValueFormatter.WriteQuotedJsonString(name, output);
                output.Write(':');
                _valueFormatter.Format(property.Value, output);
            }

            output.Write('}');
        }

        /// <inheritdoc/>
        [Obsolete("Use \"Format(LogEvent logEvent, TextWriter output, IEnumerable<string> labels)\" instead!")]
        public void Format(LogEvent logEvent, TextWriter output) => Format(logEvent, output, Enumerable.Empty<string>());
    }
}
