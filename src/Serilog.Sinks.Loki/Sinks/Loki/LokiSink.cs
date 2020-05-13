using Serilog.Sinks.PeriodicBatching;

namespace Serilog.Sinks.Loki.Sinks.Loki
{
    public class LokiSink : PeriodicBatchingSink
    {
        public LokiSink(LokiSinkOptions options) : base(options.BatchPostingLimit, options.Period, options.QueueSizeLimit)
        {
        }
    }
}