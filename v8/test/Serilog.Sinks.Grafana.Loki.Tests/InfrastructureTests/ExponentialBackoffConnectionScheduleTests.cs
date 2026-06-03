using Serilog.Sinks.Grafana.Loki.Infrastructure;
using Serilog.Sinks.Grafana.Loki.Tests.TestHelpers.Backoff;
using Shouldly;
using Xunit;

namespace Serilog.Sinks.Grafana.Loki.Tests.InfrastructureTests;

public class ExponentialBackoffConnectionScheduleTests
{
    [Theory]
    [InlineData(1)] // 1s
    [InlineData(2)] // 2s
    [InlineData(5)] // 5s
    [InlineData(10)] // 10s
    [InlineData(30)] // 30s
    [InlineData(1 * 60)] // 1 min
    [InlineData(5 * 60)] // 5 min
    [InlineData(10 * 60)] // 10 min
    public void SchedulerShouldReturnPeriodOnSuccess(int periodInSeconds)
    {
        var expected = TimeSpan.FromSeconds(periodInSeconds);
        var schedule = new ExponentialBackoffConnectionSchedule(expected);

        var actual = schedule.NextInterval;

        actual.ShouldBe(expected);
    }

    [Theory]
    [InlineData(1)] // 1s
    [InlineData(2)] // 2s
    [InlineData(5)] // 5s
    [InlineData(10)] // 10s
    [InlineData(30)] // 30s
    [InlineData(1 * 60)] // 1 min
    [InlineData(5 * 60)] // 5 min
    [InlineData(10 * 60)] // 10 min
    public void SchedulerShouldReturnPeriodAfterFirstFailure(int periodInSeconds)
    {
        var expected = TimeSpan.FromSeconds(periodInSeconds);
        var schedule = new ExponentialBackoffConnectionSchedule(expected);

        schedule.MarkFailure();

        var actual = schedule.NextInterval;

        actual.ShouldBe(expected);
    }

    [Theory]
    [InlineData(1)] // 1s
    [InlineData(2)] // 2s
    [InlineData(5)] // 5s
    [InlineData(10)] // 10s
    [InlineData(30)] // 30s
    [InlineData(1 * 60)] // 1 min
    [InlineData(5 * 60)] // 5 min
    [InlineData(10 * 60)] // 10 min
    public void SchedulerShouldBehaveExponentially(int periodInSeconds)
    {
        var period = TimeSpan.FromSeconds(periodInSeconds);
        var schedule = new ExponentialBackoffConnectionSchedule(period);
        IBackoff backoff = new LinearBackoff(period);

        while (backoff is not CappedBackoff)
        {
            schedule.MarkFailure();

            backoff = backoff.GetNext(schedule.NextInterval);
        }

        schedule.NextInterval.ShouldBe(ExponentialBackoffConnectionSchedule.MaximumBackoffInterval);
    }

    [Theory]
    [InlineData(1)] // 1s
    [InlineData(2)] // 2s
    [InlineData(5)] // 5s
    [InlineData(10)] // 10s
    [InlineData(30)] // 30s
    [InlineData(1 * 60)] // 1 min
    [InlineData(5 * 60)] // 5 min
    [InlineData(10 * 60)] // 10 min
    public void SchedulerShouldRemainCappedDuringFailures(int periodInSeconds)
    {
        var period = TimeSpan.FromSeconds(periodInSeconds);
        var schedule = new ExponentialBackoffConnectionSchedule(period);

        while (schedule.NextInterval != ExponentialBackoffConnectionSchedule.MaximumBackoffInterval)
        {
            schedule.MarkFailure();
        }

        for (var i = 0; i < 10000; i++)
        {
            schedule.NextInterval.ShouldBe(ExponentialBackoffConnectionSchedule.MaximumBackoffInterval);
            schedule.MarkFailure();
        }
    }
}