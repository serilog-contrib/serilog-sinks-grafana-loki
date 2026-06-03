# Serilog.Sinks.Grafana.Loki — V9 Specification

## Motivation

V8 carried forward several design decisions from its original fork that no longer hold up:
custom batching infrastructure that Serilog's own framework can replace, a mutable `LogEvent`
pipeline that causes subtle bugs under retry, an object-graph serializer that produces two full
in-memory copies of every payload, and a renaming strategy that caused more problems than it
solved. V9 is a ground-up rewrite that fixes these structurally while adding features that have
been explicitly deferred since V7/V8.

---

## Language & Runtime

| Decision | Choice | Rationale |
|---|---|---|
| Implementation language | **F#** | Functional-first, immutable pipelines, inline dispatch, compiler-enforced correctness |
| Public API style | **OOP / C#-compatible** | Extension methods, interfaces, `[<CLIMutable>]` records — callable from C# and F# identically |
| Target frameworks | **net8.0, net9.0, net10.0** | All prior TFMs are EOL; net8.0 is the current LTS baseline |
| Minimum Serilog | **4.x** | Current latest 4.3.1 — provides `IBatchedLogEventSink` support and `BatchingOptions` |
| Minimum Serilog.Sinks.PeriodicBatching | **5.x** | Separate NuGet; defines the `IBatchedLogEventSink` interface (latest: 5.0.0) |
| Minimum F# | **9** (ships with .NET 9) | `allows ref struct` generic constraints, `ValueOption` parameters |

`FSharp.Core` and `Serilog.Sinks.PeriodicBatching` become new transitive dependencies for all
consumers. Both are known, intentional consequences of the approach.

---

## Architecture Philosophy

The internal pipeline is written in idiomatic F#:

- **Immutable data** — `LogEvent` is never mutated. Label maps and rendered bodies are
  derived from the original event in a single pass.
- **Pure functions** — label computation, grouping, and serialization are all pure. Side
  effects (HTTP, clock) are pushed to the edges and injected.
- **`inline` for the hot path** — label key sanitisation, timestamp formatting, and value
  rendering are `inline` functions, enabling F# static dispatch and eliminating virtual
  call overhead per log event.
- **Discriminated unions** — log level mapping, label value kinds, and auth strategy are
  modelled as DUs internally; the corresponding public types are interfaces/enums for C#
  consumers.

The public contract is OOP:

- Configuration via a `[<CLIMutable>]` record (`LokiSinkOptions`) with sensible defaults,
  bindable from `appsettings.json` via `Serilog.Settings.Configuration`.
- Extension points exposed as interfaces (`ILokiExceptionFormatter`, `ILokiLabelFilter`).
- Entry point is a standard `LoggerSinkConfiguration` extension method decorated with
  `[<Extension>]`, callable identically from C# and F#. F# optional parameters (`?param`
  syntax) do **not** translate to C# optional parameters — extension method overloads are
  kept minimal (a `uri: string` convenience overload + a full `LokiSinkOptions` overload)
  to avoid needing `[<Optional; DefaultParameterValue>]` noise on every parameter.

---

## What Is Dropped

| Removed | Reason |
|---|---|
| `IReservedPropertyRenamingStrategy` | Root cause of issues #103, #273. Mutated `LogEvent` mid-pipeline. No valid use cases that can't be solved by enrichers before the sink. |
| `leavePropertiesIntact` flag | Was a workaround for #138. Disappears when the formatter pipeline is immutable. |
| `ILokiHttpClient` / `BaseLokiHttpClient` / `LokiHttpClient` / `LokiGzipHttpClient` | Replaced by direct `HttpClient` injection. Gzip becomes a `DelegatingHandler`. |
| `LokiBatch` / `LokiStream` / `LokiSerializationContext` | Object-graph model replaced by streaming `Utf8JsonWriter` pipeline. |
| `LokiBatchFormatter` (internal) | Replaced by F# inline pipeline. |
| `BoundedQueue` / `PortableTimer` / `ExponentialBackoffConnectionSchedule` | Replaced by `IBatchedLogEventSink` from Serilog 4.x. |
| `netstandard2.0`, `net5.0`, `net6.0`, `net7.0` targets | All EOL; `netstandard2.0` was already broken (source generator bug in `LokiSerializationContext`). |
| `useInternalTimestamp` + `InternalTimestamp` on `LokiLogEvent` | Niche feature with hidden side effects (injected `Timestamp` body property, non-injectable clock). Replaced by `TimeProvider` injection if clock override is needed. |

---

## What Is Added

### `IBatchedLogEventSink` (Serilog 4.x)

The entire custom batching stack is replaced by implementing `IBatchedLogEventSink`.
Serilog's framework provides:

