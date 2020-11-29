// Copyright 2020 Mykhailo Shevchuk & Contributors
//
// Licensed under the MIT license;
// you may not use this file except in compliance with the License.
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Serilog.Configuration;
using Serilog.Events;
using Serilog.Formatting;
using Serilog.Formatting.Display;
using Serilog.Sinks.Grafana.Loki.Utils;
using Serilog.Sinks.Http;

[assembly: InternalsVisibleTo("Serilog.Sinks.Grafana.Loki.Tests")]

namespace Serilog.Sinks.Grafana.Loki
{
    /// <summary>
    /// Class containing extension methods to <see cref="LoggerConfiguration"/>, configuring sinks
    /// sending log events to Grafana Loki using HTTP.
    /// </summary>
    public static class LoggerConfigurationLokiExtensions
    {
        private const string DefaultOutputTemplate =
            "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}";

        /// <summary>
        /// Adds a non-durable sink that will send log events to Grafana Loki.
        /// A non-durable sink will lose data after a system or process restart.
        /// </summary>
        /// <param name="sinkConfiguration">
        /// The logger configuration.
        /// </param>
        /// <param name="uri">
        /// The root URI of Loki.
        /// </param>
        /// <param name="labels">
        /// The globals log event labels, which will be user for enriching all requests.
        /// </param>
        /// <param name="filtrationMode">
        /// The mode for labels filtration
        /// </param>
        /// <param name="filtrationLabels">
        /// The list of label keys used for filtration
        /// </param>
        /// <param name="credentials">
        /// Auth <see cref="LokiCredentials"/>.
        /// </param>
        /// <param name="outputTemplate">
        /// A message template describing the format used to write to the sink.
        /// Default value is <code>"[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}"</code>.
        /// </param>
        /// <param name="restrictedToMinimumLevel">
        /// The minimum level for events passed through the sink.
        /// Default value is <see cref="LevelAlias.Minimum"/>.
        /// </param>
        /// <param name="batchPostingLimit">
        /// The maximum number of events to post in a single batch. Default value is 1000.
        /// </param>
        /// <param name="queueLimit">
        /// The maximum number of events stored in the queue in memory, waiting to be posted over
        /// the network. Default value is infinitely.
        /// </param>
        /// <param name="period">
        /// The time to wait between checking for event batches. Default value is 2 seconds.
        /// </param>
        /// <param name="textFormatter">
        /// The formatter rendering individual log events into text, for example JSON. Default
        /// value is <see cref="MessageTemplateTextFormatter"/>.
        /// </param>
        /// <param name="httpClient">
        /// A custom <see cref="IHttpClient"/> implementation. Default value is
        /// <see cref="DefaultLokiHttpClient"/>.
        /// </param>
        /// <returns>Logger configuration, allowing configuration to continue.</returns>
        public static LoggerConfiguration GrafanaLoki(
            this LoggerSinkConfiguration sinkConfiguration,
            string uri,
            IEnumerable<LokiLabel> labels = null,
            LokiLabelFiltrationMode? filtrationMode = null,
            IEnumerable<string> filtrationLabels = null,
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

            var (batchFormatter, formatter, client) =
                SetupClientAndFormatters(labels, filtrationMode, filtrationLabels, textFormatter, outputTemplate, httpClient, credentials);

            return sinkConfiguration.Http(
                LokiRoutesBuilder.BuildLogsEntriesRoute(uri),
                batchPostingLimit,
                queueLimit,
                period,
                formatter,
                batchFormatter,
                restrictedToMinimumLevel,
                client);
        }

        private static (IBatchFormatter batchFormatter, ITextFormatter textFormatter, IHttpClient httpClient)
            SetupClientAndFormatters(
                IEnumerable<LokiLabel> labels,
                LokiLabelFiltrationMode? filtrationMode,
                IEnumerable<string> filtrationLabels,
                ITextFormatter textFormatter,
                string outputTemplate,
                IHttpClient httpClient,
                LokiCredentials credentials)
        {
            var batchFormatter = new LokiBatchFormatter(labels, filtrationMode, filtrationLabels);
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