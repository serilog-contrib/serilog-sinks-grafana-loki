using Serilog.Sinks.Grafana.Loki.Utils;
using Shouldly;
using Xunit;

namespace Serilog.Sinks.Grafana.Loki.Tests.UtilsTests;

public class DateTimeOffsetExtensionsTests
{
    #if NET7_0_OR_GREATER
    [Fact]
    public void UnixEpochShouldBeConvertedCorrectly()
    {
        var epoch = DateTimeOffset.UnixEpoch;

        var result = epoch.ToUnixNanosecondsString();

        result.ShouldBe("0");
    }

    [Fact]
    public void DateTimeNanosecondsOffsetShouldBeConvertedCorrectly()
    {
        var dateTimeOffset = new DateTimeOffset(2021, 05, 25, 12, 00, 00, 777, 888, TimeSpan.Zero).AddMicroseconds(0.999); // There is no other way to set nanoseconds

        var result = dateTimeOffset.ToUnixNanosecondsString();

        result.ShouldBe("1621944000777888900");
    }

    #else
    [Fact]
    public void UnixEpochShouldBeConvertedCorrectly()
    {
        var epoch = new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero);

        var result = epoch.ToUnixNanosecondsString();

        result.ShouldBe("0");
    }

    [Fact]
    public void DateTimeNanosecondsOffsetShouldBeConvertedCorrectly()
    {
        const long nanosecondsPerTick = 100;

        var ticks = new DateTimeOffset(2021, 05, 25, 12, 00, 00, TimeSpan.Zero).Ticks;
        ticks += 777888999 / nanosecondsPerTick;

        var dateTimeOffset = new DateTimeOffset(ticks, TimeSpan.Zero);

        var result = dateTimeOffset.ToUnixNanosecondsString();

        result.ShouldBe("1621944000777888900");
    }
    #endif
}