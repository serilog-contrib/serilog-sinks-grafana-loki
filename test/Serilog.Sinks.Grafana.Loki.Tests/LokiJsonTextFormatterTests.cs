using Serilog.Events;
using Serilog.Parsing;
using Serilog.Sinks.Grafana.Loki.Utils;
using Shouldly;
using Xunit;

namespace Serilog.Sinks.Grafana.Loki.Tests;

public class LokiJsonTextFormatterTests
{
    [Theory]
    [InlineData(false, "Log {Token}", "Test", "{\"Message\":\"Log \\\"Test\\\"\",\"MessageTemplate\":\"Log {Token}\",\"Token\":\"Test\"}")]
    [InlineData(true, "Log {Token}", "Test", "{\"Message\":\"Log Test\",\"MessageTemplate\":\"Log {Token}\",\"Token\":\"Test\"}")]
    public void ShouldRenderMessageWithCorrectFormatting(bool useStringLiteralFormat, string messageTemplate, string property, string expected)
    {
        var formatter = new LokiJsonTextFormatter(useStringLiteralFormat);
        var parser = new MessageTemplateParser();
        var msgTemplate = parser.Parse(messageTemplate);
        var properties = new List<LogEventProperty> { new LogEventProperty("Token", new ScalarValue(property)) };
        var logEvent = new LogEvent(DateTimeOffset.Now, LogEventLevel.Information, null, msgTemplate, properties);

        var output = new StringWriter();
        formatter.Format(logEvent, output);

        output.ToString().ShouldBe(expected);
    }
}