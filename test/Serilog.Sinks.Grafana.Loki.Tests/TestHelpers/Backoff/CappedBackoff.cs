using System;

namespace Serilog.Sinks.Grafana.Loki.Tests.TestHelpers.Backoff
{
    internal class CappedBackoff : IBackoff
    {
        private readonly TimeSpan _currentInterval;

        public CappedBackoff(TimeSpan currentInterval)
        {
            _currentInterval = currentInterval;
        }

        public IBackoff GetNext(TimeSpan nextInterval)
        {
            if (nextInterval != _currentInterval)
            {
                throw new Exception("Once backoff implementation is capped, it should remain capped");
            }

            return this;
        }
    }
}