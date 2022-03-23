// Copyright 2020-2022 Mykhailo Shevchuk & Contributors
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
using System.IO;
using Serilog.Events;
using Serilog.Formatting;

namespace Serilog.Sinks.Grafana.Loki
{
    /// <summary>
    /// Formats batches of log events into payloads that can be sent over the network.
    /// </summary>
    public interface ILokiBatchFormatter
    {
        /// <summary>
        /// Format the log events into a payload.
        /// </summary>
        /// <param name="logEvents">
        /// The events to format.
        /// </param>
        /// <param name="formatter">
        /// The formatter turning the log events into a textual representation.
        /// </param>
        /// <param name="output">
        /// The payload to send over the network.
        /// </param>
        void Format(IReadOnlyCollection<(LogEvent LogEntry, DateTimeOffset Timestamp)> logEvents, ITextFormatter formatter, TextWriter output);
    }
}