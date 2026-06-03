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
        var credentials = new LokiCredentials { Login = "Billy", Password = "Herrington" };
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

    [Theory]
    [InlineData("tenant123", true)] // only alphanumeric
    [InlineData("tenant-123", true)] // allowed hyphen
    [InlineData("tenant..123", false)] // double period not allowed
    [InlineData(".", false)] // single period not allowed
    [InlineData("tenant!_*.123'()", true)] // allowed special characters
    [InlineData("tenant-123...", false)] // ends with multiple periods
    [InlineData("tenant123456...test", false)] // ends with period
    [InlineData("tenant1234567890!@", false)] // '@' is not allowed
    [InlineData("a", true)] // minimal length
    [InlineData("tenant_with_underscores", true)] // underscores
    [InlineData("tenant..", false)] // ends with double period
    [InlineData("..tenant", false)] // starts with double period
    [InlineData("tenant-.-test", true)] // single periods inside are ok
    public void TenantHeaderShouldBeCorrect(string tenantId, bool isValid)
    {
        using var client = new TestLokiHttpClient();

        if (isValid)
        {
            // Act
            client.SetTenant(tenantId);

            // Assert header is correctly set
            var tenantHeaders = client.Client.DefaultRequestHeaders
                .GetValues("X-Scope-OrgID")
                .ToList();

            tenantHeaders.ShouldBeEquivalentTo(new List<string> { tenantId });
        }
        else
        {
            // Act & Assert: invalid tenant IDs throw ArgumentException
            Should.Throw<ArgumentException>(() => client.SetTenant(tenantId));
        }
    }

    // Allowed special characters
    [Theory]
    [InlineData('!', true)]
    [InlineData('.', true)]
    [InlineData('_', true)]
    [InlineData('*', true)]
    [InlineData('\'', true)]
    [InlineData('(', true)]
    [InlineData(')', true)]
    [InlineData('-', true)]

    // Disallowed special characters
    [InlineData('@', false)]
    [InlineData('#', false)]
    [InlineData('&', false)]
    [InlineData('$', false)]
    [InlineData('%', false)]
    [InlineData('^', false)]
    [InlineData('=', false)]
    [InlineData('+', false)]
    [InlineData('[', false)]
    [InlineData(']', false)]
    [InlineData('{', false)]
    [InlineData('}', false)]
    [InlineData('<', false)]
    [InlineData('>', false)]
    [InlineData('?', false)]
    [InlineData('/', false)]
    [InlineData('\\', false)]
    [InlineData('|', false)]
    [InlineData('~', false)]
    [InlineData('"', false)]
    public void TenantSpecialCharacterShouldValidateCorrectly(char specialChar, bool isValid)
    {
        using var client = new TestLokiHttpClient();
        string tenantId = "tenant" + specialChar + "123";

        if (isValid)
        {
            // Should succeed
            client.SetTenant(tenantId);

            var tenantHeaders = client.Client.DefaultRequestHeaders
                .GetValues("X-Scope-OrgID")
                .ToList();

            tenantHeaders.ShouldBeEquivalentTo(new List<string> { tenantId });
        }
        else
        {
            // Should throw
            Should.Throw<ArgumentException>(() => client.SetTenant(tenantId));
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

    [Theory]
    [InlineData("Custom-Header", "HeaderValue", true)]
    [InlineData("X-Test", "12345", true)]
    [InlineData("X-Correlation-ID", "abcd-1234", true)]
    [InlineData("X-Feature-Flag", "enabled", true)]
    [InlineData("", "value", false)]
    [InlineData(" ", "value", false)]
    [InlineData(null, "value", false)]
    [InlineData("Invalid Header", "value", false)]
    [InlineData("X-Test", "", false)]
    [InlineData("X-Test", null, false)]
    public void SetDefaultHeadersShouldValidateCorrectly(string? headerKey, string? headerValue, bool isValid)
    {
        using var httpClient = new HttpClient();
        var client = new TestLokiHttpClient(httpClient);

        if (isValid)
        {
            var headersToSet = new Dictionary<string, string>
            {
                { headerKey!, headerValue! }
            };

            client.SetDefaultHeaders(headersToSet);

            httpClient.DefaultRequestHeaders.Contains(headerKey!).ShouldBeTrue();
            httpClient.DefaultRequestHeaders
                .GetValues(headerKey!)
                .ShouldBe(new[] { headerValue });
        }
        else
        {
            Should.Throw<ArgumentException>(() =>
            {
                var headersToSet = new Dictionary<string, string>
                {
                    { headerKey!, headerValue! }
                };
                client.SetDefaultHeaders(headersToSet);
            });
        }
    }

    [Theory]
    [InlineData('!', true)]
    [InlineData('#', true)]
    [InlineData('$', true)]
    [InlineData('%', true)]
    [InlineData('&', true)]
    [InlineData('\'', true)]
    [InlineData('*', true)]
    [InlineData('+', true)]
    [InlineData('-', true)]
    [InlineData('.', true)]
    [InlineData('^', true)]
    [InlineData('_', true)]
    [InlineData('`', true)]
    [InlineData('|', true)]
    [InlineData('~', true)]
    [InlineData('A', true)]
    [InlineData('z', true)]
    [InlineData(' ', false)]
    [InlineData('(', false)]
    [InlineData(')', false)]
    [InlineData('<', false)]
    [InlineData('>', false)]
    [InlineData('@', false)]
    [InlineData(',', false)]
    [InlineData(';', false)]
    [InlineData(':', false)]
    [InlineData('"', false)]
    [InlineData('/', false)]
    [InlineData('[', false)]
    [InlineData(']', false)]
    [InlineData('?', false)]
    [InlineData('=', false)]
    [InlineData('{', false)]
    [InlineData('}', false)]
    [InlineData('\\', false)]
    [InlineData('\t', false)]
    public void DefaultHeaderCharactersShouldValidateCorrectly(char character, bool isValid) // Valid token characters according to RFC 7230
    {
        using var httpClient = new HttpClient();
        var client = new TestLokiHttpClient(httpClient);

        string headerKey = "X-Test" + character;
        var headersToSet = new Dictionary<string, string>
        {
            { headerKey, "value" }
        };

        if (isValid)
        {
            // Should succeed
            client.SetDefaultHeaders(headersToSet);

            httpClient.DefaultRequestHeaders.Contains(headerKey).ShouldBeTrue();
            httpClient.DefaultRequestHeaders.GetValues(headerKey).ShouldHaveSingleItem().ShouldBe("value");
        }
        else
        {
            // Should throw exception
            Should.Throw<ArgumentException>(() => client.SetDefaultHeaders(headersToSet));
        }
    }
}
