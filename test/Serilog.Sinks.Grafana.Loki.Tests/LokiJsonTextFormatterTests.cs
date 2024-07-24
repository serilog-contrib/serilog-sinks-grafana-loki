using Serilog.Events;
using Serilog.Parsing;
using Serilog.Sinks.Grafana.Loki.Utils;
using Shouldly;
using Xunit;

namespace Serilog.Sinks.Grafana.Loki.Tests;

public class LokiJsonTextFormatterTests
{
    [Theory]
    [InlineData(null, "Log {Token}", "Test", "{\"Message\":\"Log \\\"Test\\\"\",\"MessageTemplate\":\"Log {Token}\",\"Token\":\"Test\"}")]
    [InlineData("{Message:j}", "Log {Token}", "Test", "{\"Message\":\"Log \\\"Test\\\"\",\"MessageTemplate\":\"Log {Token}\",\"Token\":\"Test\"}")]
    [InlineData("{Message:l}", "Log {Token}", "Test", "{\"Message\":\"Log Test\",\"MessageTemplate\":\"Log {Token}\",\"Token\":\"Test\"}")]
    [InlineData(null, "Log {Token}", "{ \"Value\": \"Test\" }", "{\"Message\":\"Log \\\"{ \\\\\\\"Value\\\\\\\": \\\\\\\"Test\\\\\\\" }\\\"\",\"MessageTemplate\":\"Log {Token}\",\"Token\":\"{ \\\"Value\\\": \\\"Test\\\" }\"}")]
    [InlineData("{Message:j}", "Log {Token}", "{ \"Value\": \"Test\" }", "{\"Message\":\"Log \\\"{ \\\\\\\"Value\\\\\\\": \\\\\\\"Test\\\\\\\" }\\\"\",\"MessageTemplate\":\"Log {Token}\",\"Token\":\"{ \\\"Value\\\": \\\"Test\\\" }\"}")]
    [InlineData("{Message:l}", "Log {Token}", "{ \"Value\": \"Test\" }", "{\"Message\":\"Log { \\\"Value\\\": \\\"Test\\\" }\",\"MessageTemplate\":\"Log {Token}\",\"Token\":\"{ \\\"Value\\\": \\\"Test\\\" }\"}")]
    public void ShouldRenderCorrectMessage(string? outputTemplate, string messageTemplate, string property, string expected)
    {
        var formatter = new LokiJsonTextFormatter(outputTemplate);
        var parser = new MessageTemplateParser();
        var msgTemplate = parser.Parse(messageTemplate);
        var properties = new List<LogEventProperty> { new LogEventProperty("Token", new ScalarValue(property)) };
        var logEvent = new LogEvent(DateTimeOffset.Now, LogEventLevel.Information, null, msgTemplate, properties);

        var output = new StringWriter();
        formatter.Format(logEvent, output);

        output.ToString().ShouldBe(expected);
    }
}