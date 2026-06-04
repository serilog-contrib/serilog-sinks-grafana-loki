#!/usr/bin/env -S dotnet fsi
// Usage:  dotnet fsi build.fsx [-- --target <name>]
// Targets: Clean | Restore | Build | Test | IntegrationTest | Pack | Push | Default (default)
// Example: dotnet fsi build.fsx -- --target Pack

#r "nuget: Fake.Core.Target,   6.1.4"
#r "nuget: Fake.DotNet.Cli,    6.1.4"
#r "nuget: Fake.IO.FileSystem, 6.1.4"

open Fake.Core
open Fake.DotNet
open Fake.IO
open Fake.IO.Globbing.Operators

// Initialise FAKE context so Target.create / ==> etc. work when invoked via
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
                MSBuildParams = noBinLog o.MSBuildParams })
        solution)

Target.create "Build" (fun _ ->
    // Build the full solution so both library and test projects are ready
    // for subsequent --no-build steps.
    DotNet.build
        (fun o ->
            { o with
                Configuration = releaseCfg
                NoRestore = true
                MSBuildParams = noBinLog o.MSBuildParams })
        solution)

Target.create "Test" (fun _ ->
    DotNet.test
        (fun o ->
            { o with
                Configuration = releaseCfg
                NoBuild = true
                MSBuildParams = noBinLog o.MSBuildParams })
        unitTests)

Target.create "IntegrationTest" (fun _ ->
    DotNet.test
        (fun o ->
            { o with
                Configuration = releaseCfg
                NoBuild = true
                MSBuildParams = noBinLog o.MSBuildParams })
        intTests)

Target.create "Pack" (fun _ ->
    DotNet.pack
        (fun o ->
            { o with
                Configuration = releaseCfg
                NoBuild = true
                OutputPath = Some Artifacts
                MSBuildParams = noBinLog o.MSBuildParams })
        library)

Target.create "Push" (fun _ ->
    let key = Environment.environVarOrFail "NUGET_API_KEY"

    !! $"{Artifacts}/*.nupkg"
    |> Seq.iter (fun pkg ->
        let result =
            DotNet.exec
                (fun o ->
                    { o with
                        Verbosity = Some DotNet.Verbosity.Minimal })
                "nuget"
                $"push \"{pkg}\" -s https://api.nuget.org/v3/index.json -k {key}"

        if not result.OK then
            failwithf "nuget push failed: %A" result.Errors))

Target.create "Default" ignore

// ── Dependency chain ──────────────────────────────────────────────────────────

open Fake.Core.TargetOperators

"Restore" ==> "Build" ==> "Test" ==> "Pack" ==> "Default"
"Build" ==> "IntegrationTest"
"Pack" ==> "Push"

Target.runOrDefaultWithArguments "Default"