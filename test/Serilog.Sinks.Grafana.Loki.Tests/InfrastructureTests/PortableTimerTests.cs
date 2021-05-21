using System;
using System.Threading;
using System.Threading.Tasks;
using Serilog.Sinks.Http.Private.Time;
using Shouldly;
using Xunit;

#pragma warning disable 1998
namespace Serilog.Sinks.Grafana.Loki.Tests.InfrastructureTests
{
    public class PortableTimerTests
    {
        [Fact]
        public void TimerShouldThrowExceptionOnCreatingWithNullOnTick()
        {
            Should.Throw<ArgumentNullException>(() => new PortableTimer(null))
                .ParamName.ShouldBe("onTick");
        }

        [Fact]
        public void TimerShouldThrowExceptionOnStartWithNegativeInterval()
        {
            var wasCalled = false;
            using var timer = new PortableTimer(async () => { wasCalled = true; });

            Should.Throw<ArgumentOutOfRangeException>(() => timer.Start(TimeSpan.MinValue)).ParamName
                .ShouldBe("interval");
            wasCalled.ShouldBeFalse();
        }

        [Fact]
        public void TimerShouldThrowExceptionOnStartWhenDisposed()
        {
            var wasCalled = false;
            var timer = new PortableTimer(async () => { wasCalled = true; });

            timer.Start(TimeSpan.FromMilliseconds(100));
            timer.Dispose();

            wasCalled.ShouldBeFalse();
            Should.Throw<ObjectDisposedException>(() => timer.Start(TimeSpan.Zero)).ObjectName
                .ShouldBe(nameof(PortableTimer));
        }

        [Fact]
        public void TimerShouldWaitUntilEventHandlerOnDispose()
        {
            var wasCalled = false;
            var barrier = new Barrier(2);

            using (var timer = new PortableTimer(async () =>
            {
                barrier.SignalAndWait();
                await Task.Delay(100);
                wasCalled = true;
            }))
            {
                timer.Start(TimeSpan.Zero);
                barrier.SignalAndWait();
            }

            wasCalled.ShouldBeTrue();
        }

        [Fact]
        public void TimerShouldNotProcessEventWhenWaiting()
        {
            var wasCalled = false;

            using (var timer = new PortableTimer(async () =>
            {
                await Task.Delay(50);
                wasCalled = true;
            }))
            {
                timer.Start(TimeSpan.FromMilliseconds(20));
            }

            Thread.Sleep(100);

            wasCalled.ShouldBeFalse();
        }

        [Fact]
        public void EventShouldBeProcessedOneAtTimeWhenOverlaps()
        {
            var userHandlerOverlapped = false;

            // ReSharper disable AccessToModifiedClosure
            PortableTimer timer = null;
            timer = new PortableTimer(
                async () =>
                {
                    if (Monitor.TryEnter(timer!))
                    {
                        try
                        {
                            // ReSharper disable once PossibleNullReferenceException
                            timer.Start(TimeSpan.Zero);
                            Thread.Sleep(20);
                        }
                        finally
                        {
                            Monitor.Exit(timer);
                        }
                    }
                    else
                    {
                        userHandlerOverlapped = true;
                    }
                });

            timer.Start(TimeSpan.FromMilliseconds(1));
            Thread.Sleep(50);
            timer.Dispose();

            userHandlerOverlapped.ShouldBeFalse();
        }

        [Fact]
        public void TimerCanBeDisposedFromMultipleThread()
        {
            PortableTimer timer = null;

            // ReSharper disable once PossibleNullReferenceException
            timer = new PortableTimer(async () => timer.Start(TimeSpan.FromMilliseconds(1)));

            timer.Start(TimeSpan.Zero);
            Thread.Sleep(50);

            Parallel.For(0, Environment.ProcessorCount * 2, _ => timer.Dispose());
        }
    }
}