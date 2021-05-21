using System;
using System.Collections.Generic;

namespace Serilog.Sinks.Grafana.Loki.Infrastructure
{
    internal class BoundedQueue<T>
    {
        private const int Unbounded = -1;

        private readonly Queue<T> _queue;
        private readonly int _queueLimit;
        private readonly object _syncRoot = new();

        public BoundedQueue(int? queueLimit)
        {
            if (queueLimit < 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(queueLimit),
                    "Queue limit must be positive, or `null` to indicate unbounded");
            }

            _queue = new Queue<T>();
            _queueLimit = queueLimit ?? Unbounded;
        }

        public bool TryEnqueue(T item)
        {
            lock (_syncRoot)
            {
                if (_queueLimit != Unbounded && _queueLimit == _queue.Count)
                {
                    return false;
                }

                _queue.Enqueue(item);
                return true;
            }
        }

        public bool TryDequeue(out T item)
        {
            lock (_syncRoot)
            {
                if (_queue.Count == 0)
                {
                    item = default;
                    return false;
                }

                item = _queue.Dequeue();
                return true;
            }
        }
    }
}