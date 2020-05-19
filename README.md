# Serilog.Sinks.Grafana.Loki

[![Build status](https://github.com/mishamyte/serilog-sinks-grafana-loki/workflows/Build/badge.svg)](https://github.com/mishamyte/serilog-sinks-grafana-loki/actions?query=workflow%3ABuild)
[![NuGet Version](https://img.shields.io/nuget/v/Serilog.Sinks.Grafana.Loki)](https://www.nuget.org/packages/Serilog.Sinks.Grafana.Loki)
[![Documentation](https://img.shields.io/badge/docs-wiki-blueviolet.svg)](https://github.com/mishamyte/serilog-sinks-grafana-loki/wiki)

## Table of contents
- [What is this sink and Loki?](#what-is-this)
- [Features](#features)
- [Comparison with other Loki sinks](#comparison)
- [Quickstart](#quickstart)

## What is this sink and Loki?

The Serilog Grafana Loki sink project is a sink (basically a writer) for the Serilog logging framework. Structured log events are written to sinks and each sink is responsible for writing it to its own backend, database, store etc. This sink delivers the data to Grafana Loki, a horizontally-scalable, highly-available, multi-tenant log aggregation system. It allows you to use Grafana for visualizing your logs.

You can find more information about what Loki is over on [Grafana's website here](https://grafana.com/loki).

## Features:
- Formats and batches log entries to Loki via HTTP (using actual API)
- Global and contextual labels support
- Labels exclusion
- Integration with Serilog.Settings.Configuration
- Customizable HTTP client
- Durable mode
- Using fast System.Text.Json library for serialization

Coming soon:
- Improve durable mode (keep labels & etc)

## Comparison
Features comparison table could be found [here](https://github.com/mishamyte/serilog-sinks-grafana-loki/wiki/Comparison-with-another-Loki-sinks)

## Quickstart
The `Serilog.Sinks.Grafana.Loki` NuGet [package can be found here](https://www.nuget.org/packages/Serilog.Sinks.Grafana.Loki). Alternatively you can install it via one of the following commands below:

NuGet command:
```bash
Install-Package Serilog.Sinks.Grafana.Loki
```
.NET Core CLI:
```bash
dotnet add package Serilog.Sinks.Grafana.Loki
```

In the following example, the sink will send log events to Loki available on `http://localhost:3100`
```csharp
ILogger logger = new LoggerConfiguration()
    .WriteTo.GrafanaLoki(
        "http://localhost:3100")
    .CreateLogger();

logger.Information("The god of the day is {@God}", odin)
```

Used in conjunction with [Serilog.Settings.Configuration](https://github.com/serilog/serilog-settings-configuration) the same sink can be configured in the following way:

```json
{
  "Serilog": {
    "Using": [
      "Serilog.Sinks.Grafana.Loki"
    ],
    "MinimumLevel": {
      "Default": "Debug"
    },
    "WriteTo": [
      {
        "Name": "GrafanaLoki",
        "Args": {
          "url": "http://localhost:3100",
          "labels": [
            {
              "key": "app",
              "value": "web_app"
            }
          ],
          "outputTemplate": "{Timestamp:dd-MM-yyyy HH:mm:ss} [{Level:u3}] [{ThreadId}] {Message}{NewLine}{Exception}"
        }
      }
    ]
  }
}
```

Description of parameters and configuration details could be found [here](https://github.com/mishamyte/serilog-sinks-grafana-loki/wiki/Application-settings).


### Custom HTTP Client
Serilog.Loki.Grafana.Loki is built on top of the popular [Serilog.Sinks.Http](https://github.com/FantasticFiasco/serilog-sinks-http) library.
In order to use a custom HttpClient you can extend the default HttpClient (`Serilog.Sinks.Grafana.Loki.DefaultLokiHttpClient`), or create one implementing `Serilog.Sinks.Grafana.Loki.ILokiHttpClient` (which extends `Serilog.Sinks.Http.IHttpClient`).

```csharp
// CustomHttpClient.cs

public class CustomHttpClient : DefaultLokiHttpClient
{
    public override Task<HttpResponseMessage> PostAsync(string requestUri, HttpContent content)
    {
        return base.PostAsync(requestUri, content);
    }
}
```
```csharp
// Usage

Log.Logger = new LoggerConfiguration()
    .WriteTo.GrafanaLoki(
         "http://localhost:3100",
         httpClient: new CustomHttpClient()
    )
    .CreateLogger();
```

### Inspiration and Credits
- [Serilog.Sinks.Loki](https://github.com/JosephWoodward/Serilog-Sinks-Loki)
- [Serilog.Sinks.Http](https://github.com/FantasticFiasco/serilog-sinks-http)
