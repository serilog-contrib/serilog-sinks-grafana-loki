// Copyright 2020-2022 Mykhailo Shevchuk & Contributors
//
// Licensed under the MIT license;
// you may not use this file except in compliance with the License.
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See LICENSE file in the project root for full license information.

namespace Serilog.Sinks.Grafana.Loki;

/// <summary>
/// Defines renaming strategy for properties with names equal to sink's reserved keywords.
/// </summary>
public interface IReservedPropertyRenamingStrategy
{
    /// <summary>
    /// Property rename function
    /// By default adds an underscore to the property name.
    /// </summary>
    /// <param name="originalName">
    /// Original name of property.
    /// </param>
    /// <returns>
    /// New property name.
    /// </returns>
    string Rename(string originalName);
}