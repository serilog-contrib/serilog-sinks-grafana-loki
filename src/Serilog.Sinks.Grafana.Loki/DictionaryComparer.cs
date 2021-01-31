using System.Collections.Generic;
using System.Linq;

namespace Serilog.Sinks.Grafana.Loki
{
    /// <summary>
    /// Used to compare if two dictionaries have equal key and values
    /// </summary>
    /// <typeparam name="TKey"></typeparam>
    /// <typeparam name="TValue"></typeparam>
    internal class DictionaryComparer<TKey, TValue> : IEqualityComparer<IDictionary<TKey, TValue>>
    {
        public static DictionaryComparer<TKey, TValue> Instance { get; } = new DictionaryComparer<TKey, TValue>();

        public bool Equals(IDictionary<TKey, TValue> x, IDictionary<TKey, TValue> y)
        {
            if (ReferenceEquals(x, y))
            {
                return true;
            }

            if (x is null || y is null || x.GetType() != y.GetType())
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
                    hash = (hash * 27) + kvp.Key.GetHashCode();
                    hash = (hash * 27) + kvp.Value.GetHashCode();
                }

                return hash;
            }
        }
    }
}
