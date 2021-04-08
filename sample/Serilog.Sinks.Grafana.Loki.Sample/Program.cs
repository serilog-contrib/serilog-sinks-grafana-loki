using System;
using System.Collections.Generic;
using Serilog.Debugging;

namespace Serilog.Sinks.Grafana.Loki.Sample
{
    public static class Program
    {
        private const string OutputTemplate =
            "{Timestamp:dd-MM-yyyy HH:mm:ss} [{Level:u3}] [{ThreadId}] {Message}{NewLine}{Exception}";

        public static void Main(string[] args)
        {
            SelfLog.Enable(Console.Error);

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .Enrich.WithThreadId()
                .Enrich.WithProperty("meaning_of_life", "42")
                .WriteTo.Console(outputTemplate: OutputTemplate)
                .WriteTo.GrafanaLoki(
                    "http://localhost:3100",
                    new List<LokiLabel> {new() {Key = "app", Value = "console"}},
                    credentials: null,
                    outputTemplate: OutputTemplate)
                .CreateLogger();

            Log.Debug("This is a debug message");

            var person = new Person
            {
                Name = "Billy",
                Age = 42
            };

            Log.Information("Person of the day: {@Person}", person);

            try
            {
                throw new AccessViolationException("Access denied");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "An error occured");
            }

            Log.CloseAndFlush();
        }
    }
}