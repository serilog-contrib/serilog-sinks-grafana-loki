using System;
using System.Collections.Generic;
using Serilog.Configuration;
using Serilog.Events;
using Serilog.Formatting;
using Serilog.Formatting.Display;
using Serilog.Sinks.Grafana.Loki.Utils;
using Serilog.Sinks.Http;

namespace Serilog.Sinks.Grafana.Loki
{
    public static class LoggerConfigurationLokiExtensions
    {
        private const string DefaultOutputTemplate =
            "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}";

        public static LoggerConfiguration GrafanaLoki(
            this LoggerSinkConfiguration sinkConfiguration,
            string url,
            IEnumerable<LokiLabel> labels = null,
            LokiCredentials credentials = null,
            string outputTemplate = DefaultOutputTemplate,
            LogEventLevel restrictedToMinimumLevel = LevelAlias.Minimum,
            int batchPostingLimit = 1000,
            int? queueLimit = null,
            TimeSpan? period = null,
            ITextFormatter textFormatter = null,
            IHttpClient httpClient = null)
        {
            if (sinkConfiguration == null)
            {
                throw new ArgumentNullException(nameof(sinkConfiguration));
            }

            var (batchFormatter, formatter, client) = SetupClientAndFormatters(labels, textFormatter, outputTemplate, httpClient, credentials);

            return sinkConfiguration.Http(
                LokiRoutes.BuildLogsEntriesRoute(url),
                batchPostingLimit,
                queueLimit,
                period,
                formatter,
                batchFormatter,
                restrictedToMinimumLevel,
                client);
        }

        public static LoggerConfiguration DurableGrafanaLokiUsingTimeRolledBuffers(
            this LoggerSinkConfiguration sinkConfiguration,
            string url,
            string bufferPathFormat = "Buffer-{Date}.json",
            long? bufferFileSizeLimitBytes = null,
            bool bufferFileShared = false,
            int? retainedBufferFileCountLimit = 31,
            IEnumerable<LokiLabel> labels = null,
            LokiCredentials credentials = null,
            string outputTemplate = DefaultOutputTemplate,
            LogEventLevel restrictedToMinimumLevel = LevelAlias.Minimum,
            int batchPostingLimit = 1000,
            int? queueLimit = null,
            TimeSpan? period = null,
            ITextFormatter textFormatter = null,
            IHttpClient httpClient = null)
        {
            if (sinkConfiguration == null)
            {
                throw new ArgumentNullException(nameof(sinkConfiguration));
            }

            var (batchFormatter, formatter, client) = SetupClientAndFormatters(labels, textFormatter, outputTemplate, httpClient, credentials);

            return sinkConfiguration.DurableHttpUsingTimeRolledBuffers(
                LokiRoutes.BuildLogsEntriesRoute(url),
                bufferPathFormat,
                bufferFileSizeLimitBytes,
                bufferFileShared,
                retainedBufferFileCountLimit,
                batchPostingLimit,
                period,
                formatter,
                batchFormatter,
                restrictedToMinimumLevel,
                client);
        }

        public static LoggerConfiguration DurableGrafanaLokiUsingFileSizeRolledBuffers(
            this LoggerSinkConfiguration sinkConfiguration,
            string url,
            string bufferBaseFileName = "Buffer",
            long? bufferFileSizeLimitBytes = 1024 * 1024 * 1024,
            bool bufferFileShared = false,
            int? retainedBufferFileCountLimit = 31,
            IEnumerable<LokiLabel> labels = null,
            LokiCredentials credentials = null,
            string outputTemplate = DefaultOutputTemplate,
            LogEventLevel restrictedToMinimumLevel = LevelAlias.Minimum,
            int batchPostingLimit = 1000,
            int? queueLimit = null,
            TimeSpan? period = null,
            ITextFormatter textFormatter = null,
            IHttpClient httpClient = null)
        {
            if (sinkConfiguration == null)
            {
                throw new ArgumentNullException(nameof(sinkConfiguration));
            }

            var (batchFormatter, formatter, client) = SetupClientAndFormatters(labels, textFormatter, outputTemplate, httpClient, credentials);

            return sinkConfiguration.DurableHttpUsingFileSizeRolledBuffers(
                LokiRoutes.BuildLogsEntriesRoute(url),
                bufferBaseFileName,
                bufferFileSizeLimitBytes,
                bufferFileShared,
                retainedBufferFileCountLimit,
                batchPostingLimit,
                period,
                formatter,
                batchFormatter,
                restrictedToMinimumLevel,
                client);
        }

        private static (IBatchFormatter batchFormatter, ITextFormatter textFormatter, IHttpClient httpClient) SetupClientAndFormatters(
            IEnumerable<LokiLabel> labels,
            ITextFormatter textFormatter,
            string outputTemplate,
            IHttpClient httpClient,
            LokiCredentials credentials)
        {
            var batchFormatter = labels != null ? new LokiBatchFormatter(labels) : new LokiBatchFormatter();
            var formatter = textFormatter ?? new MessageTemplateTextFormatter(outputTemplate);
            var client = httpClient ?? new DefaultLokiHttpClient();

            if (client is ILokiHttpClient lokiHttpClient)
            {
                lokiHttpClient.SetCredentials(credentials);
            }

            return ((IBatchFormatter) batchFormatter, formatter, client);
        }
    }
}