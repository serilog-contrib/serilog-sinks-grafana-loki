// Copyright 2020-2022 Mykhailo Shevchuk & Contributors
//
// Licensed under the MIT license;
// you may not use this file except in compliance with the License.
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See LICENSE file in the project root for full license information.

namespace Serilog.Sinks.Grafana.Loki
{
    /// <summary>
    /// Mode, used for labels filtration
    /// </summary>
    public enum LokiLabelFiltrationMode
    {
        /// <summary>
        /// By including specific labels
        /// </summary>
        Include = 0,
        /// <summary>
        /// By excluding specific labels
        /// </summary>
        Exclude = 1
    }
}