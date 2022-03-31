using Serilog.Sinks.Grafana.Loki.Infrastructure;

namespace Serilog.Sinks.Grafana.Loki.Tests.TestHelpers.Backoff;

internal class ExponentialBackoff : IBackoff
{
    private readonly TimeSpan _currentInterval;

    public ExponentialBackoff(TimeSpan currentInterval)
    {
        _currentInterval = currentInterval;
    }

    public IBackoff GetNext(TimeSpan nextInterval)
    {
        // From the state of being exponential, the implementation can become capped
        if (nextInterval == ExponentialBackoffConnectionSchedule.MaximumBackoffInterval)
        {
            return new CappedBackoff(nextInterval);
        }

        // From the state of being exponential, the implementation can remain exponential
        if (nextInterval > _currentInterval)
        {
            return new ExponentialBackoff(nextInterval);
        }

        throw new Exception("Next interval can't be lower then current interval");
    }
}