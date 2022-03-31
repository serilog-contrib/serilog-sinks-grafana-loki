// Copyright 2020-2022 Mykhailo Shevchuk & Contributors
//
// Licensed under the MIT license;
// you may not use this file except in compliance with the License.
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Linq;

namespace Serilog.Sinks.Grafana.Loki;

/// <summary>
/// Used to compare if two dictionaries have equal key and values
/// </summary>
/// <typeparam name="TKey"></typeparam>
/// <typeparam name="TValue"></typeparam>
internal class DictionaryComparer<TKey, TValue> : IEqualityComparer<IDictionary<TKey, TValue>>
{
    public static DictionaryComparer<TKey, TValue> Instance { get; } = new();

    public bool Equals(IDictionary<TKey, TValue> x, IDictionary<TKey, TValue> y)
    {
        if (ReferenceEquals(x, y))
        {
            return true;
        }

        if (x.GetType() != y.GetType())
        {
            return false;
        }

        return x.Count == y.Count && !x.Except(y).Any();
    }

    public int GetHashCode(IDictionary<TKey, TValue> obj)
    {
        // Overflow is fine, just wrap
        unchecked
        {
            var hash = 17;
            foreach (var kvp in obj.OrderBy(kvp => kvp.Key))
            {
                hash = (hash * 27) + kvp.Key!.GetHashCode();
                hash = (hash * 27) + kvp.Value!.GetHashCode();
            }

            return hash;
        }
    }
}