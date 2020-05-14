using System;
using Serilog.Formatting;
using Serilog.Sinks.Http;
using Serilog.Sinks.Http.Private.Sinks;

namespace Serilog.Sinks.Loki.Sinks.Loki
{
    public class LokiSink : HttpSink ////PeriodicBatchingSink
    {
        /*public LokiSink(LokiSinkOptions options) : base(options.BatchPostingLimit, options.Period, options.QueueSizeLimit)
        {
        }*/

        public LokiSink(string requestUri, int batchPostingLimit, int queueLimit, TimeSpan period,
            ITextFormatter textFormatter, IBatchFormatter batchFormatter, IHttpClient httpClient) : base(requestUri,
            batchPostingLimit, queueLimit, period, textFormatter, batchFormatter, httpClient)
        {
        }

        public LokiSink(string requestUri, int batchPostingLimit, TimeSpan period, ITextFormatter textFormatter,
            IBatchFormatter batchFormatter, IHttpClient httpClient) : base(requestUri, batchPostingLimit, period,
            textFormatter, batchFormatter, httpClient)
        {
        }
    }
}