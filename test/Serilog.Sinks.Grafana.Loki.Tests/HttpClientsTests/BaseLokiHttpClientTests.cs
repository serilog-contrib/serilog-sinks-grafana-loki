using Serilog.Sinks.Grafana.Loki.Tests.TestHelpers;
using Shouldly;
using Xunit;

namespace Serilog.Sinks.Grafana.Loki.Tests.HttpClientsTests;

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

    [Fact]
    public void TenantHeaderShouldBeCorrect()
    {
        var tenantId = "lokitenant";
        using var client = new TestLokiHttpClient();

        client.SetTenant(tenantId);

        var tenantHeaders = client.Client.DefaultRequestHeaders.GetValues("X-Scope-OrgID").ToList();
        tenantHeaders.ShouldBeEquivalentTo(new List<string> {"lokitenant"});
    }

    [Fact]
    public void TenantHeaderShouldNotBeSetWithoutTenantId()
    {
        using var client = new TestLokiHttpClient();

        client.SetTenant(null);

        client.Client.DefaultRequestHeaders.Contains("X-Scope-OrgID").ShouldBeFalse();
    }

    [Fact]
    public void TenantHeaderShouldThrowAnExceptionOnTenantIdAgainstRule()
    {
        var tenantId = "non-alphanumerical tenant";
        using var client = new TestLokiHttpClient();

        Should.Throw<ArgumentException>(() => client.SetTenant(tenantId));
    }
}