# Serilog.Sinks.Grafana.Loki

[![Made in Ukraine](https://img.shields.io/badge/made_in-ukraine-ffd700.svg?labelColor=0057b7)](https://stand-with-ukraine.pp.ua/)
[![Build status](https://github.com/serilog-contrib/serilog-sinks-grafana-loki/workflows/CI/badge.svg)](https://github.com/serilog-contrib/serilog-sinks-grafana-loki/actions?query=workflow%3ACI)
[![NuGet version](https://img.shields.io/nuget/v/Serilog.Sinks.Grafana.Loki)](https://www.nuget.org/packages/Serilog.Sinks.Grafana.Loki)
[![Latest release](https://img.shields.io/github/v/release/serilog-contrib/serilog-sinks-grafana-loki?include_prereleases)](https://github.com/serilog-contrib/serilog-sinks-grafana-loki/releases)
[![Documentation](https://img.shields.io/badge/docs-wiki-blueviolet.svg)](https://github.com/serilog-contrib/serilog-sinks-grafana-loki/wiki)

## Terms of use

By using this project or its source code, for any purpose and in any shape or form, you grant your **implicit agreement
** to all the following statements:

- You **condemn Russia and its military aggression against Ukraine**
- You **recognize that Russia is an occupant that unlawfully invaded a sovereign state**
- You **support Ukraine's territorial integrity, including its claims over temporarily occupied territories of Crimea
  and Donbas**
- You **reject false narratives perpetuated by Russian state propaganda**

Glory to Ukraine! 🇺🇦

## Table of contents

- [What is this sink and Loki?](#what-is-this)
- [What's new in V9](#whats-new-in-v9)
- [Requirements](#requirements)
- [Features](#features)
- [Quickstart](#quickstart)
- [Configuration reference](#configuration-reference)
- [Labels](#labels)
- [Authentication and multi-tenancy](#authentication-and-multi-tenancy)
- [Custom HttpClient](#custom-http-client)
- [Trace and span enrichment](#trace-and-span-enrichment)
- [JSON formatting and custom formatters](#json-formatting)
- [Batching and delivery](#batching-and-delivery)
- [Migrating from V8](#migrating-from-v8)
- [Samples](#samples)
- [Inspiration and Credits](#inspiration-and-credits)

## <a id="what-is-this"></a>What is this sink and Loki?

The Serilog Grafana Loki sink project is a sink (basically a writer) for the Serilog logging framework. Structured log
events are written to sinks and each sink is responsible for writing it to its own backend, database, store etc. This
sink delivers the data to Grafana Loki, a horizontally-scalable, highly-available, multi-tenant log aggregation system.
It allows you to use Grafana for visualizing your logs.

You can find more information about what Loki is over on [Grafana's website here](https://grafana.com/loki).

## <a id="whats-new-in-v9"></a>What's new in V9

V9 is a ground-up rewrite of the sink in **F#**, keeping a public API that remains idiomatic to call from **C#**. The
rewrite fixes a class of long-standing structural bugs and modernizes the delivery pipeline:

- **Built on Serilog 4.x native batching.** The custom queue/timer/backoff stack is gone. Delivery now uses Serilog's
  `IBatchedLogEventSink`, giving a **bounded queue by default (50 000 events)**, async emission, and retry with
  exponential backoff — no more dropped batches on failure and no dispose-time deadlocks.
- **Immutable log-event pipeline.** Labels are derived from a read-only view of the event. Properties promoted to labels
  now **also remain in the log body**, and the message template is never corrupted on retry.
- **Streaming serialization.** Payloads are written in a single forward pass with `Utf8JsonWriter` over pooled buffers —
  no intermediate object graph and no intermediate strings.
- **Bring-your-own `HttpClient` / `HttpMessageHandler.`** The old `ILokiHttpClient`/`LokiGzipHttpClient` hierarchy is
  replaced by direct injection; gzip, retries, mTLS and bearer auth are now standard `DelegatingHandler` concerns.
- **New features:** pluggable exception formatter (`ILokiExceptionFormatter`), `TraceId`/`SpanId` enrichment, startup
  URI validation, and the log level exposed as a label using Grafana's vocabulary.

See [Migrating from V8](#migrating-from-v8) for the full list of breaking changes.

## <a id="requirements"></a>Requirements

- **.NET:** `net8.0`, `net9.0`, or `net10.0` (earlier target frameworks are EOL and no longer supported).
- **Serilog:** `4.3.1` or later.
- **Transitive dependency:** `FSharp.Core` is pulled in automatically. No other sink packages are required.

## <a id="features"></a>Features

- Batches and ships structured log events to Loki over its HTTP push API
- Serilog 4.x native batching: bounded in-memory queue, retry with exponential backoff, fully async emission
- Streaming `System.Text.Json` serialization with pooled buffers (no intermediate object graph or strings)
- Global and property-derived labels with deterministic stream grouping and key sanitisation
- Log level exposed as a label using Grafana's level vocabulary (optional)
- Basic authentication and multi-tenancy (`X-Scope-OrgID`) support
- Bring-your-own `HttpClient` / `HttpMessageHandler` — gzip, retries, mTLS and bearer auth via `DelegatingHandler`
- `TraceId` / `SpanId` enrichment from the ambient `Activity` (OpenTelemetry)
- Pluggable exception formatter and text formatter
- First-class `Serilog.Settings.Configuration` (appsettings.json) support
- No dependency on any other Serilog sink

## <a id="quickstart"></a>Quickstart

The `Serilog.Sinks.Grafana.Loki`
NuGet [package can be found here](https://www.nuget.org/packages/Serilog.Sinks.Grafana.Loki). Install it via one of the
following commands:

NuGet command:

```bash
Install-Package Serilog.Sinks.Grafana.Loki
```

.NET CLI:

```bash
dotnet add package Serilog.Sinks.Grafana.Loki
```

In the following example, the sink will send log events to Loki available on `http://localhost:3100`:

```csharp
using Serilog;
using Serilog.Sinks.Grafana.Loki;

Log.Logger = new LoggerConfiguration()
    .WriteTo.GrafanaLoki(
        "http://localhost:3100",
        [new LokiLabel { Key = "app", Value = "web_app" }])
    .CreateLogger();

Log.Information("The god of the day is {@God}", odin);
```

> The sink posts to `<uri>/loki/api/v1/push`. The push path is appended automatically — pass only the base address
> (a path prefix such as `http://gateway/loki` is supported and preserved).

Used together with [Serilog.Settings.Configuration](https://github.com/serilog/serilog-settings-configuration), the same
sink can be configured from `appsettings.json`:

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

## <a id="configuration-reference"></a>Configuration reference

All options are passed as named arguments to `WriteTo.GrafanaLoki(...)`. Only `uri` is required.

| Parameter | Type | Default | Description |
|---|---|---|---|
| `uri` | `string` | — (required) | Loki base URI, e.g. `http://localhost:3100`. Validated at startup; must be an absolute `http`/`https` URI. |
| `labels` | `LokiLabel[]` | `[]` | Static labels attached to every stream. |
| `propertiesAsLabels` | `string[]` | `[]` | Log-event property names to promote to stream labels. |
| `handleLogLevelAsLabel` | `bool` | `true` | Add a `level` label using Grafana's level vocabulary. |
| `credentialsLogin` | `string` | `null` | Basic-auth username. Pair with `credentialsPassword`. |
| `credentialsPassword` | `string` | `null` | Basic-auth password. |
| `tenant` | `string` | `null` | Value for the `X-Scope-OrgID` multi-tenancy header. |
| `enrichTraceId` | `bool` | `false` | Write the event's `TraceId` as a field in the JSON body. |
| `enrichSpanId` | `bool` | `false` | Write the event's `SpanId` as a field in the JSON body. |
| `batchSizeLimit` | `int` | `1000` | Maximum events per HTTP POST. |
| `queueLimit` | `int` | `50000` | Maximum events buffered in memory before new events are dropped. |
| `period` | `TimeSpan?` | `1 s` | Flush interval. From `appsettings.json`, written as an `"hh:mm:ss"` string. |
| `eagerlyEmitFirstEvent` | `bool` | `true` | Flush immediately on the first event (surfaces misconfiguration early). |
| `retryTimeLimit` | `TimeSpan?` | `10 min` | Stop retrying a failed batch after this duration. From `appsettings.json`, an `"hh:mm:ss"` string. |
| `textFormatter` | `ITextFormatter` | `LokiJsonTextFormatter` | Per-event body formatter. |
| `exceptionFormatter` | `ILokiExceptionFormatter` | `LokiExceptionFormatter` | Exception serializer. |
| `httpClient` | `HttpClient` | `null` | Pre-built client (e.g. from `IHttpClientFactory`). The sink never disposes an injected client. |
| `httpMessageHandler` | `HttpMessageHandler` | `null` | Handler for the sink's own client (gzip, retries, …). Ignored when `httpClient` is set. |
| `restrictedToMinimumLevel` | `LogEventLevel` | `Verbose` | Minimum level handled by this sink. |

> **Note on `period` / `retryTimeLimit`:** in C# these are `TimeSpan?` (e.g. `period: TimeSpan.FromSeconds(5)`); leave
> them unset to use the defaults. In `appsettings.json` they are written as `"hh:mm:ss"` strings (e.g. `"00:00:05"`),
> which `Serilog.Settings.Configuration` converts to `TimeSpan`.

A more complete C# example:

```csharp
Log.Logger = new LoggerConfiguration()
    .WriteTo.GrafanaLoki(
        "http://localhost:3100",
        labels: [new LokiLabel { Key = "app", Value = "my-service" }],
        propertiesAsLabels: ["RequestPath"],
        credentialsLogin: "user",
        credentialsPassword: "pass",
        tenant: "my-tenant",
        enrichTraceId: true,
        queueLimit: 100_000,
        period: TimeSpan.FromSeconds(2))
    .CreateLogger();
```

Configuration details for `appsettings.json` are also documented
[in the wiki](https://github.com/serilog-contrib/serilog-sinks-grafana-loki/wiki/Application-settings).

## <a id="labels"></a>Labels

Each log event is mapped to a Loki **stream** identified by its label set. Events that resolve to the same label set are
grouped into one stream and ordered by timestamp.

Labels come from three sources, in descending priority:

1. **Global labels** — from the `labels` option, attached to every stream.
2. **The `level` label** — added when `handleLogLevelAsLabel` is `true` (the default).
3. **Property-derived labels** — from `propertiesAsLabels`.

When keys collide, a higher-priority source wins; a property is silently skipped if its key matches a global label or the
reserved `level` key. Unlike V8, properties promoted to labels are **kept in the log body as well** — promotion no longer
removes them from the event.

Other rules:

- **Key sanitisation:** label keys must begin with a letter or underscore. Keys that start with a digit (for example
  positional template tokens like `{0}`) are prefixed with `param`, becoming `param0`.
- **Level vocabulary:** Serilog levels map to Grafana's level names — `Verbose → trace`, `Debug → debug`,
  `Information → info`, `Warning → warning`, `Error → error`, `Fatal → fatal` (previously `critical`).

## <a id="authentication-and-multi-tenancy"></a>Authentication and multi-tenancy

**Basic authentication** is configured with `credentialsLogin` / `credentialsPassword`:

```csharp
.WriteTo.GrafanaLoki(
    "http://localhost:3100",
    credentialsLogin: "user",
    credentialsPassword: "pass")
```

> Basic auth is applied only to a client the sink creates. If you inject your own `httpClient`, configure its
> `Authorization` header yourself — the sink never mutates an injected client.

**Multi-tenancy** is configured with `tenant`, which sets the `X-Scope-OrgID` header:

```csharp
.WriteTo.GrafanaLoki("http://localhost:3100", tenant: "tenant-1")
```

**Bearer tokens / OAuth2** are not a first-class option — add them through the injected client, either by setting a
default `Authorization` header on an `HttpClient` or via a `DelegatingHandler` (see below).

## <a id="custom-http-client"></a>Custom HttpClient

V9 accepts a standard `HttpClient` or `HttpMessageHandler` directly. Inject your own to add gzip compression, retries,
mTLS, bearer auth, or any other cross-cutting behaviour:

```csharp
// GzipHandler.cs
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;

public class GzipHandler : DelegatingHandler
{
    public GzipHandler() : base(new HttpClientHandler()) { }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct)
    {
        if (request.Content is not null)
        {
            var bytes = await request.Content.ReadAsByteArrayAsync(ct);
            using var ms = new MemoryStream();
            using (var gz = new GZipStream(ms, CompressionLevel.Fastest, leaveOpen: true))
                await gz.WriteAsync(bytes, ct);
            request.Content = new ByteArrayContent(ms.ToArray());
            request.Content.Headers.ContentType =
                new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
            request.Content.Headers.ContentEncoding.Add("gzip");
        }
        return await base.SendAsync(request, ct);
    }
}
```

```csharp
// Inject via httpMessageHandler — the sink creates and owns the HttpClient
// (and still applies basic auth / tenant headers).
Log.Logger = new LoggerConfiguration()
    .WriteTo.GrafanaLoki(
        "http://localhost:3100",
        httpMessageHandler: new GzipHandler())
    .CreateLogger();

// Or inject a pre-built HttpClient (e.g. from IHttpClientFactory — the sink never disposes it).
var httpClient = httpClientFactory.CreateClient("loki");
Log.Logger = new LoggerConfiguration()
    .WriteTo.GrafanaLoki(
        "http://localhost:3100",
        httpClient: httpClient)
    .CreateLogger();
```

## <a id="trace-and-span-enrichment"></a>Trace and span enrichment

When `enrichTraceId` / `enrichSpanId` are enabled, the event's `TraceId` and `SpanId` (populated by Serilog 4.x from the
ambient `Activity`) are written as `TraceId` / `SpanId` fields in the JSON body:

```csharp
.WriteTo.GrafanaLoki(
    "http://localhost:3100",
    enrichTraceId: true,
    enrichSpanId: true)
```

Both default to `false`. Trace context is typically populated for you by `Serilog.AspNetCore` /
`Serilog.Extensions.Logging`; outside those, an active `Activity` must be present for the fields to be emitted.

## <a id="json-formatting"></a>JSON formatting and custom formatters

By default the sink uses `LokiJsonTextFormatter`, which renders each log entry's body as a JSON object. This makes logs
easy to filter in Loki — see Grafana's write-up on
[querying JSON logs](https://grafana.com/blog/2020/10/28/loki-2.0-released-transform-logs-as-youre-querying-them-and-set-up-alerts-within-loki/).

The resulting push payload looks like:

```json
{
  "streams": [
    {
      "stream": { "app": "web_app", "level": "info" },
      "values": [
        [ "1700000000000000000", "{\"Message\":\"...\",\"MessageTemplate\":\"...\"}" ]
      ]
    }
  ]
}
```

Each body object contains `Message`, `MessageTemplate`, an optional `Exception`, the optional `TraceId` / `SpanId`
fields, and every event property. Property names that collide with these reserved keys (`Message`, `MessageTemplate`,
`Exception`, `TraceId`, `SpanId`) are prefixed with `_`.

**Custom text formatter.** Implement `Serilog.Formatting.ITextFormatter`, or subclass `LokiJsonTextFormatter` and
override `Format` or `SanitizePropertyName`, then pass it via `textFormatter`:

```json
{
  "Serilog": {
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

**Custom exception formatter.** Exception serialization is delegated to `ILokiExceptionFormatter`. The default
(`LokiExceptionFormatter`) recursively writes `Type`, `Message`, `Source`, `StackTrace` and inner exceptions. Replace it
to scrub PII, change the shape, or suppress stack traces:

```csharp
using System.Text.Json;
using Serilog.Sinks.Grafana.Loki;

public class CompactExceptionFormatter : ILokiExceptionFormatter
{
    public void Format(Utf8JsonWriter writer, Exception exception)
    {
        writer.WriteStartObject();
        writer.WriteString("type", exception.GetType().Name);
        writer.WriteString("message", exception.Message);
        writer.WriteEndObject();
    }
}
```

```csharp
.WriteTo.GrafanaLoki(
    "http://localhost:3100",
    exceptionFormatter: new CompactExceptionFormatter())
```

## <a id="batching-and-delivery"></a>Batching and delivery

Delivery is handled by Serilog 4.x's native batching infrastructure:

- Events are buffered and flushed every `period` (default 1 s) or once `batchSizeLimit` (default 1000) is reached.
- The in-memory queue is **bounded** at `queueLimit` (default 50 000). When the queue is full, new events are dropped
  rather than growing memory without limit.
- On a failed POST, the batch is retried with exponential backoff up to `retryTimeLimit` (default 10 min), after which it
  is dropped so the pipeline can make progress.
- Emission is fully asynchronous, so disposing the logger (`Log.CloseAndFlush()`) flushes cleanly without deadlocks.

Delivery problems are reported through Serilog's `SelfLog`. Enable it during development to see HTTP errors and dropped
batches:

```csharp
Serilog.Debugging.SelfLog.Enable(Console.Error);
```

## <a id="migrating-from-v8"></a>Migrating from V8

V9 is a major release with breaking changes. The highlights:

| Area | V8 | V9 |
|---|---|---|
| Target frameworks | `netstandard2.0`, `net5.0`–`net8.0` | `net8.0`, `net9.0`, `net10.0` |
| Serilog | 2.x / 3.x | **4.3.1+** (native batching) |
| HTTP client | `ILokiHttpClient` / `LokiGzipHttpClient` subclasses | Inject `httpClient` / `httpMessageHandler`; gzip via `DelegatingHandler` |
| Credentials | `credentials: LokiCredentials` | `credentialsLogin` / `credentialsPassword` |
| Level label | always injected, collisions renamed | `handleLogLevelAsLabel` (default `true`); `Fatal` → `fatal` (was `critical`) |
| Property → label | removed the property from the body | property is **kept** in the body |
| Reserved-property renaming | `IReservedPropertyRenamingStrategy` | removed — pipeline is immutable; reserved body keys are prefixed with `_` |
| `leavePropertiesIntact` | flag | removed — no longer needed |
| `useInternalTimestamp` | flag | removed |
| Queue | unbounded by default | **bounded** at `queueLimit` (default 50 000) |
| Exception formatting | hardcoded | pluggable `ILokiExceptionFormatter` |
| Trace context | not supported | `enrichTraceId` / `enrichSpanId` |
| URI validation | on first request | at logger configuration time |

`FSharp.Core` becomes a transitive dependency of all consumers.

The full, maintained list of breaking changes lives in the
[wiki](https://github.com/serilog-contrib/serilog-sinks-grafana-loki/wiki/Breaking-changes).

## <a id="samples"></a>Samples

Runnable examples live in the [`samples`](samples) folder:

- **`Serilog.Sinks.Grafana.Loki.Sample`** — a minimal console app.
- **`Serilog.Sinks.Grafana.Loki.SampleWebApp`** — an ASP.NET Core app configured from `appsettings.json`.

## <a id="inspiration-and-credits"></a>Inspiration and Credits

- [Serilog.Sinks.Loki](https://github.com/JosephWoodward/Serilog-Sinks-Loki)
