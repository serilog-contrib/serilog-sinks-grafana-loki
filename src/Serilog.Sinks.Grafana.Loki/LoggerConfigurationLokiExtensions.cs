// Copyright 2020-2022 Mykhailo Shevchuk & Contributors
//
// Licensed under the MIT license;
// you may not use this file except in compliance with the License.
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See LICENSE file in the project root for full license information.

using System.Runtime.CompilerServices;
using Serilog.Configuration;
using Serilog.Events;
using Serilog.Formatting;
using Serilog.Formatting.Display;
using Serilog.Sinks.Grafana.Loki.HttpClients;
using Serilog.Sinks.Grafana.Loki.Utils;

[assembly:
    InternalsVisibleTo(
        "Serilog.Sinks.Grafana.Loki.Tests, PublicKey=00240000048000001401000006020000002400005253413100080000010001004538980045097B6B0736A5972D5514C1D20974B646CF20C961E2D40FC30EC72D0502BF825E70F478F2D4FE6123CAEF7D564336048251C59C6885A2BD3FFFE120FDD71ABAE36E8D9923CA0B036A9C592CF3484E05A83B77E0376AC83873862636DA15D47A0F65C2044BA8273CC7FE738D6CDD8D92BE26737A7DA28C76B0CE5DAEBDB3360E78CA10DA0BF39AA42B9ED626CC7178A6A2172EB68E585BFCBE6FA2F8176DF5C52DB151FB47149BF1AB55F9C11AC28FE40D3634A58007A3D9A1233190A7FBAE948398CF64FB813646E4013353B20F563CFD936061BBFCA06E89B4ED695140FF931832D06753704F58E7A4C7ED8C02E49BBBCBAA19DC05658612EF2D9B")]

namespace Serilog.Sinks.Grafana.Loki;

/// <summary>
/// Class containing extension methods to <see cref="LoggerConfiguration"/>, configuring sinks
/// sending log events to Grafana Loki using HTTP.
/// </summary>
public static class LoggerConfigurationLokiExtensions
{
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
    /// The global log event labels, which will be user for enriching all requests.
    /// </param>
    /// <param name="propertiesAsLabels">
    /// The list of properties, which would be mapped to the labels.
    /// </param>
    /// <param name="credentials">
    /// Auth <see cref="LokiCredentials"/>.
    /// </param>
    /// <param name="tenant">
    /// Tenant ID See <a href="https://grafana.com/docs/loki/latest/operations/multi-tenancy/">docs</a>.
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
    /// A custom <see cref="ILokiHttpClient"/> implementation. Default value is
    /// <see cref="LokiHttpClient"/>.
    /// </param>
    /// <param name="reservedPropertyRenamingStrategy">
    /// Renaming strategy for properties' names equal to reserved keywords.
    /// </param>
    /// <param name="useInternalTimestamp">
    /// Should use internal sink timestamp instead of application one to use as log timestamp.
    /// </param>
    /// <param name="leavePropertiesIntact">
    /// Leaves the list of properties intact after extracting the labels specified in propertiesAsLabels.
    /// </param>
    /// <param name="messageTemplate">
    /// The message template to use when rendering the log event's `Message` field.
    /// </param>
    /// <returns>Logger configuration, allowing configuration to continue.</returns>
    public static LoggerConfiguration GrafanaLoki(
        this LoggerSinkConfiguration sinkConfiguration,
        string uri,
        IEnumerable<LokiLabel>? labels = null,
        IEnumerable<string>? propertiesAsLabels = null,
        LokiCredentials? credentials = null,
        string? tenant = null,
        LogEventLevel restrictedToMinimumLevel = LevelAlias.Minimum,
        int batchPostingLimit = 1000,
        int? queueLimit = null,
        TimeSpan? period = null,
        ITextFormatter? textFormatter = null,
        ILokiHttpClient? httpClient = null,
        IReservedPropertyRenamingStrategy? reservedPropertyRenamingStrategy = null,
        bool useInternalTimestamp = false,
        bool leavePropertiesIntact = false,
        string? messageTemplate = null)
    {
        if (sinkConfiguration == null)
        {
            throw new ArgumentNullException(nameof(sinkConfiguration));
        }

        reservedPropertyRenamingStrategy ??= new DefaultReservedPropertyRenamingStrategy();
        period ??= TimeSpan.FromSeconds(1);
        textFormatter ??= new LokiJsonTextFormatter(reservedPropertyRenamingStrategy, messageTemplate);
        httpClient ??= new LokiHttpClient();

        httpClient.SetCredentials(credentials);
        httpClient.SetTenant(tenant);

        var batchFormatter = new LokiBatchFormatter(
            reservedPropertyRenamingStrategy,
            labels,
            propertiesAsLabels,
            useInternalTimestamp,
            leavePropertiesIntact);

        var sink = new LokiSink(
            LokiRoutesBuilder.BuildLogsEntriesRoute(uri),
            batchPostingLimit,
            queueLimit,
            period.Value,
            textFormatter,
            batchFormatter,
            httpClient);

        return sinkConfiguration.Sink(sink, restrictedToMinimumLevel);
    }
}