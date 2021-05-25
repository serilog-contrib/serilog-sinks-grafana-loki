using System.Net.Http;
using Serilog.Sinks.Grafana.Loki.Tests.TestHelpers;
using Shouldly;
using Xunit;

namespace Serilog.Sinks.Grafana.Loki.Tests.HttpClientsTests
{
    public class BaseLokiHttpClientTests
    {
        [Fact]
        public void ProvidedHttpClientShouldBeUsed()
        {
            using var httpClient = new HttpClient();

            using var client = new TestLokiHttpClient(httpClient);

            client.Client.ShouldBe(httpClient);
        }

        [Fact]
        public void HttpClientShouldBeCreatedIfNotProvider()
        {
            using var client = new TestLokiHttpClient();

            client.Client.ShouldNotBeNull();
        }

        [Fact]
        public void BasicAuthHeaderShouldBeCorrect()
        {
            var credentials = new LokiCredentials {Login = "Billy", Password = "Herrington"};
            using var client = new TestLokiHttpClient();

            client.SetCredentials(credentials);

            var authorization = client.Client.DefaultRequestHeaders.Authorization;
            authorization.ShouldSatisfyAllConditions(
                () => authorization!.Scheme.ShouldBe("Basic"),
                () => authorization!.Parameter.ShouldBe("QmlsbHk6SGVycmluZ3Rvbg=="));
        }

        [Fact]
        public void AuthorizationHeaderShouldNotBeSetWithoutCredentials()
        {
            using var client = new TestLokiHttpClient();

            client.SetCredentials(null);

            client.Client.DefaultRequestHeaders.Authorization.ShouldBeNull();
        }
    }
}