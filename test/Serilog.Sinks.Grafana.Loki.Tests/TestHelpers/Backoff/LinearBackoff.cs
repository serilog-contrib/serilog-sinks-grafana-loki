using System;
using Serilog.Sinks.Grafana.Loki.Infrastructure;

namespace Serilog.Sinks.Grafana.Loki.Tests.TestHelpers.Backoff
{
    internal class LinearBackoff : IBackoff
    {
        private readonly TimeSpan _currentInterval;

        public LinearBackoff(TimeSpan currentInterval)
        {
            _currentInterval = currentInterval;
        }

        IBackoff IBackoff.GetNext(TimeSpan nextInterval)
        {
            // From the state of being linear, the implementation can become capped
            if (nextInterval == ExponentialBackoffConnectionSchedule.MaximumBackoffInterval)
            {
                return new CappedBackoff(nextInterval);
            }

            // From the state of being linear, the implementation can become exponential
            if (nextInterval > _currentInterval)
            {
                return new ExponentialBackoff(nextInterval);
            }

            // From the state of being linear, the implementation can remain linear
            if (nextInterval == _currentInterval)
            {
                return this;
            }

            throw new Exception("The implementation from being linear must remain linear or become exponential");
        }
    }
}