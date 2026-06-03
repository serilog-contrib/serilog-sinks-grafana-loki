using Serilog.Sinks.Grafana.Loki.Utils;
using Shouldly;
using Xunit;

namespace Serilog.Sinks.Grafana.Loki.Tests.UtilsTests;

public class LokiRoutesBuilderTests
{
    [Theory]
    [InlineData("https://loki", "https://loki/loki/api/v1/push")]
    [InlineData("https://loki/", "https://loki/loki/api/v1/push")]
    [InlineData("https://loki:3100", "https://loki:3100/loki/api/v1/push")]
    [InlineData("https://loki:3100/", "https://loki:3100/loki/api/v1/push")]
    public void CorrectRoutesShouldBeBuilt(string host, string expected)
    {
        var route = LokiRoutesBuilder.BuildLogsEntriesRoute(host);

        route.ShouldBe(expected);
    }
}