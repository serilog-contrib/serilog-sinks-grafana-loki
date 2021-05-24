using System.Collections.Generic;
using System.IO;
using Serilog.Events;
using Serilog.Formatting;

namespace Serilog.Sinks.Grafana.Loki
{
    /// <summary>
    /// Formats batches of log events into payloads that can be sent over the network.
    /// </summary>
    public interface ILokiBatchFormatter
    {
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
        void Format(IReadOnlyCollection<LogEvent> logEvents, ITextFormatter formatter, TextWriter output);
    }
}