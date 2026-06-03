using Serilog.Events;
using Serilog.Sinks.Grafana.Loki.Utils;
using Shouldly;
using Xunit;

namespace Serilog.Sinks.Grafana.Loki.Tests.UtilsTests;

public class LogEventLevelExtensionsTests
{
    [Theory]
    [InlineData(LogEventLevel.Verbose, "trace")]
    [InlineData(LogEventLevel.Debug, "debug")]
    [InlineData(LogEventLevel.Information, "info")]
    [InlineData(LogEventLevel.Warning, "warning")]
    [InlineData(LogEventLevel.Error, "error")]
    [InlineData(LogEventLevel.Fatal, "critical")]
    public void LogEventLevelShouldMapToCorrectGrafanaLevel(LogEventLevel logEventLevel, string expected)
    {
        var grafanaLogLevel = logEventLevel.ToGrafanaLogLevel();

        grafanaLogLevel.ShouldBe(expected);
    }
}