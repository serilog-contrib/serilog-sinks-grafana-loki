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
using Serilog.Events;

namespace Serilog.Sinks.Grafana.Loki.Utils;

internal static class LogEventExtensions
{
    /// <summary>
    /// Renames property in the event, if present.
    /// Otherwise no action is performed.
    /// Calls itself recursively to check if new name is free.
    /// If it is taken by other property - renames it according to strategy.
    /// </summary>
    /// <param name="logEvent">
    /// Log Event.
    /// </param>
    /// <param name="propertyName">
    /// Property name to be renamed.
    /// </param>
    /// <param name="renamingStrategy">
    /// Renaming strategy.
    /// </param>
    internal static void RenamePropertyIfPresent(
        this LogEvent logEvent,
        string propertyName,
        Func<string, string> renamingStrategy)
    {
        if (logEvent.Properties.TryGetValue(propertyName, out var value))
        {
            var newName = renamingStrategy(propertyName);
            logEvent.RemovePropertyIfPresent(propertyName);
            logEvent.RenamePropertyIfPresent(newName, renamingStrategy);
            logEvent.AddOrUpdateProperty(new LogEventProperty(newName, value));
        }
    }
}