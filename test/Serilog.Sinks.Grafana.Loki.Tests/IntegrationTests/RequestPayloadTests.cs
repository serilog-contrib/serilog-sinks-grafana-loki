/*using System.Text.RegularExpressions;
using Serilog.Sinks.Grafana.Loki.Tests.TestHelpers;
using Shouldly;
using Xunit;

namespace Serilog.Sinks.Grafana.Loki.Tests.IntegrationTests;

public class RequestPayloadTests
{
    private const string ApprovalsFolderName = "Approvals";
    private const string OutputTemplate = "{Message}";

    private static readonly TimeSpan BatchPeriod = TimeSpan.FromHours(1);

    private readonly TestLokiHttpClient _client;

    public RequestPayloadTests()
    {
        _client = new TestLokiHttpClient();
    }

    [Fact]
    public void LabelsForIndexedPlaceholdersShouldBeCreatedWithParamPrefix()
    {
        var logger = new LoggerConfiguration()
            .WriteTo.GrafanaLoki(
                "https://loki:3100",
                outputTemplate: OutputTemplate,
                httpClient: _client)
            .CreateLogger();

        logger.Information("An error occured in {0}", "Namespace.Module.Method");
        logger.Dispose();

        _client.Content.ShouldMatchApproved(c =>
        {
            c.SubFolder(ApprovalsFolderName);
            c.WithScrubber(s => Regex.Replace(s, "\"[0-9]{19}\"", "\"<unixepochinnanoseconds>\""));
        });
    }
}*/