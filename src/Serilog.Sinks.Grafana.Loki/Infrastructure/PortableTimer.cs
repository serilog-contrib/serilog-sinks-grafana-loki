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
using System.Threading;
using System.Threading.Tasks;

namespace Serilog.Sinks.Grafana.Loki.Infrastructure
{
    internal class PortableTimer : IDisposable
    {
        private readonly Func<Task> _onTick;
        private readonly object _syncRoot = new();
        private readonly Timer _timer;

        private bool _isDisposed;
        private bool _isRunning;

        public PortableTimer(Func<Task> onTick)
        {
            _onTick = onTick ?? throw new ArgumentNullException(nameof(onTick));
            _timer = new Timer(_ => OnTick(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }

        public void Start(TimeSpan interval)
        {
            if (interval < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(interval));
            }

            lock (_syncRoot)
            {
                if (_isDisposed)
                {
                    throw new ObjectDisposedException(nameof(PortableTimer));
                }

                _timer.Change(interval, Timeout.InfiniteTimeSpan);
            }
        }

        public void Dispose()
        {
            lock (_syncRoot)
            {
                if (_isDisposed)
                {
                    return;
                }

                while (_isRunning)
                {
                    Monitor.Wait(_syncRoot);
                }

                _timer.Dispose();

                _isDisposed = true;
            }
        }

        private async void OnTick()
        {
            try
            {
                lock (_syncRoot)
                {
                    if (_isDisposed)
                    {
                        return;
                    }

                    // There's a little bit of raciness here, but it's needed to support the
                    // current API, which allows the tick handler to reenter and set the next interval.
                    if (_isRunning)
                    {
                        Monitor.Wait(_syncRoot);

                        if (_isDisposed)
                        {
                            return;
                        }
                    }

                    _isRunning = true;
                }

                await _onTick();
            }
            finally
            {
                lock (_syncRoot)
                {
                    _isRunning = false;
                    Monitor.PulseAll(_syncRoot);
                }
            }
        }
    }
}