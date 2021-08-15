using System;
using System.Text.RegularExpressions;
using Serilog.Sinks.Grafana.Loki.Tests.TestHelpers;
using Shouldly;
using Xunit;

namespace Serilog.Sinks.Grafana.Loki.Tests.IntegrationTests
{
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
        public void RequestContentShouldMatchApproved()
        {
            var logger = new LoggerConfiguration()
                .WriteTo.GrafanaLoki(
                    "https://loki:3100",
                    outputTemplate: OutputTemplate,
                    httpClient: _client)
                .CreateLogger();

            logger.Error("An error occured");
            logger.Dispose();

            _client.Content.ShouldMatchApproved(c =>
            {
                c.SubFolder(ApprovalsFolderName);
                c.WithScrubber(s => Regex.Replace(s, "\"[0-9]{19}\"", "\"<unixepochinnanoseconds>\""));
            });
        }

        [Fact]
        public void LevelLabelShouldBeCreatedCorrectly()
        {
            var logger = new LoggerConfiguration()
                .WriteTo.GrafanaLoki(
                    "https://loki:3100",
                    outputTemplate: OutputTemplate,
                    httpClient: _client,
                    createLevelLabel: true)
                .CreateLogger();

            logger.Error("An error occured");
            logger.Dispose();

            _client.Content.ShouldMatchApproved(c =>
            {
                c.SubFolder(ApprovalsFolderName);
                c.WithScrubber(s => Regex.Replace(s, "\"[0-9]{19}\"", "\"<unixepochinnanoseconds>\""));
            });
        }

        [Fact]
        public void OnlyIncludedLabelsShouldBePresentInRequest()
        {
            var logger = new LoggerConfiguration()
                .Enrich.WithProperty("server_name", "loki_test")
                .Enrich.WithProperty("server_ip", "127.0.0.1")
                .WriteTo.GrafanaLoki(
                    "https://loki:3100",
                    outputTemplate: OutputTemplate,
                    filtrationMode: LokiLabelFiltrationMode.Include,
                    filtrationLabels: new[] {"server_ip"},
                    httpClient: _client)
                .CreateLogger();

            logger.Error("An error occured");
            logger.Dispose();

            _client.Content.ShouldMatchApproved(c =>
            {
                c.SubFolder(ApprovalsFolderName);
                c.WithScrubber(s => Regex.Replace(s, "\"[0-9]{19}\"", "\"<unixepochinnanoseconds>\""));
            });
        }

        [Fact]
        public void ExcludedLabelsShouldNotBePresentInRequest()
        {
            var logger = new LoggerConfiguration()
                .Enrich.WithProperty("server_name", "loki_test")
                .Enrich.WithProperty("server_ip", "127.0.0.1")
                .WriteTo.GrafanaLoki(
                    "https://loki:3100",
                    outputTemplate: OutputTemplate,
                    filtrationMode: LokiLabelFiltrationMode.Exclude,
                    filtrationLabels: new[] {"server_ip"},
                    httpClient: _client)
                .CreateLogger();

            logger.Error("An error occured");
            logger.Dispose();

            _client.Content.ShouldMatchApproved(c =>
            {
                c.SubFolder(ApprovalsFolderName);
                c.WithScrubber(s => Regex.Replace(s, "\"[0-9]{19}\"", "\"<unixepochinnanoseconds>\""));
            });
        }

        [Fact]
        public void EntryShouldBeRenderedAccordingToOutputTemplate()
        {
            var logger = new LoggerConfiguration()
                .WriteTo.GrafanaLoki(
                    "https://loki:3100",
                    outputTemplate: "[{Level:u3}] {Message}",
                    httpClient: _client)
                .CreateLogger();

            logger.Error("An error occured");
            logger.Dispose();

            _client.Content.ShouldMatchApproved(c =>
            {
                c.SubFolder(ApprovalsFolderName);
                c.WithScrubber(s => Regex.Replace(s, "\"[0-9]{19}\"", "\"<unixepochinnanoseconds>\""));
            });
        }

