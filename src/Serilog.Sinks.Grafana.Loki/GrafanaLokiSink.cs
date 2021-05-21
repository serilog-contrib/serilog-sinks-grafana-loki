using System;
using System.Threading.Tasks;
using Serilog.Core;
using Serilog.Debugging;
using Serilog.Events;
using Serilog.Sinks.Grafana.Loki.Infrastructure;

namespace Serilog.Sinks.Grafana.Loki
{
    public class GrafanaLokiSink : ILogEventSink, IDisposable
    {
        private readonly string _requestUri;
        private readonly int _batchPostingLimit;
        private readonly int? _queueLimit;
        private readonly ExponentialBackoffConnectionSchedule _connectionSchedule;
        private readonly PortableTimer _timer;
        private readonly BoundedQueue<LogEvent> _queue;

        // Text formatter
        // Batch formatter
        // Client
        public GrafanaLokiSink(
            string requestUri,
            int batchPostingLimit,
            int? queueLimit,
            TimeSpan period)
        {
            _requestUri = requestUri ?? throw new ArgumentNullException(nameof(requestUri));
            _batchPostingLimit = batchPostingLimit;
            _queueLimit = queueLimit;

            _connectionSchedule = new ExponentialBackoffConnectionSchedule(period);
            _timer = new PortableTimer(OnTick);
            _queue = new BoundedQueue<LogEvent>(queueLimit);

            SetTimer();
        }

        public void Emit(LogEvent logEvent)
        {
            if (logEvent == null)
            {
                throw new ArgumentNullException(nameof(logEvent));
            }

            if (!_queue.TryEnqueue(logEvent))
            {
                SelfLog.WriteLine("Queue has reached it's limit and the log event {@Event} will be dropped", logEvent);
            }
        }

        public void Dispose()
        {
            throw new NotImplementedException();
        }

        private async Task OnTick()
        {
            throw new NotImplementedException();
        }

        private void SetTimer()
        {
            _timer.Start(_connectionSchedule.NextInterval);
        }
    }
}