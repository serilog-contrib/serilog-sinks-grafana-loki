using Serilog;
using Serilog.Debugging;
using Serilog.Sinks.Grafana.Loki;

const string outputTemplate =
    "{Timestamp:dd-MM-yyyy HH:mm:ss} [{Level:u3}] [{ThreadId}] {Message}{NewLine}{Exception}";

SelfLog.Enable(Console.Error);

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .Enrich.WithThreadId()
    .Enrich.WithProperty("meaning_of_life", 42)
    .WriteTo.Console(outputTemplate: outputTemplate)
    .WriteTo.GrafanaLoki(new LokiSinkOptions
    {
        Uri = "http://localhost:3100",
        Labels = [new LokiLabel { Key = "app", Value = "console" }],
        PropertiesAsLabels = ["ThreadId"]
    })
    .CreateLogger();

Log.Debug("This is a debug message");

var person = new Person("Billy", 42);
Log.Information("Person of the day: {@Person}", person);

try
{
    throw new AccessViolationException("Access denied");
}
catch (Exception ex)
{
    Log.Error(ex, "An error occurred");
}

Log.CloseAndFlush();

record Person(string Name, int Age);
