﻿using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Serilog.Sinks.Grafana.Loki.Tests.TestHelpers;
using Shouldly;
using Xunit;

namespace Serilog.Sinks.Grafana.Loki.Tests.FormatterTests
{
    // TODO: Exception without message
    // TODO: Nested exceptions
    public class LokiJsonTextFormatterTests
    {
        private const string ApprovalsFolderName = "Approvals";
        private const string OutputTemplate = "{Message}";

        private readonly TestLokiHttpClient _client;

        public LokiJsonTextFormatterTests()
        {
            _client = new TestLokiHttpClient();
        }

        [Fact]
        public void MessageWithoutParametersShouldBeSerializedCorrectly()
        {
            var logger = new LoggerConfiguration()
                .WriteTo.GrafanaLoki(
                    "http://loki:3100",
                    outputTemplate: OutputTemplate,
                    textFormatter: new LokiJsonTextFormatter(),
                    httpClient: _client)
                .CreateLogger();

            logger.Information("This is an information without params");
            logger.Dispose();

            _client.Content.ShouldMatchApproved(c =>
            {
                c.SubFolder(ApprovalsFolderName);
                c.WithScrubber(s => Regex.Replace(s, "\"[0-9]{19}\"", "\"<unixepochinnanoseconds>\""));
            });
        }

        [Fact]
        public void MessageWithParametersShouldBeSerializedCorrectly()
        {
            var logger = new LoggerConfiguration()
                .WriteTo.GrafanaLoki(
                    "http://loki:3100",
                    outputTemplate: OutputTemplate,
                    textFormatter: new LokiJsonTextFormatter(),
                    httpClient: _client)
                .CreateLogger();

            logger.Information("The meaning of life is {@MeaningOfLife}", 42);
            logger.Dispose();

            _client.Content.ShouldMatchApproved(c =>
            {
                c.SubFolder(ApprovalsFolderName);
                c.WithScrubber(s => Regex.Replace(s, "\"[0-9]{19}\"", "\"<unixepochinnanoseconds>\""));
            });
        }

        [Fact]
        public void SimpleExceptionShouldBeSerializedCorrectly()
        {
            var logger = new LoggerConfiguration()
                .WriteTo.GrafanaLoki(
                    "http://loki:3100",
                    outputTemplate: OutputTemplate,
                    textFormatter: new LokiJsonTextFormatter(),
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

            _client.Content.ShouldMatchApproved(c =>
            {
                c.SubFolder(ApprovalsFolderName);
                c.WithScrubber(s =>
                {
                    s = Regex
                        .Replace(s, "\"[0-9]{19}\"", "\"<unixepochinnanoseconds>\"");
                    return Regex.Replace(
                        s,
                        @"(?<=\\u0022StackTrace)(.*?)(?=}})",
                        @"<stack-trace>");
                });
            });
        }

        [Fact]
        public void MessagePropertyForExceptionsWithoutMessageShouldNotBeCreated()
        {
            var logger = new LoggerConfiguration()
                .WriteTo.GrafanaLoki(
                    "http://loki:3100",
                    outputTemplate: OutputTemplate,
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

            _client.Content.ShouldMatchApproved(c =>
            {
                c.SubFolder(ApprovalsFolderName);
                c.WithScrubber(s =>
                {
                    s = Regex
                        .Replace(s, "\"[0-9]{19}\"", "\"<unixepochinnanoseconds>\"");
                    return Regex.Replace(
                        s,
                        @"(?<=\\u0022StackTrace)(.*?)(?=}})",
                        @"<stack-trace>");
                });
            });
        }

        [Fact]
        public void AggregateExceptionShouldBeSerializedCorrectly()
        {
            var logger = new LoggerConfiguration()
                .WriteTo.GrafanaLoki(
                    "http://loki:3100",
                    outputTemplate: OutputTemplate,
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

            _client.Content.ShouldMatchApproved(c =>
            {
                c.SubFolder(ApprovalsFolderName);
                c.WithScrubber(s =>
                {
                    s = Regex
                        .Replace(s, "\"[0-9]{19}\"", "\"<unixepochinnanoseconds>\"");
                    return Regex.Replace(
                        s,
                        @"(?<=\\u0022StackTrace)(.*?)(?=}})",
                        @"<stack-trace>");
                });
            });
        }
    }
}