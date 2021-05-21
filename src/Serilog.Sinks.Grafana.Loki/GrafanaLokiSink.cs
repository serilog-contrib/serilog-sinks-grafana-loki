using System;
using Serilog.Core;
using Serilog.Events;

namespace Serilog.Sinks.Grafana.Loki
{
    public class GrafanaLokiSink : ILogEventSink, IDisposable
    {
        private readonly string _requestUri;
        private readonly int _batchPostingLimit;
        private readonly int? _queueLimit;

        // Text formatter
        // Batch formatter
        // Client
        public GrafanaLokiSink(
            string requestUri,
            int batchPostingLimit = 1000,
            int? queueLimit = null,
            TimeSpan? period = null)
        {
            _requestUri = requestUri ?? throw new ArgumentNullException(nameof(requestUri));
            _batchPostingLimit = batchPostingLimit;
            _queueLimit = queueLimit;
        }

        public void Emit(LogEvent logEvent)
        {
            throw new NotImplementedException();
        }

        public void Dispose()
        {
            throw new NotImplementedException();
        }
    }
}