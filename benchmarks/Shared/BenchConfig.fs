// Copyright 2020-2026 Mykhailo Shevchuk & Contributors
//
// Licensed under the MIT license;
// you may not use this file except in compliance with the License.
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See LICENSE file in the project root for full license information.

/// BenchmarkDotNet configurations shared by both benchmark projects.
/// References only BenchmarkDotNet, so it compiles unchanged in each.
module Benchmarks.Shared.Config

open System
open System.IO
open BenchmarkDotNet.Columns
open BenchmarkDotNet.Configs
open BenchmarkDotNet.Diagnosers
open BenchmarkDotNet.Exporters
open BenchmarkDotNet.Exporters.Json
open BenchmarkDotNet.Jobs
open BenchmarkDotNet.Loggers

/// Common pieces: a readable console table, GitHub-flavoured markdown export
/// (so the two projects' results can be diffed), a brief JSON export (machine-readable,
/// consumed by the CI comparison step), and the allocation diagnoser (whose numbers
/// are exact regardless of the short timing job).
///
/// The artifacts path is pinned to this assembly's output directory. The benchmark
/// projects use the same `Benchmarks` namespace, so their report files share names;
/// keeping each project's output under its own bin folder stops one project
/// overwriting another.
let private applyCommon (config: ManualConfig) =
    config.ArtifactsPath <- Path.Combine(AppContext.BaseDirectory, "BenchmarkDotNet.Artifacts")

    config
        .AddColumnProvider(DefaultColumnProviders.Instance)
        .AddLogger(ConsoleLogger.Default)
        .AddExporter(MarkdownExporter.GitHub)
        .AddExporter(JsonExporter.Brief)
        .AddDiagnoser(MemoryDiagnoser.Default)
    |> ignore

/// Stateless micro-benchmarks (per-event formatter): the standard accurate job.
type MicroConfig() as this =
    inherit ManualConfig()

    do
        applyCommon this
        this.AddJob(Job.Default) |> ignore

/// End-to-end sink benchmarks. Each invocation builds a fresh logger in
/// [<IterationSetup>] and disposes it (flush) inside the benchmark, so the work
/// must run exactly once per measured invocation: InvocationCount = UnrollFactor = 1.
type SinkConfig() as this =
    inherit ManualConfig()

    do
        applyCommon this
        this.AddJob(Job.Default.WithInvocationCount(1).WithUnrollFactor(1)) |> ignore