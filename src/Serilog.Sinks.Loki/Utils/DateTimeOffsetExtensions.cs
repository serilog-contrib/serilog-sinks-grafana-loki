using System;

namespace Serilog.Sinks.Loki.Utils
{
    internal static class DateTimeOffsetExtensions
    {
        internal static string ToUnixNanosecondsString(this DateTimeOffset offset) =>
            (offset.ToUnixTimeMilliseconds() * 1000000).ToString();
    }
}