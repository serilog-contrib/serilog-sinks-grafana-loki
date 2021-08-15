// Copyright 2020-2021 Mykhailo Shevchuk & Contributors
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

        public bool TryDequeue(out T? item)
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