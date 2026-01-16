# Serilog.Sinks.Grafana.Loki

[![Made in Ukraine](https://img.shields.io/badge/made_in-ukraine-ffd700.svg?labelColor=0057b7)](https://stand-with-ukraine.pp.ua/)
[![Build status](https://github.com/mishamyte/serilog-sinks-grafana-loki/workflows/CI/badge.svg)](https://github.com/mishamyte/serilog-sinks-grafana-loki/actions?query=workflow%3ACI)
[![NuGet version](https://img.shields.io/nuget/v/Serilog.Sinks.Grafana.Loki)](https://www.nuget.org/packages/Serilog.Sinks.Grafana.Loki)
[![Latest release](https://img.shields.io/github/v/release/mishamyte/serilog-sinks-grafana-loki?include_prereleases)](https://github.com/mishamyte/serilog-sinks-grafana-loki/releases)
[![Documentation](https://img.shields.io/badge/docs-wiki-blueviolet.svg)](https://github.com/mishamyte/serilog-sinks-grafana-loki/wiki)

## Terms of use

By using this project or its source code, for any purpose and in any shape or form, you grant your **implicit agreement** to all the following statements:

- You **condemn Russia and its military aggression against Ukraine**
- You **recognize that Russia is an occupant that unlawfully invaded a sovereign state**
- You **support Ukraine's territorial integrity, including its claims over temporarily occupied territories of Crimea and Donbas**
- You **reject false narratives perpetuated by Russian state propaganda**

Glory to Ukraine! 🇺🇦

## Table of contents
- [What is this sink and Loki?](#what-is-this)
- [Features](#features)
- [Comparison with other Loki sinks](#comparison)
- [Breaking changes](#breaking-changes)
- [Quickstart](#quickstart)
- [Custom HTTP Client](#custom-http-client)
- [Sending json content to Loki](#sending-json-content-to-loki)
- [Structured Metadata](#structured-metadata)
- [Inspiration and Credits](#inspiration-and-credits)

## What is this sink and Loki?

The Serilog Grafana Loki sink project is a sink (basically a writer) for the Serilog logging framework. Structured log events are written to sinks and each sink is responsible for writing it to its own backend, database, store etc. This sink delivers the data to Grafana Loki, a horizontally-scalable, highly-available, multi-tenant log aggregation system. It allows you to use Grafana for visualizing your logs.

You can find more information about what Loki is over on [Grafana's website here](https://grafana.com/loki).

## Features:
- Formats and batches log entries to Loki via HTTP (using actual API)
- Global and contextual labels support
- Flexible Loki labels configuration possibilities
- Structured metadata support for enhanced log context
- Collision avoiding mechanism for labels
- Integration with Serilog.Settings.Configuration
- Customizable HTTP clients
- HTTP client with gzip compression
- Using fast System.Text.Json library for serialization
- Possibility of sending [json logs](https://grafana.com/blog/2020/10/28/loki-2.0-released-transform-logs-as-youre-querying-them-and-set-up-alerts-within-loki/) to Loki
- No dependencies on another sinks

## Comparison
Features comparison table could be found [here](https://github.com/mishamyte/serilog-sinks-grafana-loki/wiki/Comparison-with-another-Loki-sinks)

## Breaking changes
The list of breaking changes could be found [here](https://github.com/mishamyte/serilog-sinks-grafana-loki/wiki/Breaking-changes)

## Quickstart
The `Serilog.Sinks.Grafana.Loki` NuGet [package could be found here](https://www.nuget.org/packages/Serilog.Sinks.Grafana.Loki). Alternatively you can install it via one of the following commands below:

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
          "uri": "http://localhost:3100",
          "labels": [
            {
              "key": "app",
              "value": "web_app"
            }
          ],
          "propertiesAsLabels": [
            "app"
          ]
        }
      }
    ]
  }
}
```

Description of parameters and configuration details could be found [here](https://github.com/mishamyte/serilog-sinks-grafana-loki/wiki/Application-settings).

### Custom HTTP Client
Serilog.Loki.Grafana.Loki exposes `ILokiHttpClient` interface with the main operations, required for sending logs.
In order to use a custom HttpClient you can extend of default implementations:
- `Serilog.Sinks.Grafana.Loki.HttpClients.BaseLokiHttpClient` (implements creation of internal `HttpClient` and setting credentials)
- `Serilog.Sinks.Grafana.Loki.HttpClients.LokiHttpClient` (default client which sends logs via HTTP)
- `Serilog.Sinks.Grafana.Loki.HttpClients.LokiGzipHttpClient` (default client which sends logs via HTTP with gzip compression)
  
or create one implementing `Serilog.Sinks.Grafana.Loki.ILokiHttpClient`.

```csharp
// CustomHttpClient.cs

public class CustomHttpClient : BaseLokiHttpClient
{
    public override Task<HttpResponseMessage> PostAsync(string requestUri, Stream contentStream)
    {
        return base.PostAsync(requestUri, contentStream);
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

### Sending json content to Loki
From v8 Serilog.Sinks.Grafana.Loki uses `LokiJsonTextFormatter` by default, which allows to send logs to Loki as a JSON-payloads. This allows easier filtering in Loki v2, more information about how to filter can be found [here](https://grafana.com/blog/2020/10/28/loki-2.0-released-transform-logs-as-youre-querying-them-and-set-up-alerts-within-loki/)  

Also, you could implement your own formatter, implementing `Serilog.Formatting.ITextFormatter` interface and pass it to the sink configuration.

Example configuration:
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
          "uri": "http://localhost:3100",
          "textFormatter": "My.Awesome.Namespace.MyTextFormatter, MyCoolAssembly"
        }
      }
    ]
  }
}
```

### Structured Metadata

Loki supports [structured metadata](https://grafana.com/docs/loki/latest/reference/loki-http-api/#ingest-logs) - additional key-value pairs that can be attached to each log line without being indexed as labels. This is useful for high-cardinality data like trace IDs, user IDs, or request IDs that you want to query but don't want to use as labels.

> **Requirements:**
> - Structured metadata requires **Loki 1.2.0 or newer**
> - You must [enable structured metadata](https://grafana.com/docs/loki/latest/get-started/labels/structured-metadata/#enable-or-disable-structured-metadata) in your Loki configuration by setting `allow_structured_metadata: true` in `limits_config` (enabled by default in Loki 3.0+)
>
> Example Loki configuration:
> ```yaml
> limits_config:
>   allow_structured_metadata: true
> ```

You can configure which log event properties should be extracted as structured metadata using the `propertiesAsStructuredMetadata` parameter:

```csharp
Log.Logger = new LoggerConfiguration()
    .WriteTo.GrafanaLoki(
        "http://localhost:3100",
        propertiesAsStructuredMetadata: new[] { "trace_id", "user_id", "request_id" })
    .CreateLogger();

Log.Information("User {user_id} performed action with trace {trace_id}", "user123", "abc-xyz-123");
```

This produces a log entry with structured metadata attached:
```json
{
  "streams": [{
    "stream": {},
    "values": [
      ["1234567890000000000", "{\"Message\":\"...\",\"level\":\"info\"}", {"trace_id":"abc-xyz-123","user_id":"user123"}]
    ]
  }]
}
```

By default, properties extracted as structured metadata are removed from the log line JSON to avoid duplication. To keep them in both places, set `leaveStructuredMetadataPropertiesIntact` to `true`:

```csharp
Log.Logger = new LoggerConfiguration()
    .WriteTo.GrafanaLoki(
        "http://localhost:3100",
        propertiesAsStructuredMetadata: new[] { "trace_id", "user_id" },
        leaveStructuredMetadataPropertiesIntact: true)
    .CreateLogger();
```

Configuration via `appsettings.json`:

```json
{
  "Serilog": {
    "Using": ["Serilog.Sinks.Grafana.Loki"],
    "WriteTo": [
      {
        "Name": "GrafanaLoki",
        "Args": {
          "uri": "http://localhost:3100",
          "propertiesAsStructuredMetadata": ["trace_id", "user_id", "request_id"],
          "leaveStructuredMetadataPropertiesIntact": false
        }
      }
    ]
  }
}
```

For more details on all configuration parameters, see the [Application Settings wiki page](https://github.com/mishamyte/serilog-sinks-grafana-loki/wiki/Application-settings).

### Inspiration and Credits
- [Serilog.Sinks.Loki](https://github.com/JosephWoodward/Serilog-Sinks-Loki)
