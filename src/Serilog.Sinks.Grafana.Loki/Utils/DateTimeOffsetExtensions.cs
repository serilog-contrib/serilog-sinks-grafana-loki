using System;

namespace Serilog.Sinks.Grafana.Loki.Utils
{
    internal static class DateTimeOffsetExtensions
    {
        internal static string ToUnixNanosecondsString(this DateTimeOffset offset) =>
            (offset.ToUnixTimeMilliseconds() * 1000000).ToString();
    }
}