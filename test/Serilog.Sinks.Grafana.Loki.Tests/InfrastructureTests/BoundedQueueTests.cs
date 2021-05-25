using System;
using Serilog.Sinks.Grafana.Loki.Infrastructure;
using Shouldly;
using Xunit;

namespace Serilog.Sinks.Grafana.Loki.Tests.InfrastructureTests
{
    public class BoundedQueueTests
    {
        [Fact]
        public void QueueShouldThrowExceptionForNegativeQueueLimit()
        {
            Should
                .Throw<ArgumentOutOfRangeException>(() => new BoundedQueue<int>(-42))
                .Message.ShouldBe("Queue limit must be positive, or `null` to indicate unbounded (Parameter 'queueLimit')");
        }

        [Fact]
        public void QueueShouldUseFifo()
        {
            var queue = new BoundedQueue<int>(null);

            var enqueueResult1 = queue.TryEnqueue(1);
            var enqueueResult2 = queue.TryEnqueue(2);
            var enqueueResult3 = queue.TryEnqueue(3);

            var dequeueResult1 = queue.TryDequeue(out var dequeueItem1);
            var dequeueResult2 = queue.TryDequeue(out var dequeueItem2);
            var dequeueResult3 = queue.TryDequeue(out var dequeueItem3);

            enqueueResult1.ShouldBeTrue();
            enqueueResult2.ShouldBeTrue();
            enqueueResult3.ShouldBeTrue();

            dequeueResult1.ShouldBeTrue();
            dequeueResult2.ShouldBeTrue();
            dequeueResult3.ShouldBeTrue();

            dequeueItem1.ShouldBe(1);
            dequeueItem2.ShouldBe(2);
            dequeueItem3.ShouldBe(3);
        }

        [Fact]
        public void QueueShouldNotEnqueueFullQueue()
        {
            var queue = new BoundedQueue<int>(1);

            var enqueueResult1 = queue.TryEnqueue(1);
            var enqueueResult2 = queue.TryEnqueue(2);

            enqueueResult1.ShouldBeTrue();
            enqueueResult2.ShouldBeFalse();
        }

        [Fact]
        public void QueueShouldNotDequeueEmptyQueue()
        {
            var queue = new BoundedQueue<int>(null);

            var result = queue.TryDequeue(out var item);

            result.ShouldBeFalse();
            item.ShouldBe(default);
        }
    }
}