// Copyright 2020 Mykhailo Shevchuk & Contributors
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
using System.IO;
using System.Linq;
using System.Text;
using Serilog.Events;
using Serilog.Formatting;
using Serilog.Sinks.Grafana.Loki.Models;
using Serilog.Sinks.Grafana.Loki.Utils;
using Serilog.Sinks.Http;

namespace Serilog.Sinks.Grafana.Loki
{
    /// <summary>
    /// Formatter serializing batches of log events into a JSON object in the format, recognized by Grafana Loki.
    /// <para/>
    /// Example:
    /// <code>
    /// {
    ///     "streams": [
    ///     {
    ///         "stream": {
    ///             "label": "value"
    ///             },
    ///         "values": [
    ///             [ "unix epoch in nanoseconds", "log line" ],
    ///             [ "unix epoch in nanoseconds", "log line" ]
    ///         ]
    ///     }
    ///     ]
    /// }
    /// </code>
    /// </summary>
    internal class LokiBatchFormatter : IBatchFormatter
    {
        private readonly IEnumerable<LokiLabel> _globalLabels;
        private readonly LokiLabelFiltrationMode? _filtrationMode;
        private readonly IEnumerable<string> _filtrationLabels;

        /// <summary>
        /// Initializes a new instance of the <see cref="LokiBatchFormatter"/> class.
        /// </summary>
        /// <param name="globalLabels">
        /// The list of global <see cref="LokiLabel"/>.
        /// </param>
        /// <param name="filtrationMode">
        /// The mode for labels filtration
        /// </param>
        /// <param name="filtrationLabels">
        /// The list of label keys used for filtration
        /// </param>
        public LokiBatchFormatter(
            IEnumerable<LokiLabel> globalLabels = null,
            LokiLabelFiltrationMode? filtrationMode = null,
            IEnumerable<string> filtrationLabels = null)
        {
            _globalLabels = globalLabels ?? Enumerable.Empty<LokiLabel>();
            _filtrationMode = filtrationMode;
            _filtrationLabels = filtrationLabels;
        }

        /// <summary>
        /// Format the log events into a payload.
        /// </summary>
        /// <param name="logEvents">
        /// The events to format.
        /// </param>
        /// <param name="formatter">
        /// The formatter turning the log events into a textual representation.
        /// </param>
        /// <param name="output">
        /// The payload to send over the network.
        /// </param>
        /// <exception cref="ArgumentNullException"></exception>
        public void Format(IEnumerable<LogEvent> logEvents, ITextFormatter formatter, TextWriter output)
        {
            if (logEvents == null)
            {
                throw new ArgumentNullException(nameof(logEvents));
            }

            if (output == null)
            {
                throw new ArgumentNullException(nameof(output));
            }

            var events = logEvents as LogEvent[] ?? logEvents.ToArray();

            if (events.Length == 0)
            {
                return;
            }

            var batch = new LokiBatch();

            foreach (var logEvent in events)
            {
                var stream = batch.CreateStream();
                GenerateLabels(logEvent, stream);
                GenerateEntry(logEvent, stream);
            }

            if (batch.IsNotEmpty)
            {
                output.Write(batch.Serialize());
            }
        }

        /// <summary>
        /// Format the log events into a payload.
        /// </summary>
        /// <param name="logEvents">
        /// The events to format.
        /// </param>
        /// <param name="output">
        /// The payload to send over the network.
        /// </param>
        /// <exception cref="ArgumentNullException"></exception>
        public void Format(IEnumerable<string> logEvents, TextWriter output)
        {
            if (logEvents == null)
            {
                throw new ArgumentNullException(nameof(logEvents));
            }

            if (output == null)
            {
                throw new ArgumentNullException(nameof(output));
            }

            var events = logEvents as string[] ?? logEvents.ToArray();

            if (events.Length == 0)
            {
                return;
            }

            var batch = new LokiBatch();

            foreach (var logEvent in events)
            {
                var stream = batch.CreateStream();

                foreach (var label in _globalLabels)
                {
                    if (IsAllowedByFilter(label.Key))
                    {
                        stream.AddLabel(label.Key, label.Value);
                    }
                }

                stream.AddEntry(DateTimeOffset.UtcNow, logEvent.TrimEnd('\r', '\n'));
            }

            if (batch.IsNotEmpty)
            {
                output.Write(batch.Serialize());
            }
        }

        private static void GenerateEntry(LogEvent logEvent, LokiStream stream)
        {
            var sb = new StringBuilder();
            sb.AppendLine(logEvent.RenderMessage());

            if (logEvent.Exception != null)
            {
                var ex = logEvent.Exception;
                while (ex != null)
                {
                    sb.AppendLine(ex.Message);
                    sb.AppendLine(ex.StackTrace);
                    ex = ex.InnerException;
                }
            }

            stream.AddEntry(logEvent.Timestamp, sb.ToString().TrimEnd('\r', '\n'));
        }

        private void GenerateLabels(LogEvent logEvent, LokiStream stream)
        {
            stream.AddLabel("level", logEvent.Level.ToGrafanaLogLevel());

            foreach (var label in _globalLabels)
            {
                if (IsAllowedByFilter(label.Key))
                {
                    stream.AddLabel(label.Key, label.Value);
                }
            }

            foreach (var property in logEvent.Properties)
            {
                if (IsAllowedByFilter(property.Key))
                {
                    // Some enrichers generates extra quotes and it breaks the payload
                    stream.AddLabel(property.Key, property.Value.ToString().Replace("\"", string.Empty));
                }
            }
        }

        private bool IsAllowedByFilter(string label) =>
            _filtrationMode switch
            {
                LokiLabelFiltrationMode.Include => IsInFilterList(label),
                LokiLabelFiltrationMode.Exclude => !IsInFilterList(label),
                null => true,
                _ => true
            };

        private bool IsInFilterList(string label) => _filtrationLabels != null && _filtrationLabels.Contains(label);
    }
}