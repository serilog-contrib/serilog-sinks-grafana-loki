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
        /// Adds a non-durable sink that sends log events to Grafana Loki.
        /// A non-durable sink will lose data after a system or process restart.
        /// </summary>
        /// <param name="sinkConfiguration">The logger configuration.</param>
        /// <param name="uri">The root URI of Loki.</param>
        /// <param name="labels">
        /// The globals log event labels, which will be user for enriching all requests.
        /// </param>
        /// <param name="credentials">Auth <see cref="LokiCredentials"/></param>
        /// <param name="outputTemplate">A message template describing the format used to write to the sink.
        /// The default is <code>"[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}"</code>.
        /// </param>
        /// <param name="restrictedToMinimumLevel"> The minimum level for events passed through the sink.
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
                SetupClientAndFormatters(labels, textFormatter, outputTemplate, httpClient, credentials);

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

        /// <summary>
        /// Adds a durable sink that sends log events to Grafana Loki. A durable sink
        /// will persist log events on disk in buffer files before sending them over the
        /// network, thus protecting against data loss after a system or process restart. The
        /// buffer files will use a rolling behavior defined by the time interval specified in
        /// <paramref name="bufferPathFormat"/>, i.e. a new buffer file is created every time a new
        /// interval is started. The maximum size of a file is defined by
        /// <paramref name="bufferFileSizeLimitBytes"/>, and when that limit is reached all
        /// incoming log events will be dropped until a new interval is started.
        /// </summary>
        /// <param name="sinkConfiguration">The logger configuration.</param>
        /// <param name="uri">The root URI of Loki.</param>
        /// <param name="bufferPathFormat">
        /// The relative or absolute path format for a set of files that will be used to buffer
        /// events until they can be successfully sent over the network. Default value is
        /// "Buffer-{Date}.json". To use file rotation that is on an 30 or 60 minute interval pass
        /// "Buffer-{HalfHour}.json" or "Buffer-{Hour}.json".
        /// </param>
        /// <param name="bufferFileSizeLimitBytes">
        /// The approximate maximum size, in bytes, to which a buffer file for a specific time interval will be
        /// allowed to grow. By default no limit will be applied.
        /// </param>
        /// <param name="bufferFileShared">
        /// Allow the buffer file to be shared by multiple processes. Default value is false.
        /// </param>
        /// <param name="retainedBufferFileCountLimit">
        /// The maximum number of buffer files that will be retained, including the current buffer
        /// file. Under normal operation only 2 files will be kept, however if the log server is
        /// unreachable, the number of files specified by <paramref name="retainedBufferFileCountLimit"/>
        /// will be kept on the file system. For unlimited retention, pass null. Default value is 31.
        /// </param>
        /// <param name="labels">
        /// The globals log event labels, which will be user for enriching all requests.
        /// </param>
        /// <param name="credentials">Auth <see cref="LokiCredentials"/></param>
        /// <param name="outputTemplate">A message template describing the format used to write to the sink.
        /// The default is <code>"[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}"</code>.
        /// </param>
        /// <param name="restrictedToMinimumLevel"> The minimum level for events passed through the sink.
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
        public static LoggerConfiguration DurableGrafanaLokiUsingTimeRolledBuffers(
            this LoggerSinkConfiguration sinkConfiguration,
            string uri,
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

            var (batchFormatter, formatter, client) =
                SetupClientAndFormatters(labels, textFormatter, outputTemplate, httpClient, credentials);

            return sinkConfiguration.DurableHttpUsingTimeRolledBuffers(
                LokiRoutesBuilder.BuildLogsEntriesRoute(uri),
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

        /// <summary>
        /// Adds a durable sink that sends log events to Grafana Loki. A durable sink
        /// will persist log events on disk in buffer files before sending them over the
        /// network, thus protecting against data loss after a system or process restart. The
        /// buffer files will use a rolling behavior defined by the file size specified in
        /// <paramref name="bufferFileSizeLimitBytes"/>, i.e. a new buffer file is created when
        /// current has passed its limit. The maximum number of retained files is defined by
        /// <paramref name="retainedBufferFileCountLimit"/>, and when that limit is reached the
        /// oldest file is dropped to make room for a new.
        /// </summary>
        /// <param name="sinkConfiguration">The logger configuration.</param>
        /// <param name="uri">The root URI of Loki.</param>
        /// <param name="bufferBaseFileName">
        /// The relative or absolute path for a set of files that will be used to buffer events
        /// until they can be successfully transmitted across the network. Individual files will be
        /// created using the pattern "<paramref name="bufferBaseFileName"/>*.json", which should
        /// not clash with any other file names in the same directory. Default value is "Buffer".
        /// </param>
        /// <param name="bufferFileSizeLimitBytes">
        /// The approximate maximum size, in bytes, to which a buffer file for a specific time interval will be
        /// allowed to grow. By default no limit will be applied.
        /// </param>
        /// <param name="bufferFileShared">
        /// Allow the buffer file to be shared by multiple processes. Default value is false.
        /// </param>
        /// <param name="retainedBufferFileCountLimit">
        /// The maximum number of buffer files that will be retained, including the current buffer
        /// file. Under normal operation only 2 files will be kept, however if the log server is
        /// unreachable, the number of files specified by <paramref name="retainedBufferFileCountLimit"/>
        /// will be kept on the file system. For unlimited retention, pass null. Default value is 31.
        /// </param>
        /// <param name="labels">
        /// The globals log event labels, which will be user for enriching all requests.
        /// </param>
        /// <param name="credentials">Auth <see cref="LokiCredentials"/></param>
        /// <param name="outputTemplate">A message template describing the format used to write to the sink.
        /// The default is <code>"[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}"</code>.
        /// </param>
        /// <param name="restrictedToMinimumLevel"> The minimum level for events passed through the sink.
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
        public static LoggerConfiguration DurableGrafanaLokiUsingFileSizeRolledBuffers(
            this LoggerSinkConfiguration sinkConfiguration,
            string uri,
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

            var (batchFormatter, formatter, client) =
                SetupClientAndFormatters(labels, textFormatter, outputTemplate, httpClient, credentials);

            return sinkConfiguration.DurableHttpUsingFileSizeRolledBuffers(
                LokiRoutesBuilder.BuildLogsEntriesRoute(uri),
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

        private static (IBatchFormatter batchFormatter, ITextFormatter textFormatter, IHttpClient httpClient)
            SetupClientAndFormatters(
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