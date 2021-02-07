// Copyright 2020-2021 Mykhailo Shevchuk & Contributors
//
// Licensed under the MIT license;
// you may not use this file except in compliance with the License.
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.IO;
using Serilog.Events;

namespace Serilog.Sinks.Grafana.Loki
{
    /// <summary>
    /// Interface that has a Format method that accepts labels as input
    /// </summary>
    public interface ILabelAwareTextFormatter
    {
        /// <summary>
        /// Used to exclude the Level label.
        /// </summary>
        public bool ExcludeLevelLabel { get; }

        /// <summary>
        /// Format the log event into the output.
        /// </summary>
        /// <param name="logEvent">The event to format.</param>
        /// <param name="output">The output.</param>
        /// <param name="labels">List of labels that are attached to this stream</param>
        public void Format(LogEvent logEvent, TextWriter output, IEnumerable<string> labels);
    }
}
