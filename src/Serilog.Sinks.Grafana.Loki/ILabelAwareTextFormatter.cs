using System.Collections.Generic;
using System.IO;
using Serilog.Events;

namespace Serilog.Sinks.Grafana.Loki
{
    /// <summary>
    /// Interface that has a Format method that accepts labels as input
    /// </summary>
    public interface ILabelAwareTextFormatter
    {
        /// <summary>
        /// USed to exclude the Level label.
        /// </summary>
        public bool ExcludeLevelLabel { get; }
        /// <summary>
        /// Format the log event into the output.
        /// </summary>
        /// <param name="logEvent">The event to format.</param>
        /// <param name="output">The output.</param>
        /// <param name="labels">List of labels that are attached to this stream</param>
        public void Format(LogEvent logEvent, TextWriter output, IEnumerable<string> labels);
    }
}