- `IBatchedLogEventSink` is defined in `Serilog.Sinks.PeriodicBatching` (interface only).
  Serilog 4.x provides `WriteTo.Sink(IBatchedLogEventSink, BatchingOptions)` and owns the
  scheduling, backoff, and retry loop. `BatchingOptions` properties used by V9:

| `BatchingOptions` field | V9 default | Notes |
|---|---|---|
| `BatchSizeLimit` | 1 000 | Max events per POST |
| `BufferingTimeLimit` | 1 s | Flush interval (exposed as `Period` in `LokiSinkOptions`) |
| `EagerlyEmitFirstEvent` | `true` | Immediate flush on first event |
| `QueueLimit` | 50 000 | Bounded by default — **fixes #141** |
| `RetryTimeLimit` | 10 min | Exponential backoff up to 60 s; batch dropped after limit |

- Async `EmitBatchAsync` — no `GetAwaiter().GetResult()` disposal deadlock (fixes #305)
- On failure: exponential backoff (2×, 4×, 8× … capped at 60 s). Batch dropped after
  `RetryTimeLimit` expires. Queue drained after 10 consecutive dropped batches.

### Streaming serialization

The object graph + `JsonSerializer.Serialize()` → intermediate UTF-16 string → UTF-8 encode
chain is replaced by a single forward pass:

```
LogEvents
  → F# inline pipeline (labels, grouping, ordering)
  → Utf8JsonWriter → PooledByteBufferWriter (ArrayPool<byte>.Shared)
  → ReadOnlyMemory<byte> → LokiPushContent (HttpContent subclass)
  → HttpClient.SendAsync()
```

Key components:

| Component | Description |
|---|---|
| `PooledByteBufferWriter` | `IBufferWriter<byte>` backed by `ArrayPool<byte>.Shared`. Reused across ticks. |
| `Utf8TextWriter` | Custom `TextWriter` that writes UTF-8 bytes directly into the pooled buffer. Passed to `ITextFormatter`. Eliminates `StringWriter` + intermediate `string` per event. |
| `Utf8JsonWriter` | Drives the entire JSON structure in one forward pass. Property names pre-encoded as `JsonEncodedText` static fields. |
| `LokiPushContent` | `HttpContent` subclass. `SerializeToStreamAsync` writes pooled bytes directly into the HTTP stream. No second allocation for gzip — compression via `DelegatingHandler`. |

### TraceId and SpanId enrichment (fixes #233)

Serilog 4.x exposes `LogEvent.TraceId` (`ActivityTraceId?`) and `LogEvent.SpanId`
(`ActivitySpanId?`) natively, populated by `Serilog.Extensions.Hosting` /
`Serilog.Extensions.Logging` from the current `Activity`. No Serilog version bump is needed
beyond the 4.x minimum already required.

Both properties are marked `[CLSCompliant(false)]` on Serilog's side (those `Activity*`
types are not CLS-compliant). This has no runtime impact but the sink assembly should carry
`[assembly: CLSCompliant(false)]` or suppress the warning explicitly.

When `EnrichTraceId = true` / `EnrichSpanId = true` (both `false` by default), the values
are written as top-level fields in the JSON log line body.

### Pluggable exception formatter

```csharp
public interface ILokiExceptionFormatter
{
    void Format(Utf8JsonWriter writer, Exception exception);
}
```

Default implementation mirrors the current `SerializeException` behaviour (recursive,
`Type` / `Message` / `StackTrace` / `InnerException`, no depth limit). Users can replace it
for PII scrubbing, compact formats, or to suppress stack traces in production.

### `HandleLogLevelAsLabel` (replaces hardwired `level` injection)

When `true` (default), the Grafana log level string is added as a `level` stream label.
When `false`, it is omitted entirely. Removes the need for any collision-handling logic —
if users have a property named `level`, they simply set this to `false` and manage it
themselves.

`Fatal` maps to `"fatal"` (previously `"critical"` — aligns with Grafana's own level
vocabulary and resolves the outstanding `TODO: CHANGE` comment in
`LogEventLevelExtensions.cs`).

### `TimeProvider` injection

`LokiSinkOptions.TimeProvider` accepts any `TimeProvider` (default:
`TimeProvider.System`). Used anywhere the sink needs the current time. Makes timestamp
behaviour fully deterministic in tests.

### URI validation at startup (fixes #256)

The `uri` parameter is validated (scheme, host, parseable) when the sink is constructed.
An `ArgumentException` is thrown at logger configuration time, not on the first HTTP call.

### Tenant regex fix (fixes #202)

The `X-Scope-OrgID` validation regex is updated to match the actual Grafana Loki
multi-tenancy spec (alphanumeric string, no length limit beyond Go HTTP header limits).

---

## Public API Surface

### Configuration object

