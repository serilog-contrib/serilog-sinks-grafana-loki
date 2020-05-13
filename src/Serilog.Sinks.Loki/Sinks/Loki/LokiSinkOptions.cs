using System;

namespace Serilog.Sinks.Loki.Sinks.Loki
{
    public class LokiSinkOptions
    {
        private int _queueSizeLimit;

        /// <summary>
        /// The maximum number of events to post in a single batch. Defaults to: 50.
        /// </summary>
        public int BatchPostingLimit { get; set; }

        /// <summary>
        /// The time to wait between checking for event batches. Defaults to 2 seconds.
        /// </summary>
        public TimeSpan Period { get; set; }

        /// <summary>
        /// The maximum number of events that will be held in-memory while waiting to ship them to
        /// Loki. Beyond this limit, events will be dropped. The default is 100,000. Has no effect on
        /// durable log shipping.
        /// </summary>
        public int QueueSizeLimit
        {
            get => _queueSizeLimit;
            set
            {
                if (value < 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(QueueSizeLimit), "Queue size limit must be non-zero.");
                }

                _queueSizeLimit = value;
            }
        }
    }
}