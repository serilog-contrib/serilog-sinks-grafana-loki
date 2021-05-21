using System;
using Serilog.Core;
using Serilog.Events;

namespace Serilog.Sinks.Grafana.Loki
{
    public class GrafanaLokiSink : ILogEventSink, IDisposable
    {
        public GrafanaLokiSink()
        {
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