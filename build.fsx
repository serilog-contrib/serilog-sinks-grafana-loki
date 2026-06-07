#!/usr/bin/env -S dotnet fsi
// Usage:  dotnet fsi build.fsx [-- --target <name>]
// Targets: Clean | Restore | Build | Test | IntegrationTest | Pack | Benchmark | Push | Default (default)
// Example: dotnet fsi build.fsx -- --target Pack

#r "nuget: Fake.Core.Target,   6.1.4"
#r "nuget: Fake.DotNet.Cli,    6.1.4"
#r "nuget: Fake.IO.FileSystem, 6.1.4"

open Fake.Core
open Fake.DotNet
open Fake.IO
open Fake.IO.Globbing.Operators

// Initialize FAKE context so Target.create / ==> etc. work when invoked via
// `dotnet fsi build.fsx -- --target <name>`.
Context.setExecutionContext (
    Context.RuntimeContext.Fake(
        Context.FakeExecutionContext.Create false __SOURCE_FILE__ (fsi.CommandLineArgs |> Array.toList |> List.tail)
    )
)

// ── Paths ──────────────────────────────────────────────────────────────────────

[<Literal>]
let Artifacts = "./artifacts"

let solution = "Serilog.Sinks.Grafana.Loki.slnx"
let library = "src/Serilog.Sinks.Grafana.Loki/Serilog.Sinks.Grafana.Loki.fsproj"

let unitTests =
    "tests/Serilog.Sinks.Grafana.Loki.UnitTests/Serilog.Sinks.Grafana.Loki.UnitTests.fsproj"

let intTests =
    "tests/Serilog.Sinks.Grafana.Loki.IntegrationTests/Serilog.Sinks.Grafana.Loki.IntegrationTests.fsproj"

// Benchmark executables live outside the solution (each pins a different Serilog closure),
// so they are listed explicitly and driven with `dotnet run` rather than via the solution.
let benchmarks =
    [
        "benchmarks/Serilog.Sinks.Grafana.Loki.Benchmarks.Current/Serilog.Sinks.Grafana.Loki.Benchmarks.Current.fsproj"
        "benchmarks/Serilog.Sinks.Grafana.Loki.Benchmarks.NuGet/Serilog.Sinks.Grafana.Loki.Benchmarks.NuGet.fsproj"
        "benchmarks/Serilog.Sinks.Grafana.Loki.Benchmarks.YetAnother/Serilog.Sinks.Grafana.Loki.Benchmarks.YetAnother.fsproj"
    ]

// ── Helpers ────────────────────────────────────────────────────────────────────

let releaseCfg = DotNet.BuildConfiguration.Release

// FAKE 6.1.4 parses the MSBuild binary log for error extraction, but .NET 10 SDK
// writes binlog format v25 while FAKE only supports up to v16. Disable the internal
// binlog on all MSBuild-backed operations to avoid the version mismatch.
let noBinLog (o: MSBuild.CliArguments) = { o with DisableInternalBinLog = true }

// ── Targets ───────────────────────────────────────────────────────────────────

Target.create "Clean" (fun _ -> Shell.cleanDir Artifacts)

Target.create "Restore" (fun _ ->
    DotNet.restore
        (fun o ->
            { o with
                MSBuildParams = noBinLog o.MSBuildParams
            })
        solution)

Target.create "Build" (fun _ ->
    // Build the full solution so both library and test projects are ready
    // for subsequent --no-build steps.
    DotNet.build
        (fun o ->
            { o with
                Configuration = releaseCfg
                NoRestore = true
                MSBuildParams = noBinLog o.MSBuildParams
            })
        solution)

Target.create "Test" (fun _ ->
    DotNet.test
        (fun o ->
            { o with
                Configuration = releaseCfg
                NoBuild = true
                MSBuildParams = noBinLog o.MSBuildParams
            })
        unitTests)

Target.create "IntegrationTest" (fun _ ->
    DotNet.test
        (fun o ->
            { o with
                Configuration = releaseCfg
                NoBuild = true
                MSBuildParams = noBinLog o.MSBuildParams
            })
        intTests)

Target.create "Pack" (fun _ ->
    DotNet.pack
        (fun o ->
            { o with
                Configuration = releaseCfg
                NoBuild = true
                OutputPath = Some Artifacts
                MSBuildParams = noBinLog o.MSBuildParams
            })
        library)

Target.create "Push" (fun _ ->
    let key = Environment.environVarOrFail "NUGET_API_KEY"

    !! $"{Artifacts}/*.nupkg"
    |> Seq.iter (fun pkg ->
        // No DotNet.exec Verbosity here: `dotnet nuget push` is not an MSBuild command and
        // has no --verbosity option — passing one fails argument parsing before any upload.
        // --skip-duplicate makes re-runs of a (partially) failed release idempotent.
        let args =
            $"push \"{pkg}\" -s https://api.nuget.org/v3/index.json -k {key} --skip-duplicate"

        let result = DotNet.exec id "nuget" args

        if not result.OK then
            failwithf $"nuget push failed for %s{pkg} with exit code %d{result.ExitCode}"))

// Run the BenchmarkDotNet suites (Current + NuGet + YetAnother). Standalone — deliberately
// NOT in the Default chain (slow, and pulls the NuGet baseline closure). Cross-platform via dotnet fsi:
//   dotnet fsi build.fsx -- --target Benchmark
// Optional environment variables:
//   BENCH_FILTER       a BenchmarkDotNet --filter glob, e.g. '*Sink*'
//   LOKI_BENCH_TARGET  push to a real Loki (docker compose up -d loki) instead of the fake
Target.create "Benchmark" (fun _ ->
    // Husky's MSBuild auto-install hook is pure overhead for a benchmark run.
    Environment.setEnvironVar "HUSKY" "0"

    let filterArgs =
        match Environment.environVarOrNone "BENCH_FILTER" with
        | Some f when f <> "" -> $" -- --filter {f}"
        | _ -> ""

    for proj in benchmarks do
        Trace.traceImportant $"=== Running {proj} ==="
        let result = DotNet.exec id "run" $"-c Release --project \"{proj}\"{filterArgs}"

        if not result.OK then
            failwithf $"benchmark run failed for {proj}: %A{result.Errors}"

    Trace.traceImportant "=== Reports ==="

    !!"benchmarks/**/BenchmarkDotNet.Artifacts/results/*-report-github.md"
    |> Seq.iter (Trace.tracefn "%s"))

Target.create "Default" ignore

// ── Dependency chain ──────────────────────────────────────────────────────────

open Fake.Core.TargetOperators

// Default: Restore → Build → Test → Pack. Push deliberately does NOT depend on Test —
// the CI publish job runs only after the build/test jobs are green, so its chain is
// Restore → Build → Pack → Push. The soft dependency (?=>) keeps Test ordered before
// Pack whenever both are activated (e.g. Default) without Pack pulling Test in.
"Restore" ==> "Build" ==> "Pack" ==> "Default"
"Build" ==> "Test" ==> "Default"
"Test" ?=> "Pack"
"Build" ==> "IntegrationTest"
"Pack" ==> "Push"

Target.runOrDefaultWithArguments "Default"