        [Fact]
        public void GlobalLabelsShouldNotBeFiltered()
        {
            var logger = new LoggerConfiguration()
                .WriteTo.GrafanaLoki(
                    "https://loki:3100",
                    outputTemplate: OutputTemplate,
                    filtrationMode: LokiLabelFiltrationMode.Exclude,
                    filtrationLabels: new[] {"server_ip"},
                    labels: new[] {new LokiLabel {Key = "server_ip", Value = "127.0.0.1"}},
                    httpClient: _client)
                .CreateLogger();

            logger.Error("An error occured");
            logger.Dispose();

            _client.Content.ShouldMatchApproved(c =>
            {
                c.SubFolder(ApprovalsFolderName);
                c.WithScrubber(s => Regex.Replace(s, "\"[0-9]{19}\"", "\"<unixepochinnanoseconds>\""));
            });
        }

        [Fact]
        public void SameGroupLabelsShouldBeInTheSameStreams()
        {
            var logger = new LoggerConfiguration()
                .WriteTo.GrafanaLoki(
                    "https://loki:3100",
                    outputTemplate: OutputTemplate,
                    period: BatchPeriod,
                    httpClient: _client)
                .CreateLogger();

            logger.Information("This is an information without params");
            logger.Information("This is also an information without params");
            logger.Dispose();

            _client.Content.ShouldMatchApproved(c =>
            {
                c.SubFolder(ApprovalsFolderName);
                c.WithScrubber(s => Regex.Replace(s, "\"[0-9]{19}\"", "\"<unixepochinnanoseconds>\""));
            });
        }

        [Fact]
        public void DifferentLevelsShouldNotGenerateDifferentStreamsWithoutLevelLabel()
        {
            var logger = new LoggerConfiguration()
                .WriteTo.GrafanaLoki(
                    "https://loki:3100",
                    outputTemplate: OutputTemplate,
                    period: BatchPeriod,
                    httpClient: _client)
                .CreateLogger();

            logger.Information("This is an information without params");
            logger.Error("This is an error without params");
            logger.Dispose();

            _client.Content.ShouldMatchApproved(c =>
            {
                c.SubFolder(ApprovalsFolderName);
                c.WithScrubber(s => Regex.Replace(s, "\"[0-9]{19}\"", "\"<unixepochinnanoseconds>\""));
            });
        }

        [Fact]
        public void LevelLabelShouldGenerateNewGroupAndStream()
        {
            var logger = new LoggerConfiguration()
                .WriteTo.GrafanaLoki(
                    "https://loki:3100",
                    outputTemplate: OutputTemplate,
                    period: BatchPeriod,
                    httpClient: _client,
                    createLevelLabel: true)
                .CreateLogger();

            logger.Information("This is an information without params");
            logger.Error("This is an error without params");
            logger.Dispose();

            _client.Content.ShouldMatchApproved(c =>
            {
                c.SubFolder(ApprovalsFolderName);
                c.WithScrubber(s => Regex.Replace(s, "\"[0-9]{19}\"", "\"<unixepochinnanoseconds>\""));
            });
        }

        [Fact]
        public void ParameterShouldGenerateNewGroupAndStream()
        {
            var logger = new LoggerConfiguration()
                .WriteTo.GrafanaLoki(
                    "https://loki:3100",
                    outputTemplate: OutputTemplate,
                    period: BatchPeriod,
                    httpClient: _client)
                .CreateLogger();

            logger.Information("What is the meaning of life?");
            logger.Information("The meaning of life is {@MeaningOfLife}", 42);
            logger.Dispose();

            _client.Content.ShouldMatchApproved(c =>
            {
                c.SubFolder(ApprovalsFolderName);
                c.WithScrubber(s => Regex.Replace(s, "\"[0-9]{19}\"", "\"<unixepochinnanoseconds>\""));
            });
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
    }
}