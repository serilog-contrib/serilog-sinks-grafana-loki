using Serilog.Sinks.Grafana.Loki.Tests.Fixtures;
using Serilog.Sinks.Grafana.Loki.Tests.TestHelpers;
using Shouldly;
using Xunit;

namespace Serilog.Sinks.Grafana.Loki.Tests.HttpClientTests
{
    public class AuthTests : IClassFixture<HttpClientTextFixture>
    {
        private readonly TestLokiHttpClient _client;

        public AuthTests()
        {
            _client = new TestLokiHttpClient();
        }

        [Fact]
        public void BasicAuthHeaderShouldBeCorrect()
        {
            var credentials = new LokiCredentials {Login = "Billy", Password = "Herrington"};
            var logger = new LoggerConfiguration()
                .WriteTo.GrafanaLoki("http://loki:3100", credentials: credentials, httpClient: _client)
                .CreateLogger();

            logger.Error("An error occured");
            logger.Dispose();

            var authorization = _client.Client.DefaultRequestHeaders.Authorization;
            authorization.ShouldSatisfyAllConditions(
                () => authorization.Scheme.ShouldBe("Basic"),
                () => authorization.Parameter.ShouldBe("QmlsbHk6SGVycmluZ3Rvbg=="));
        }

        [Fact]
        public void NoAuthHeaderShouldBeCorrect()
        {
            var logger = new LoggerConfiguration()
                .WriteTo.GrafanaLoki("http://loki:3100", httpClient: _client)
                .CreateLogger();

            logger.Error("An error occured");
            logger.Dispose();

            _client.Client.DefaultRequestHeaders.Authorization.ShouldBeNull();
        }
    }
}