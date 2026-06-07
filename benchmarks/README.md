# Benchmarks — NuGet baseline vs current source vs YetAnother

BenchmarkDotNet suites comparing three Grafana Loki sinks:

| Project | Package | Notes |
|---|---|---|
| `…Benchmarks.NuGet` | `Serilog.Sinks.Grafana.Loki` (NuGet, `BaselineVersion`, default **9.0.0**) | the published **baseline**; CI moves it to the latest same-major release on NuGet |
| `…Benchmarks.Current` | this repo (`../src`, `ProjectReference`) | the current **source**; streams JSON **bytes** through a pooled `Utf8JsonWriter` |
| `…Benchmarks.YetAnother` | `Serilog.Sinks.Loki.YetAnother` **4.0.5** (NuGet) | third-party yardstick; Serilog 4.x, also a streaming "low-allocation" design |

> Until v9.0.0 shipped, the baseline project compiled against the v8 API and pinned `8.3.2` —
> the result tables below are that release-time v8 → v9 comparison, kept as the historical
> record.

## Why three projects

The NuGet and Current projects ship the **same** assembly name (`Serilog.Sinks.Grafana.Loki.dll`) and cannot
coexist in one process. Each contender therefore lives in its own executable;
BenchmarkDotNet runs each in isolation and the reports are merged below. The shared
workload, config and entry point live in `Shared/` and are linked into all three
projects, so every side measures byte-for-byte identical inputs.

## Methodology

- **Public API only, all sides.** Nothing reaches into internals. Each sink is driven
  through its public configuration extension (`WriteTo.GrafanaLoki` / `WriteTo.Loki`).
- **Identical in-process transport.** Each sink exposes an HTTP injection point
  (NuGet/Current `httpMessageHandler`, YetAnother `httpClient`). A fake drains
  the request body — forcing the real serialization to run — and returns `204` like Loki
  on success. No sockets, no network variance. Set `LOKI_BENCH_TARGET` to hit a real Loki
  instead (see below).
- **Identical workload** (`Shared/EventGen.fs`): a "Simple" event with a realistic mix of
  scalar property types (int, string, float, bool, Guid), and an "Exception" event with a
  two-level nested exception carrying real stack traces.
- **Identical settings:** one global label, `HandleLogLevelAsLabel = true`,
  `batchSizeLimit = 1000`, a 1-hour period (so all flushing happens on dispose), large
  queue. A fresh logger is built per measured invocation (`InvocationCount = 1`).
- **Metrics:** `Mean` (time/op) and **`Allocated`** (managed bytes/op, exact).

### Benchmark groups

1. **`SinkBenchmarks`** — the full public pipeline (configure → write _N_ events → dispose
   → serialize + POST). The real production path, run for all three sinks.
   `EventCount` ∈ {1000, 10000}, `Payload` ∈ {Simple, Exception}.
2. **`FormatterBenchmarks`** — per-event `LokiJsonTextFormatter.Format`, **NuGet and Current
   only** (YetAnother has no public per-event formatter). The sink doesn't use this public
   path in production (it writes bytes directly), so this group is a conservative view.

## Results

> AMD Ryzen 9 3950X, .NET 8.0 host, BenchmarkDotNet 0.15.8, fake in-process transport.
> Lower is better; **bold** marks the best in each row.
> V8 8.3.2 and YetAnother 4.0.5 are fixed NuGet packages, so their numbers are stable
> references; the V9 column was the source build at the v9.0.0 release. (Re-running
> `dotnet fsi build.fsx -- --target Benchmark` today compares the current baseline instead.)

### End-to-end sink — Allocated (MB/op)

| Workload | V8 8.3.2 | V9 | YetAnother 4.0.5 |
|---|--:|--:|--:|
| Simple · 1 000 | 7.48 | **0.14** | 0.47 |
| Simple · 10 000 | 74.95 | **1.36** | 4.63 |
| Exception · 1 000 | 21.93 | **6.61** | 7.00 |
| Exception · 10 000 | 219.88 | **66.21** | 70.09 |

### End-to-end sink — Mean (ms/op)

| Workload | V8 8.3.2 | V9 | YetAnother 4.0.5 |
|---|--:|--:|--:|
| Simple · 1 000 | 11.53 | **5.35** | 6.65 |
| Simple · 10 000 | 79.14 | **17.66** | 20.67 |
| Exception · 1 000 † | **25.10** | 33.52 | 34.95 |
| Exception · 10 000 | 262.93 | **121.03** | 126.77 |

† The 1 000-event means are overhead-dominated and noisy (large StdDev); use the 10 000-event
rows for throughput.

### Per-event formatter (V8 vs V9, 1 000 events)

| Workload | Metric | V8 8.3.2 | V9 |
|---|---|--:|--:|
| Simple | Allocated | 2.51 MB | **1.71 MB** |
| | Mean | **1.225 ms** | 1.239 ms |
| Exception | Allocated | 14.28 MB | **8.60 MB** |
| | Mean | 14.727 ms | **7.162 ms** |

