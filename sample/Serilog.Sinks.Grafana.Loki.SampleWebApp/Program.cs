using System;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog.Debugging;

namespace Serilog.Sinks.Grafana.Loki.SampleWebApp
{
    public static class Program
    {
        public static void Main(string[] args)
        {
            SelfLog.Enable(Console.Error);

            CreateHostBuilder(args).Build().Run();
        }

        private static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureLogging((ctx, cfg) => cfg.ClearProviders())
                .UseSerilog((ctx, cfg) => cfg.ReadFrom.Configuration(ctx.Configuration))
                .ConfigureWebHostDefaults(webBuilder => { webBuilder.UseStartup<Startup>(); });
    }
}