```fsharp
[<CLIMutable>]
type LokiSinkOptions = {
    // Required
    Uri: string

    // Labels
    Labels: LokiLabel[]                         // default: [||]
    PropertiesAsLabels: string[]                // default: [||]
    HandleLogLevelAsLabel: bool                 // default: true

    // Auth & routing
    Credentials: LokiCredentials option         // default: None  (Basic Auth)
    Tenant: string option                       // default: None

    // Tracing
    EnrichTraceId: bool                         // default: false
    EnrichSpanId: bool                          // default: false

    // Batching (maps to Serilog BatchingOptions)
    BatchSizeLimit: int                         // default: 1000
    QueueLimit: int                             // default: 50_000
    Period: TimeSpan                            // default: 1s
    EagerlyEmitFirstEvent: bool                 // default: true
    RetryTimeLimit: TimeSpan                    // default: 10min

    // Extension points
    TextFormatter: ITextFormatter               // default: LokiJsonTextFormatter
    ExceptionFormatter: ILokiExceptionFormatter // default: DefaultLokiExceptionFormatter
    HttpClient: HttpClient option               // default: None  (sink creates its own)
    TimeProvider: TimeProvider                  // default: TimeProvider.System
}
```

`LokiLabel` and `LokiCredentials` remain `[<CLIMutable>]` records for
`Serilog.Settings.Configuration` compatibility.

> **Validation note:** `[<CLIMutable>]` provides the parameterless constructor and settable
> properties that `Serilog.Settings.Configuration`'s reflection binder requires for nested
> object binding. The known reflection limitation in Serilog's settings binder (issue #1309)
> affects *extension method discovery* only, not record property binding. The `LokiSinkOptions`
> approach must be validated against real appsettings.json binding in integration tests before
> release.

### Extension method (C#-callable)

```csharp
// Minimal
.WriteTo.GrafanaLoki("http://localhost:3100")

// Full options object
.WriteTo.GrafanaLoki(new LokiSinkOptions {
    Uri = "http://localhost:3100",
    Labels = [new LokiLabel { Key = "app", Value = "my-service" }],
    PropertiesAsLabels = ["RequestPath"],
    Credentials = new LokiCredentials { Login = "user", Password = "pass" },
    Tenant = "my-tenant",
    EnrichTraceId = true,
    QueueLimit = 100_000,
    HttpClient = httpClientFactoryInstance.CreateClient("loki"),
})
```

### Formatter

`LokiJsonTextFormatter` remains public and non-sealed with `virtual` methods
(`Format`, `GetSanitizedPropertyName`). `SerializeException` is removed from the
formatter — exceptions are delegated to `ILokiExceptionFormatter`.

### HttpClient

The sink accepts an `HttpClient option`. When `None`, it creates its own with a 30-second
timeout. The sink **never disposes an injected `HttpClient`** — lifecycle is the caller's
responsibility. This makes `IHttpClientFactory` integration trivial:

```csharp
services.AddHttpClient("loki", c => c.BaseAddress = new Uri("http://localhost:3100"));
// ...
var httpClient = serviceProvider.GetRequiredService<IHttpClientFactory>()
                                .CreateClient("loki");
.WriteTo.GrafanaLoki(new LokiSinkOptions { Uri = "...", HttpClient = httpClient })
```

Gzip compression is a `DelegatingHandler` concern, not a subclass.

---

## Label Handling Changes

| Behaviour | V8 | V9 |
|---|---|---|
| `LogEvent` mutation | Yes — `AddOrUpdateProperty`, `RenamePropertyIfPresent` called in-place | No — labels derived from a read-only copy of `logEvent.Properties` |
| Property-as-label removal from body | Default (`leavePropertiesIntact = false`) | Properties used as labels are **kept in the body by default**. Deduplication is the formatter's concern, not the pipeline's. |
| Global label priority | Global wins silently | Global wins; `SelfLog` warning retained |
| Label key sanitisation | Numeric prefix → `param{key}` | Retained |
| Value sanitisation | `.Replace("\"", "")` — blunt | Render via `LogEventPropertyValue.ToString()` with proper quoting |
| `level` label | Always injected, collision-handled via renaming strategy | Controlled by `HandleLogLevelAsLabel`; no mutation required |
| `Fatal` level string | `"critical"` | `"fatal"` |

---

## Issue Resolution

| Issue | Resolution |
|---|---|
| #103 Message template not updated after renaming | Closed — `IReservedPropertyRenamingStrategy` dropped |
| #138 Property-as-label breaks message rendering | Closed — immutable pipeline; properties always available to formatter |
| #141 Unbounded queue → OOM | Closed — `queueLimit` default set to `50_000` |
| #147 Period default mismatch in XML doc | Closed — single source of truth in `LokiSinkOptions` |
| #202 Tenant regex rejects valid values | Closed — regex updated to Loki spec |
| #224 Thread-safety of `LogEvent` mutation | Closed — no mutation |
| #233 TraceId / SpanId support | Closed — `EnrichTraceId` / `EnrichSpanId` options |
| #256 URI not validated at startup | Closed — validated in sink constructor |
| #273 `level` re-added on retry | Closed — immutable pipeline; no accumulation possible |
| #305 Deadlock on dispose | Closed — `IBatchedLogEventSink.EmitBatchAsync` is fully async |

