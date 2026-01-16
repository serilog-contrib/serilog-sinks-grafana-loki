using System.Text.RegularExpressions;
using Serilog.Sinks.Grafana.Loki.Tests.TestHelpers;
using Shouldly;
using Xunit;

namespace Serilog.Sinks.Grafana.Loki.Tests.IntegrationTests;

public class LokiJsonTextFormatterRequestPayloadTests
{
    private const string ApprovalsFolderName = "Approvals";
    private const string ExceptionStackTraceRegEx = @"(?<= in)(.*?)(?=},\\)";
    private const string ExceptionStackTraceReplacement = " <stack-trace>";
    private const string TimeStampRegEx = "\"[0-9]{19}\"";
    private const string TimeStampReplacement = "\"<unixepochinnanoseconds>\"";

    private readonly TestLokiHttpClient _client;

    public LokiJsonTextFormatterRequestPayloadTests()
    {
        _client = new TestLokiHttpClient();
    }

    [Fact]
    public void MessageWithoutParametersShouldBeSerializedCorrectly()
    {
        var logger = new LoggerConfiguration()
            .WriteTo.GrafanaLoki(
                "https://loki:3100",
                httpClient: _client)
            .CreateLogger();

        logger.Information("This is an information without params");
        logger.Dispose();

        _client.Content.ShouldMatchApproved(
            c =>
            {
                c.SubFolder(ApprovalsFolderName);
                c.WithScrubber(
                    s => Regex.Replace(
                        s,
                        TimeStampRegEx,
                        TimeStampReplacement));
            });
    }

    [Fact]
    public void MessageWithParametersShouldBeSerializedCorrectly()
    {
        var logger = new LoggerConfiguration()
            .WriteTo.GrafanaLoki(
                "https://loki:3100",
                httpClient: _client)
            .CreateLogger();

        logger.Information("The meaning of life is {MeaningOfLife}", 42);
        logger.Dispose();

        _client.Content.ShouldMatchApproved(
            c =>
            {
                c.SubFolder(ApprovalsFolderName);
                c.WithScrubber(
                    s => Regex.Replace(
                        s,
                        TimeStampRegEx,
                        TimeStampReplacement));
            });
    }

    [Fact]
    public void SimpleExceptionShouldBeSerializedCorrectly()
    {
        var logger = new LoggerConfiguration()
            .WriteTo.GrafanaLoki(
                "https://loki:3100",
                httpClient: _client)
            .CreateLogger();

        try
        {
            throw new Exception("Exception message");
        }
        catch (Exception ex)
        {
            logger.Error(ex, "An error occured");
        }

        logger.Dispose();

        _client.Content.ShouldMatchApproved(
            c =>
            {
                c.SubFolder(ApprovalsFolderName);
                c.WithScrubber(
                    s =>
                    {
                        s = Regex
                            .Replace(
                                s,
                                TimeStampRegEx,
                                TimeStampReplacement);

                        return Regex.Replace(
                            s,
                            ExceptionStackTraceRegEx,
                            ExceptionStackTraceReplacement);
                    });
            });
    }

    [Fact]
    public void MessagePropertyForExceptionsWithoutMessageShouldNotBeCreated()
    {
        var logger = new LoggerConfiguration()
            .WriteTo.GrafanaLoki(
                "https://loki:3100",
                textFormatter: new LokiJsonTextFormatter(),
                httpClient: _client)
            .CreateLogger();

        try
        {
            throw new Exception(string.Empty);
        }
        catch (Exception ex)
        {
            logger.Error(ex, "An error occured");
        }

        logger.Dispose();

        _client.Content.ShouldMatchApproved(
            c =>
            {
                c.SubFolder(ApprovalsFolderName);
                c.WithScrubber(
                    s =>
                    {
                        s = Regex
                            .Replace(
                                s,
                                TimeStampRegEx,
                                TimeStampReplacement);

                        return Regex.Replace(
                            s,
                            ExceptionStackTraceRegEx,
                            ExceptionStackTraceReplacement);
                    });
            });
    }

