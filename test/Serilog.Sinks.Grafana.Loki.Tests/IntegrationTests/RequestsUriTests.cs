using Serilog.Sinks.Grafana.Loki.Tests.TestHelpers;
using Serilog.Sinks.Grafana.Loki.Utils;
using Shouldly;
using Xunit;

namespace Serilog.Sinks.Grafana.Loki.Tests.IntegrationTests
{
    public class RequestsUriTests
    {
        private readonly TestLokiHttpClient _client;

        public RequestsUriTests()
        {
            _client = new TestLokiHttpClient();
        }

        [Theory]
        [InlineData("https://loki:3100")]
        [InlineData("https://loki:3100/")]
        public void RequestUriShouldBeCorrect(string uri)
        {
            var logger = new LoggerConfiguration()
                .WriteTo.GrafanaLoki("https://loki:3100", httpClient: _client)
                .CreateLogger();

            logger.Error("An error occured");
            logger.Dispose();

            _client.RequestUri.ShouldBe(LokiRoutesBuilder.BuildLogsEntriesRoute(uri));
        }
    }
}