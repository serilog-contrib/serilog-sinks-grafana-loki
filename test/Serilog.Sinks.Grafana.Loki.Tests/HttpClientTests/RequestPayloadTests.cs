using System.Text.RegularExpressions;
using Serilog.Sinks.Grafana.Loki.Models;
using Serilog.Sinks.Grafana.Loki.Tests.Fixtures;
using Serilog.Sinks.Grafana.Loki.Tests.Infrastructure;
using Shouldly;
using Xunit;

namespace Serilog.Sinks.Grafana.Loki.Tests.HttpClientTests
{
    public class RequestPayloadTests : IClassFixture<HttpClientTextFixture>
    {
        private readonly TestLokiHttpClient _client;

        public RequestPayloadTests()
        {
            _client = new TestLokiHttpClient();
        }

        [Fact]
        public void RequestContentShouldMatchApproved()
        {
            var logger = new LoggerConfiguration()
                .WriteTo.GrafanaLoki("http://loki:3100", httpClient: _client)
                .CreateLogger();

            logger.Error("An error occured");
            logger.Dispose();

            _client.Content.ShouldMatchApproved(c =>
                c.WithScrubber(s => Regex.Replace(s, "\"[0-9]{19}\"", "\"<unixepochinnanoseconds>\"")));
        }

        [Fact]
        public void IncludedOnlyLabelsShouldBePresentInRequest()
        {
            var logger = new LoggerConfiguration()
                .Enrich.WithProperty("server_name", "loki_test")
                .Enrich.WithProperty("server_ip", "127.0.0.1")
                .WriteTo.GrafanaLoki("http://loki:3100", includeOnlyLabels: new[] {"server_ip"}, httpClient: _client)
                .CreateLogger();

            logger.Error("An error occured");
            logger.Dispose();

            _client.Content.ShouldNotContain("server_name");
        }
    }
}