    [Fact]
    public void AggregateExceptionShouldBeSerializedCorrectly()
    {
        var logger = new LoggerConfiguration()
            .WriteTo.GrafanaLoki(
                "https://loki:3100",
                textFormatter: new LokiJsonTextFormatter(),
                httpClient: _client)
            .CreateLogger();

        try
        {
            var exceptions = new List<Exception>();

            for (var i = 0; i < 5; i++)
            {
                try
                {
                    throw new Exception($"Exception {i}");
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            }

            throw new AggregateException("AggregateException", exceptions);
        }
        catch (Exception ex)
        {
            logger.Error(ex, "An error occured");
        }

        logger.Dispose();

        _client.Content.ShouldMatchApproved(
            c =>
            {
                c.SubFolder(ApprovalsFolderName);
                c.WithScrubber(
                    s =>
                    {
                        s = Regex
                            .Replace(
                                s,
                                TimeStampRegEx,
                                TimeStampReplacement);

                        return Regex.Replace(
                            s,
                            ExceptionStackTraceRegEx,
                            ExceptionStackTraceReplacement);
                    });
            });
    }

    [Fact]
    public void MessagePropertyForInternalTimestampShouldBeCreated()
    {
        var logger = new LoggerConfiguration()
            .WriteTo.GrafanaLoki(
                "https://loki:3100",
                textFormatter: new LokiJsonTextFormatter(),
                httpClient: _client,
                useInternalTimestamp: true)
            .CreateLogger();

        logger.Information("The meaning of life is {@MeaningOfLife}", 42);
        logger.Dispose();

        _client.Content.ShouldMatchApproved(
            c =>
            {
                c.SubFolder(ApprovalsFolderName);
                c.WithScrubber(
                    s =>
                    {
                        var replaced = Regex.Replace(
                            s,
                            TimeStampRegEx,
                            TimeStampReplacement);

                        return Regex.Replace(
                            replaced,
                            "[0-9]{4}\\-[0-9]{2}\\-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}.[0-9]*(\\-|\\+|\\\\u002B)[0-9]{2}:[0-9]{2}",
                            "<datetimeformatted>");
                    });
            });
    }

    [Fact]
    public void PropertyNameEqualToReservedKeywordShouldBeSanitized()
    {
        var logger = new LoggerConfiguration()
            .WriteTo.GrafanaLoki(
                "https://loki:3100",
                textFormatter: new LokiJsonTextFormatter(),
                httpClient: _client)
            .CreateLogger();

        logger.Information("This is {Message}", "Ukraine!");
        logger.Dispose();

        _client.Content.ShouldMatchApproved(
            c =>
            {
                c.SubFolder(ApprovalsFolderName);
                c.WithScrubber(
                    s => Regex.Replace(
                        s,
                        TimeStampRegEx,
                        TimeStampReplacement));
            });
    }

    [Fact]
    public void PropertiesAsLabelsShouldBeCreatedCorrectly()
    {
        var logger = new LoggerConfiguration()
            .WriteTo.GrafanaLoki(
                "https://loki:3100",
                propertiesAsLabels: new[] { "level" },
                textFormatter: new LokiJsonTextFormatter(),
                httpClient: _client)
            .CreateLogger();

        logger.Information("This is {Country}", "Ukraine!");
        logger.Dispose();

        _client.Content.ShouldMatchApproved(
            c =>
            {
                c.SubFolder(ApprovalsFolderName);
                c.WithScrubber(
                    s => Regex.Replace(
                        s,
                        TimeStampRegEx,
                        TimeStampReplacement));
            });
    }

    [Fact]
    public void GlobalLabelsShouldBeCreatedCorrectly()
    {
        var logger = new LoggerConfiguration()
            .WriteTo.GrafanaLoki(
                "https://loki:3100",
                new[] { new LokiLabel { Key = "server_ip", Value = "127.0.0.1" } },
                textFormatter: new LokiJsonTextFormatter(),
                httpClient: _client)
            .CreateLogger();

        logger.Information("This is {Country}", "Ukraine!");
        logger.Dispose();

        _client.Content.ShouldMatchApproved(
            c =>
            {
                c.SubFolder(ApprovalsFolderName);
                c.WithScrubber(
                    s => Regex.Replace(
                        s,
                        TimeStampRegEx,
                        TimeStampReplacement));
            });
    }

    [Fact]
    public void SameGroupLabelsShouldBeInTheSameStreams()
    {
        var logger = new LoggerConfiguration()
            .WriteTo.GrafanaLoki(
                "https://loki:3100",
                httpClient: _client)
            .CreateLogger();

        logger.Information("This is an information without params");
        logger.Information("This is also an information without params");
        logger.Dispose();

        _client.Content.ShouldMatchApproved(
            c =>
            {
                c.SubFolder(ApprovalsFolderName);
                c.WithScrubber(
                    s => Regex.Replace(
                        s,
                        TimeStampRegEx,
                        TimeStampReplacement));
            });
    }

    [Fact]
    public void ParameterAsLabelShouldGenerateNewGroupAndStream()
    {
        var logger = new LoggerConfiguration()
            .WriteTo.GrafanaLoki(
                "https://loki:3100",
                propertiesAsLabels: new[] { "MeaningOfLife" },
                httpClient: _client)
            .CreateLogger();

        logger.Information("What is the meaning of life?");
        logger.Information("The meaning of life is {MeaningOfLife}", 42);
        logger.Dispose();

        _client.Content.ShouldMatchApproved(
            c =>
            {
                c.SubFolder(ApprovalsFolderName);
                c.WithScrubber(
                    s => Regex.Replace(
                        s,
                        TimeStampRegEx,
                        TimeStampReplacement));
            });
    }

    [Fact]
    public void LabelsForIndexedPlaceholdersShouldBeCreatedWithParamPrefix()
    {
        var logger = new LoggerConfiguration()
            .WriteTo.GrafanaLoki(
                "https://loki:3100",
                httpClient: _client)
            .CreateLogger();

        logger.Information("An error occured in {0}", "Namespace.Module.Method");
        logger.Dispose();

        _client.Content.ShouldMatchApproved(
            c =>
            {
                c.SubFolder(ApprovalsFolderName);
                c.WithScrubber(
                    s => Regex.Replace(
                        s,
                        TimeStampRegEx,
                        TimeStampReplacement));
            });
    }

    [Fact]
    public void GlobalLabelShouldHavePriorityOverPropertyOne()
    {
        var logger = new LoggerConfiguration()
            .WriteTo.GrafanaLoki(
                "https://loki:3100",
                labels: new[] { new LokiLabel { Key = "App", Value = "test" } },
                propertiesAsLabels: new[] { "App" },
                httpClient: _client)
            .CreateLogger();

        logger.Information("Hello {App}", "not test");
        logger.Dispose();

        _client.Content.ShouldMatchApproved(
            c =>
            {
                c.SubFolder(ApprovalsFolderName);
                c.WithScrubber(
                    s => Regex.Replace(
                        s,
                        TimeStampRegEx,
                        TimeStampReplacement));
            });
    }

    [Fact]
    public void LevelPropertyShouldBeRenamed()
    {
        var logger = new LoggerConfiguration()
            .WriteTo.GrafanaLoki(
                "https://loki:3100",
                httpClient: _client)
            .CreateLogger();

        // ReSharper disable once InconsistentLogPropertyNaming
        logger.Information("Hero's {level}", 42);
        logger.Dispose();

        _client.Content.ShouldMatchApproved(
            c =>
            {
                c.SubFolder(ApprovalsFolderName);
                c.WithScrubber(
                    s => Regex.Replace(
                        s,
                        TimeStampRegEx,
                        TimeStampReplacement));
            });
    }

    [Fact]
    public void PropertiesAsStructuredMetadataShouldBeSerializedCorrectly()
    {
        var logger = new LoggerConfiguration()
            .WriteTo.GrafanaLoki(
                "https://loki:3100",
                propertiesAsStructuredMetadata: new[] { "trace_id", "user_id" },
                httpClient: _client)
            .CreateLogger();

        logger.Information("User action: {Action} by {trace_id} for {user_id}", "login", "0242ac120002", "superUser123");
        logger.Dispose();

        _client.Content.ShouldMatchApproved(
            c =>
            {
                c.SubFolder(ApprovalsFolderName);
                c.WithScrubber(
                    s => Regex.Replace(
                        s,
                        TimeStampRegEx,
                        TimeStampReplacement));
            });
    }

    [Fact]
    public void StructuredMetadataWithLeavePropertiesIntactShouldKeepProperties()
    {
        var logger = new LoggerConfiguration()
            .WriteTo.GrafanaLoki(
                "https://loki:3100",
                propertiesAsStructuredMetadata: new[] { "trace_id", "user_id" },
                leaveStructuredMetadataPropertiesIntact: true,
                httpClient: _client)
            .CreateLogger();

        logger.Information("User action: {Action} by {trace_id} for {user_id}", "login", "0242ac120002", "superUser123");
        logger.Dispose();

        _client.Content.ShouldMatchApproved(
            c =>
            {
                c.SubFolder(ApprovalsFolderName);
                c.WithScrubber(
                    s => Regex.Replace(
                        s,
                        TimeStampRegEx,
                        TimeStampReplacement));
            });
    }
}