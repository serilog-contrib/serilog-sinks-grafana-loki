using System.Text;
using Serilog.Debugging;
using Serilog.Events;
using Serilog.Formatting;
using Serilog.Sinks.Grafana.Loki.Models;
using Serilog.Sinks.Grafana.Loki.Utils;

namespace Serilog.Sinks.Grafana.Loki;

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
internal class LokiBatchFormatter : ILokiBatchFormatter
{
    private const int DefaultWriteBufferCapacity = 256;

    private readonly IEnumerable<LokiLabel> _globalLabels;
    private readonly IReservedPropertyRenamingStrategy _renamingStrategy;
    private readonly IEnumerable<string> _propertiesAsLabels;

    private readonly bool _leavePropertiesIntact;
    private readonly bool _useInternalTimestamp;

    /// <summary>
    /// Initializes a new instance of the <see cref="LokiBatchFormatter"/> class.
    /// </summary>
    /// <param name="renamingStrategy">
    /// Renaming strategy for properties' names equal to reserved keywords.
    /// <see cref="IReservedPropertyRenamingStrategy"/>
    /// </param>
    /// <param name="globalLabels">
    /// The list of global <see cref="LokiLabel"/>.
    /// </param>
    /// <param name="propertiesAsLabels">
    /// The list of properties, which would be mapped to the labels.
    /// </param>
    /// <param name="useInternalTimestamp">
    /// Compute internal timestamp
    /// </param>
    /// <param name="leavePropertiesIntact">
    /// Leave the list of properties intact after extracting the labels specified in propertiesAsLabels.
    /// </param>
    public LokiBatchFormatter(
        IReservedPropertyRenamingStrategy renamingStrategy,
        IEnumerable<LokiLabel>? globalLabels = null,
        IEnumerable<string>? propertiesAsLabels = null,
        bool useInternalTimestamp = false,
        bool leavePropertiesIntact = false)
    {
        _renamingStrategy = renamingStrategy;
        _globalLabels = globalLabels ?? Enumerable.Empty<LokiLabel>();
        _propertiesAsLabels = propertiesAsLabels ?? Enumerable.Empty<string>();
        _useInternalTimestamp = useInternalTimestamp;
        _leavePropertiesIntact = leavePropertiesIntact;
    }

    /// <summary>
    /// Format the log events into a payload.
    /// </summary>
    /// <param name="lokiLogEvents">
    /// The events to format wrapped in <see cref="LokiLogEvent"/>.
    /// </param>
    /// <param name="formatter">
    /// The formatter turning the log events into a textual representation.
    /// </param>
    /// <param name="output">
    /// The payload to send over the network.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown if one of params is null.
    /// </exception>
    public void Format(
        IReadOnlyCollection<LokiLogEvent> lokiLogEvents,
        ITextFormatter formatter,
        TextWriter output)
    {
        if (lokiLogEvents == null)
        {
            throw new ArgumentNullException(nameof(lokiLogEvents));
        }

        if (formatter == null)
        {
            throw new ArgumentNullException(nameof(formatter));
        }

        if (output == null)
        {
            throw new ArgumentNullException(nameof(output));
        }

        if (lokiLogEvents.Count == 0)
        {
            return;
        }

        var batch = new LokiBatch();

        // Group logEvent by labels
        var groups = lokiLogEvents
            .Select(AddLevelAsPropertySafely)
            .Select(GenerateLabels)
            .GroupBy(
                le => le.Labels,
                le => le.LokiLogEvent,
                DictionaryComparer<string, string>.Instance);

        foreach (var group in groups)
        {
            var labels = group.Key;
            var stream = batch.CreateStream();

            foreach (var (key, value) in labels)
            {
                stream.AddLabel(key, value);
            }

            foreach (var logEvent in group.OrderBy(x => _useInternalTimestamp ? x.InternalTimestamp : x.LogEvent.Timestamp))
            {
                GenerateEntry(
                    logEvent,
                    formatter,
                    stream);
            }
        }

        if (batch.IsNotEmpty)
        {
            output.Write(batch.Serialize());
        }

        // Current behavior breaks rendering
        // Log.Info("Hero's {level}", 42)
        // Message: "Hero's \"info\""
        // level: "info"
        // _level: 42
        LokiLogEvent AddLevelAsPropertySafely(LokiLogEvent lokiLogEvent)
        {
            var logEvent = lokiLogEvent.LogEvent;
            logEvent.RenamePropertyIfPresent("level", _renamingStrategy.Rename);
            logEvent.AddOrUpdateProperty(
                new LogEventProperty("level", new ScalarValue(logEvent.Level.ToGrafanaLogLevel())));

            return lokiLogEvent;
        }
    }

    private void GenerateEntry(
        LokiLogEvent lokiLogEvent,
        ITextFormatter formatter,
        LokiStream stream)
    {
        var buffer = new StringWriter(new StringBuilder(DefaultWriteBufferCapacity));

        var logEvent = lokiLogEvent.LogEvent;
        var timestamp = logEvent.Timestamp;
        var traceId = logEvent.TraceId;
        var spanId = logEvent.SpanId;

        if (_useInternalTimestamp)
        {
            logEvent.AddPropertyIfAbsent(
                new LogEventProperty("Timestamp", new ScalarValue(timestamp)));
            timestamp = lokiLogEvent.InternalTimestamp;
        }

        if (traceId.HasValue)
        {
            logEvent.AddPropertyIfAbsent(
                new LogEventProperty("TraceId", new ScalarValue(traceId)));
        }

        if (spanId.HasValue)
        {
            logEvent.AddPropertyIfAbsent(
                new LogEventProperty("SpanId", new ScalarValue(spanId)));
        }

        formatter.Format(logEvent, buffer);

        stream.AddEntry(timestamp, buffer.ToString().TrimEnd('\r', '\n'));
    }

    private (Dictionary<string, string> Labels, LokiLogEvent LokiLogEvent) GenerateLabels(LokiLogEvent lokiLogEvent)
    {
        var labels = _globalLabels.ToDictionary(label => label.Key, label => label.Value);
        var properties = lokiLogEvent.Properties;
        var (propertiesAsLabels, remainingProperties) =
            properties.Partition(kvp => _propertiesAsLabels.Contains(kvp.Key));

        foreach (var property in propertiesAsLabels)
        {
            var key = property.Key;

            // If a message template is a composite format string that contains indexed placeholders ({0}, {1} etc),
            // Serilog turns these placeholders into event properties keyed by numeric strings.
            // Loki doesn't accept such strings as label keys. Prefix these numeric strings with "param"
            // to turn them into valid label keys and at the same time denote them as ordinal parameters.
            if (char.IsDigit(key[0]))
            {
                key = $"param{key}";
            }

            // Some enrichers generates extra quotes and it breaks the payload
            var value = property.Value.ToString().Replace("\"", string.Empty);

            if (labels.ContainsKey(key))
            {
                SelfLog.WriteLine(
                    "Labels already contains key {0}, added from global labels. Property value ({1}) with the same key is ignored",
                    key,
                    value);

                continue;
            }

            labels.Add(key, value);
        }

        return (labels,
            lokiLogEvent.CopyWithProperties(_leavePropertiesIntact ? properties : remainingProperties));
    }
}