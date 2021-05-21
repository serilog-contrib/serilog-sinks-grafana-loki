using System;

namespace Serilog.Sinks.Grafana.Loki.Infrastructure
{
    internal class ExponentialBackoffConnectionSchedule
    {
        public static readonly TimeSpan MinimumBackoffPeriod = TimeSpan.FromSeconds(5);
        public static readonly TimeSpan MaximumBackoffInterval = TimeSpan.FromMinutes(10);

        private readonly TimeSpan _period;

        private int _failuresSinceSuccessfulConnection;

        public ExponentialBackoffConnectionSchedule(TimeSpan period)
        {
            if (period < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(period),
                    "The connection retry period must be a positive timespan");
            }

            _period = period;
        }

        public TimeSpan NextInterval
        {
            get
            {
                try
                {
                    if (_failuresSinceSuccessfulConnection <= 1)
                    {
                        return _period;
                    }

                    var backoffFactor = Math.Pow(2, _failuresSinceSuccessfulConnection - 1);
                    var backoffPeriod = Math.Max(_period.Ticks, MinimumBackoffPeriod.Ticks);
                    var backedOff = checked((long)(backoffPeriod * backoffFactor));
                    var cappedBackoff = Math.Min(MaximumBackoffInterval.Ticks, backedOff);
                    var actual = Math.Max(_period.Ticks, cappedBackoff);

                    return TimeSpan.FromTicks(actual);
                }
                catch (OverflowException)
                {
                    return MaximumBackoffInterval;
                }
            }
        }

        public void MarkSuccess()
        {
            _failuresSinceSuccessfulConnection = 0;
        }

        public void MarkFailure()
        {
            _failuresSinceSuccessfulConnection++;
        }
    }
}