## Reading the results

- **V9 is a generational leap over V8.** On the common (Simple) workload at 10 000 events V9
  allocates **~55× less** (1.36 MB vs 74.95 MB) and runs **~4.5× faster** (17.7 ms vs 79 ms);
  on exceptions **~3.3× less** (66 MB vs 220 MB) and **~2.2× faster** (121 ms vs 263 ms). V8
  churns Gen0/Gen1/**Gen2** (intermediate strings hit the LOH); V9 stays Gen0-only on Simple.
  The root cause: V8 builds a `LokiBatch` object graph and serializes it to a JSON **string**
  per batch, while V9 streams JSON **bytes** through a single reused pooled `Utf8JsonWriter`.
- **V9 now edges ahead of YetAnother on both workloads and both metrics.** Simple · 10 000:
  **1.36 MB vs 4.63 MB (3.4× less)** and **17.7 ms vs 20.7 ms**; Exception · 10 000:
  **66.21 MB vs 70.09 MB** and **121 ms vs 127 ms**. Both are streaming/pooled designs; V9
  closed and reversed the gap through the optimization pass recorded in the
  [V9 Optimization Log](https://github.com/serilog-contrib/serilog-sinks-grafana-loki/wiki/V9-Optimization-Log)
  on the project wiki.
- **Use the 10 000-event rows for throughput.** At 1 000 events a fixed per-flush overhead
  (~5 ms) dominates and the means are noisy — which is why the Exception · 1 000 row looks
  out of line with the rest.
- **Exception gains are smaller for everyone** because .NET stack-trace materialisation
  (`ex.StackTrace`) is a per-event cost all three pay and it dwarfs JSON serialization.
- **Payloads are each sink's out-of-the-box default**, not byte-identical (e.g. V9 emits
  `MessageTemplate`), so the differences reflect real default behaviour, not just raw
  serializer speed.

## How V9 got here

The numbers above are **post-optimization**. A measured, one-change-at-a-time pass took V9
from 19.5 MB → 1.36 MB on Simple · 10 000 (−93%) and 133.6 MB → 66.2 MB on
Exception · 10 000 (−50%), with every step validated against the full test suite. The two
biggest wins:

1. **Grouping rewrite** — a `LabelEqualityComparer : IEqualityComparer<LogEvent>` replaced the
   per-event F# `Map` key (whose structural `GetHashCode`/`Equals` boxed and enumerated on
   every event); the label set is now built once per stream.
2. **Caching `ex.StackTrace`** — the exception formatter read `ex.StackTrace`/`ex.Source`
   twice each, and `Exception.StackTrace` rebuilds the whole string on every access. Reading
   each once halved exception allocation.

The full per-step breakdown (with reasoning and deltas) lives on the project wiki:
[V9 Optimization Log](https://github.com/serilog-contrib/serilog-sinks-grafana-loki/wiki/V9-Optimization-Log).

## Running

The suites run through the repository's FAKE build script (`build.fsx`), so the **same command
works on Windows, Linux and macOS**:

```bash
# from the repo root — all three suites, fake transport (default)
dotnet fsi build.fsx -- --target Benchmark
```

Optional environment variables:

| Variable | Effect |
|---|---|
| `BENCH_FILTER` | pass-through BenchmarkDotNet `--filter` glob, e.g. `*Sink*` |
| `LOKI_BENCH_TARGET` | POST to a real Loki instead of the in-process fake (see below) |

To run a single suite directly:

```bash
dotnet run -c Release --project benchmarks/Serilog.Sinks.Grafana.Loki.Benchmarks.Current
```

Reports land in each project's `bin/Release/net8.0/BenchmarkDotNet.Artifacts/results/`
(GitHub markdown + brief JSON; the JSON feeds `compare-results.fsx`).

### In CI

`.github/workflows/benchmark.yml` compares **this source against the latest published
NuGet package** (resolved at run time; falls back to the pinned baseline if the latest
has a different API major):

- **On PRs** touching `src/`, `benchmarks/` or `Directory.Packages.props`: runs the
  end-to-end sink group for both sides and posts a delta table to the run summary and
  as a sticky PR comment. `Allocated` is exact even on shared runners — that column is
  the regression signal; `Mean` is indicative only.
- **Manually** (workflow dispatch): same comparison with a custom `--filter`, plus the
  YetAnother yardstick.

### Against a real Loki (docker-compose)

```bash
docker compose up -d loki        # from the repo root; Loki on :3100

# PowerShell:
$env:LOKI_BENCH_TARGET = 'http://localhost:3100'; dotnet fsi build.fsx -- --target Benchmark

# bash:
LOKI_BENCH_TARGET=http://localhost:3100 dotnet fsi build.fsx -- --target Benchmark
```

Here `SinkBenchmarks` POSTs to the real ingester (network + ingest are included, so
timings get noisier and are no longer a pure serialization measure); allocations are
unaffected.