---

## What Is Not In Scope for V9

| Feature | Notes |
|---|---|
| Protobuf / Snappy wire format | Loki supports it; JSON is universal and sufficient. Add in V10 if perf data justifies. |
| Loki structured metadata (per-line key/value, Loki 2.x) | API design not settled. Issue #255 remains open; target V9.x. |
| Bearer token / OAuth2 auth | Can be done via `DelegatingHandler` on the injected `HttpClient`. Document the pattern; no first-class support needed. |

---

## Assumption Validation Status

| Assumption | Result | Notes |
|---|---|---|
| `IBatchedLogEventSink` in Serilog 4.x core | **Wrong** — separate package | Lives in `Serilog.Sinks.PeriodicBatching` 5.x; added as explicit dependency |
| `BatchingOptions.RetryTimeLimit` exists | **Confirmed** | Exponential backoff + drop after limit; 10 min default |
| `BufferingTimeLimit` vs `Period` naming | **Corrected** | Serilog calls it `BufferingTimeLimit`; exposed as `Period` in our `LokiSinkOptions` |
| `LogEvent.TraceId` / `SpanId` as `ActivityTraceId?` | **Confirmed** | Both nullable; both `[CLSCompliant(false)]` — noted |
| `Utf8JsonWriter` is a sealed class (not ref struct) | **Confirmed** | Fully usable from F#; cannot be subclassed (irrelevant — we use, not inherit) |
| `ArrayPool<byte>` / `IBufferWriter<byte>` in-box net8+ | **Confirmed** | `System.Buffers`; no NuGet needed |
| `TimeProvider` in-box net8+ | **Confirmed** | `System.TimeProvider`; `GetUtcNow()` returns `DateTimeOffset` |
| F# `?param` → C# optional params | **Wrong** | Needs `[<Optional; DefaultParameterValue>]`; mitigated by `LokiSinkOptions`-first API design |
| `[<CLIMutable>]` bindable by Settings.Configuration | **Likely yes** | Property binding should work; extension method discovery is a separate known issue; validate in integration tests |
| `HttpContent.SerializeToStreamAsync` override | **Confirmed** | `(Stream, TransportContext?, CancellationToken)` overload available net8+ |

---

## Files Deleted vs Created

### Deleted (V8 → V9)
```
src/.../Infrastructure/BoundedQueue.cs
src/.../Infrastructure/PortableTimer.cs
src/.../Infrastructure/ExponentialBackoffConnectionSchedule.cs
src/.../LokiSink.cs
src/.../LokiBatchFormatter.cs
src/.../ILokiBatchFormatter.cs
src/.../Models/LokiBatch.cs
src/.../Models/LokiStream.cs
src/.../Models/LokiSerializationContext.cs
src/.../Models/LokiLogEvent.cs          ← replaced by LokiEntry record
src/.../HttpClients/ILokiHttpClient.cs
src/.../HttpClients/BaseLokiHttpClient.cs
src/.../HttpClients/LokiHttpClient.cs
src/.../HttpClients/LokiGzipHttpClient.cs
src/.../IReservedPropertyRenamingStrategy.cs
src/.../DefaultReservedPropertyRenamingStrategy.cs
src/.../Utils/LogEventExtensions.cs     ← mutation helpers no longer needed
src/Serilog.Sinks.Grafana.Loki.V9/     ← spike folder, replaced by the real thing
```

### New project structure (F#)
```
src/Serilog.Sinks.Grafana.Loki/
  Serilog.Sinks.Grafana.Loki.fsproj

  -- Public contract (OOP surface) --
  LokiSinkOptions.fs           [<CLIMutable>] record + defaults
  LokiLabel.fs                 [<CLIMutable>] record
  LokiCredentials.fs           [<CLIMutable>] record
  ILokiExceptionFormatter.fs   interface
  LokiJsonTextFormatter.fs     public class (inheritable)
  LoggerConfigurationExtensions.fs  [<Extension>] GrafanaLoki(...)

  -- Internal pipeline (functional) --
  Labels.fs                    inline label derivation & sanitisation
  Grouping.fs                  stream grouping by label map
  Serialization.fs             Utf8JsonWriter pipeline
  LokiPushContent.fs           HttpContent subclass
  LokiSink.fs                  IBatchedLogEventSink implementation

  -- Shared infrastructure --
  PooledByteBufferWriter.fs    IBufferWriter<byte> over ArrayPool
  Utf8TextWriter.fs            TextWriter → UTF-8 bytes
```
