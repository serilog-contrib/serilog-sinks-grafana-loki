namespace Serilog.Sinks.Grafana.Loki.Tests.TestHelpers.Backoff;

internal interface IBackoff
{
    public IBackoff GetNext(TimeSpan nextInterval);
}