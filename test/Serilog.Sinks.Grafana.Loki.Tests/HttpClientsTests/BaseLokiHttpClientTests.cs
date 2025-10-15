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
        // List of test cases with tenant IDs and their expected validity
        var validTenantIds = new List<(string TenantId, bool IsValid)>
    {
        ("tenant123", true), // Only alphanumeric characters
        ("tenant-123", true), // Valid special characters
        ("tenant..123", false), // Double period ".." is not allowed
        (".", false), // Single period is not allowed
        ("tenant!_*.123'()", true), // All allowed special characters
        ("tenant-123...", false), // Multiple periods at the end are not allowed
        ("tenant123456...test", false), // Ends with a period "."
        ("tenant1234567890!@", false), // "@" is not allowed
    };

        foreach (var (tenantId, isValid) in validTenantIds)
        {
            using var client = new TestLokiHttpClient();

            if (isValid)
            {
                client.SetTenant(tenantId);

                var tenantHeaders = client.Client.DefaultRequestHeaders.GetValues("X-Scope-OrgID").ToList();
                tenantHeaders.ShouldBeEquivalentTo(new List<string> { tenantId });
            }
            else
            {
                Should.Throw<ArgumentException>(() => client.SetTenant(tenantId));
            }
        }
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

    [Fact]
    public void SetDefaultHeadersShouldSetHeaderCorrectly()
    {
        // Arrange
        using var httpClient = new HttpClient();
        var client = new TestLokiHttpClient(httpClient);

        var headersToSet = new Dictionary<string, string>
        {
            { "Custom-Header", "HeaderValue" }
        };

        // Act
        client.SetDefaultHeaders(headersToSet);

        // Assert
        httpClient.DefaultRequestHeaders.Contains("Custom-Header").ShouldBeTrue();
        httpClient.DefaultRequestHeaders.GetValues("Custom-Header").ShouldBe(new[] { "HeaderValue" });
    }
}