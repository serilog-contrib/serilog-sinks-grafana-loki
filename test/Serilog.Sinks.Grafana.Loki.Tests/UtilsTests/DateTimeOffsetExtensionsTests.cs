using System;
using Serilog.Sinks.Grafana.Loki.Utils;
using Shouldly;
using Xunit;

namespace Serilog.Sinks.Grafana.Loki.Tests.UtilsTests
{
    public class DateTimeOffsetExtensionsTests
    {
        [Fact]
        public void UnixEpochShouldBeConvertedCorrectly()
        {
            var epoch = DateTimeOffset.UnixEpoch;

            var result = epoch.ToUnixNanosecondsString();

            result.ShouldBe("0");
        }

        [Fact]
        public void DateTimeOffsetShouldBeConvertedCorrectly()
        {
            var dateTimeOffset = new DateTimeOffset(2021, 05, 25, 12, 00, 00, TimeSpan.Zero);

            var result = dateTimeOffset.ToUnixNanosecondsString();

            result.ShouldBe("1621944000000000000");
        }
    }
}