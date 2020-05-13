using Serilog.Core;
using Serilog.Events;

namespace Serilog.Sinks.Loki.Sinks.Loki
{
    public class LokiSink : ILogEventSink
    {
        public void Emit(LogEvent logEvent)
        {
            throw new System.NotImplementedException();
        }
    }
}