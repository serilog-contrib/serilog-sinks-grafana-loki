# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

A Serilog sink that ships log events to Grafana Loki. V9 is a ground-up **F#** rewrite (`src/`) exposing a **C#-idiomatic public API**. Multi-targets `net8.0`/`net9.0`/`net10.0`, depends only on `Serilog` (4.3.1+) and `FSharp.Core`.

## Commands

The FAKE build script (`build.fsx`, run via `dotnet fsi`) is the canonical entry point and works cross-platform:

```sh
dotnet fsi build.fsx                              # Default: Restore → Build → Test → Pack
dotnet fsi build.fsx -- --target Test             # unit tests (after Build)
dotnet fsi build.fsx -- --target IntegrationTest  # requires a Docker daemon (Testcontainers)
dotnet fsi build.fsx -- --target Pack             # nupkg → ./artifacts
dotnet fsi build.fsx -- --target Benchmark        # BenchmarkDotNet suites; BENCH_FILTER='*Sink*' to filter
```

Direct `dotnet` equivalents (Release is what CI uses):

```sh
dotnet build Serilog.Sinks.Grafana.Loki.slnx -c Release
dotnet test tests/Serilog.Sinks.Grafana.Loki.UnitTests -c Release
dotnet test tests/Serilog.Sinks.Grafana.Loki.UnitTests --filter "FullyQualifiedName~WireFormat"   # single test/class
dotnet test tests/Serilog.Sinks.Grafana.Loki.UnitTests -f net10.0                                 # single TFM (unit tests run on all 3)
```

Formatting — Fantomas (local dotnet tool, config in `.editorconfig`):

```sh
dotnet tool restore
dotnet fantomas src tests benchmarks build.fsx          # format
dotnet fantomas --check src tests benchmarks build.fsx  # verify
```

A Husky pre-commit hook auto-formats staged `.fs`/`.fsx` under `src/`, `tests/`, `benchmarks/`, and `build.fsx`. Hooks self-install during restore via `Directory.Build.targets`; set `HUSKY=0` to opt out (CI does).

Local Loki + Grafana for manual testing: `docker compose up -d loki` (Loki on :3100, Grafana on :3000).

## Architecture

### F# compilation order is the dependency graph

`src/Serilog.Sinks.Grafana.Loki/Serilog.Sinks.Grafana.Loki.fsproj` lists `<Compile>` items dependencies-first; a file can only reference what is listed above it. New files must be inserted at the right layer:

1. **Infrastructure** (no Serilog dependency): `PooledByteBufferWriter`, `Utf8TextWriter` — pooled-buffer primitives.
2. **Public contract types**: `LokiLabel`, `LokiCredentials`, `LokiFieldDestination`, `ILokiExceptionFormatter`, `LokiExceptionFormatter`, `LokiJsonTextFormatter`, `LokiSinkOptions`.
3. **Internal functional pipeline** (`[<AutoOpen>]` modules): `Labels` (sanitisation, label-set building), `Grouping` (stream identity via `LabelEqualityComparer`), `Serialization` (batch → Loki push JSON), `LokiPushContent` (HttpContent over the pooled buffer).
4. **Sink wiring**: `LokiSink`.
5. **Public API surface**: `LoggerConfigurationExtensions` — a single flat-parameter `GrafanaLoki` extension method (no options-object overload; signature is bound by `Serilog.Settings.Configuration` from appsettings.json).

### Data flow

`GrafanaLoki(...)` builds a `LokiSinkOptions`, validates the URI at startup, and registers `LokiSink` through **Serilog 4.x native batching** (`IBatchedLogEventSink` — batching, bounded queue, retry/backoff all live in Serilog core; there is no custom queue/timer). Per batch, `EmitBatchAsync`: groups events by label set → streams JSON in one forward pass with `Utf8JsonWriter` over reusable pooled buffers (`SerializationBuffers`, owned by the sink, used serially) → POSTs via `LokiPushContent` to `loki/api/v1/push`. No intermediate object graph or strings — keep it that way; allocation regressions are what the benchmark CI watches for.

### Design rules

- **Immutable pipeline**: never mutate a `LogEvent` (the V8 mutable pipeline was the root cause of several long-standing bugs). Labels are derived from a read-only view; promoted properties stay in the body.
- **Functional-first internals, OOP surface**: internals use modules/DUs/inline functions; everything public must be natural to call from C# (classes, interfaces, `[<Extension>]` methods, null-tolerant — guard inputs with `isNull`, including `box`-coercion for records).
- **HttpClient ownership**: auth headers and the tenant header are applied only to a client the sink created; an injected `HttpClient` is never mutated (gzip/mTLS/retries are the caller's `DelegatingHandler` concern).
- **Field routing**: `LokiFieldDestination` sends `TraceId`/`SpanId` to the body, structured metadata, or nowhere. Structured metadata (the optional 3rd element of a push entry) is emitted only when non-empty, so default output stays byte-identical and pre-3.0-Loki-safe.
- Errors surface via Serilog's `SelfLog` and rethrow — no silent drops.

### Tests

- **Unit tests** (`tests/...UnitTests`, xUnit + **Unquote** `test <@ ... @>` assertions, all 3 TFMs): wire-format tests assert on exact serialized JSON captured via fake handlers/loopback; `AppSettingsBindingTests` builds a logger from JSON config to pin the appsettings contract; `ExtensionDefaultsTests` guards default-value drift.
- **Integration tests** (`tests/...IntegrationTests`, net10.0 only): Testcontainers spins up a real Loki and queries pushed entries back. Fail fast without Docker — no skip logic. Not part of the FAKE `Default` chain.
- **Benchmarks** (`benchmarks/`): three BenchmarkDotNet executables — V9 (ProjectReference), V8 baseline (NuGet pin via `BaselineVersion`/`VersionOverride`), and Serilog.Sinks.Loki.YetAnother (yardstick) — sharing sources from `benchmarks/Shared/`. Deliberately **not in the .slnx** (each pins a conflicting Serilog/sink closure with the same assembly name). `Allocated` is the deterministic CI regression signal; `Mean` is noisy. `compare-results.fsx` diffs two result dirs (used by `.github/workflows/benchmark.yml` for PR comparisons against the latest NuGet version).

## Conventions and gotchas

- **`TreatWarningsAsErrors` + `WarnLevel` 5** apply repo-wide (`Directory.Build.props`).
- **Central Package Management** (`Directory.Packages.props`): no `Version=` on `PackageReference`s. The only sanctioned exception is `VersionOverride` in the benchmark baseline projects.
- **Versioning is MinVer** from git tags (`v` prefix, e.g. `v9.0.0`) — never hand-edit a `<Version>`. CI checkout needs `fetch-depth: 0`.
- The library sets `DisableImplicitFSharpCoreReference` and references `FSharp.Core` as an explicit NuGet package — required so FSharp.Core flows transitively to C# consumers. Don't remove either side.
- Packaging: strong-named, embedded PDB + SourceLink (`DebugType=embedded`), deterministic in CI.
- Formatting: 4-space indent, LF, BOM-less UTF-8, `insert_final_newline = false`; F# uses Fantomas with `fsharp_multiline_bracket_style = aligned` and `max_line_length = 120`.
- `build.fsx` disables MSBuild's internal binlog (`noBinLog`) — FAKE 6.1.4 can't parse .NET 10's binlog v25; keep the workaround on new MSBuild-backed targets.
- CI (`.github/workflows/ci.yaml`) runs build/unit/integration on ubuntu-latest (Docker preinstalled); least-privilege `permissions:` blocks are deliberate on all workflows.
