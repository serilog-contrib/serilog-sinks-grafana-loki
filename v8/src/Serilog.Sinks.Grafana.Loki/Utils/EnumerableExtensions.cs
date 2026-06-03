// Copyright 2020-2022 Mykhailo Shevchuk & Contributors
//
// Licensed under the MIT license;
// you may not use this file except in compliance with the License.
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See LICENSE file in the project root for full license information.

namespace Serilog.Sinks.Grafana.Loki.Utils;

internal static class EnumerableExtensions
{
    public static (IEnumerable<TSource> Matched, IEnumerable<TSource> Unmatched) Partition<TSource>(
        this IEnumerable<TSource> source,
        Func<TSource, bool> predicate)
    {
        if (source == null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        if (predicate == null)
        {
            throw new ArgumentNullException(nameof(predicate));
        }

        var matched = new List<TSource>();
        var unmatched = new List<TSource>();

        foreach (var item in source)
        {
            (predicate(item) ? matched : unmatched).Add(item);
        }

        return (matched, unmatched);
    }

    public static void Deconstruct<TKey, TValue>(
        this KeyValuePair<TKey, TValue> kvp,
        out TKey key,
        out TValue value)
    {
        key = kvp.Key;
        value = kvp.Value;
    }
}