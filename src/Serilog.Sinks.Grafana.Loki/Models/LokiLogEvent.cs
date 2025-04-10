// Copyright 2020-2022 Mykhailo Shevchuk & Contributors
//
// Licensed under the MIT license;
// you may not use this file except in compliance with the License.
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See LICENSE file in the project root for full license information.

using Serilog.Events;

namespace Serilog.Sinks.Grafana.Loki.Models;

/// <summary>
/// A wrapped log event.
/// Contains <see cref="LogEvent"/> and sink's internal timestamp.
/// <see cref="InternalTimestamp"/> is created when event is emitted to the sink.
/// </summary>
public class LokiLogEvent
{
    /// <summary>
    /// Creates <see cref="LokiLogEvent"/> from <see cref="LogEvent"/>.
    /// </summary>
    /// <param name="logEvent">
    /// A log event.
    /// </param>
    public LokiLogEvent(LogEvent logEvent)
    {
        InternalTimestamp = DateTimeOffset.Now;
        LogEvent = logEvent;
    }

    /// <summary>
    /// Internal event timestamp, created when event is emitted to the sink.
    /// </summary>
    public DateTimeOffset InternalTimestamp { get; }

    /// <summary>
    /// A log event.
    /// </summary>
    public LogEvent LogEvent { get; private set; }

    /// <summary>
    /// Properties associated with the event.
    /// </summary>
    public IReadOnlyDictionary<string, LogEventPropertyValue> Properties => LogEvent.Properties;

    internal LokiLogEvent CopyWithProperties(IEnumerable<KeyValuePair<string, LogEventPropertyValue>> properties)
    {
        LogEvent = new LogEvent(
            LogEvent.Timestamp,
            LogEvent.Level,
            LogEvent.Exception,
            LogEvent.MessageTemplate,
            properties.Select(p => new LogEventProperty(p.Key, p.Value)),
            LogEvent.TraceId ?? default,
            LogEvent.SpanId ?? default);

        return this;
    }
}