using Serilog;
using Serilog.Debugging;
using Serilog.Sinks.Grafana.Loki;

const string outputTemplate =
    "{Timestamp:dd-MM-yyyy HH:mm:ss} [{Level:u3}] [{ThreadId}] {Message}{NewLine}{Exception}";

SelfLog.Enable(Console.Error);

// C# must start from LokiSinkOptions.Defaults — [<CLIMutable>] F# records
// zero-initialise unset fields when constructed with `new`, so BatchSizeLimit
// etc. would be 0. Mutate the defaults instance to override only what you need.
var lokiOpts = LokiSinkOptions.Defaults;
lokiOpts.Uri = "http://localhost:3100";
lokiOpts.Labels = [new LokiLabel { Key = "app", Value = "console" }];
lokiOpts.PropertiesAsLabels = ["ThreadId"];

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .Enrich.WithThreadId()
    .Enrich.WithProperty("meaning_of_life", 42)
    .WriteTo.Console(outputTemplate: outputTemplate)
    .WriteTo.GrafanaLoki(lokiOpts)
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