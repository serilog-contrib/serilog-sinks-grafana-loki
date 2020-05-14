using System;
using System.Collections.Generic;
using Serilog.Configuration;
using Serilog.Events;
using Serilog.Formatting;
using Serilog.Formatting.Display;
using Serilog.Sinks.Http;
using Serilog.Sinks.Loki.Sinks.Loki;
using Serilog.Sinks.Loki.Utils;

namespace Serilog.Sinks.Loki
{
    public static class LoggerConfigurationLokiExtensions
    {
        private const string DefaultOutputTemplate =
            "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}";

        public static LoggerConfiguration Loki(
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

            var batchFormatter = labels != null ? new LokiBatchFormatter(labels) : new LokiBatchFormatter();
            var formatter = textFormatter ?? new MessageTemplateTextFormatter(outputTemplate);
            var client = httpClient ?? new DefaultLokiHttpClient();

            if (client is ILokiHttpClient lokiHttpClient)
            {
                lokiHttpClient.SetCredentials(credentials);
            }

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
    }
}