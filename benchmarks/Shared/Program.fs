// Copyright 2020-2026 Mykhailo Shevchuk & Contributors
//
// Licensed under the MIT license;
// you may not use this file except in compliance with the License.
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See LICENSE file in the project root for full license information.

/// Entry point shared by both benchmark projects. Compiled last so it can name
/// the benchmark types, which each project defines in the `Benchmarks` namespace.
module Benchmarks.Program

open System.Reflection
open BenchmarkDotNet.Running

[<EntryPoint>]
let main argv =
    if argv.Length = 0 then
        // No args: run every benchmark type in this assembly (no interactive prompt).
        // The assembly overload honours each type's [<Config>] and lets projects carry
        // different benchmark sets (the YetAnother project has no formatter group).
        BenchmarkRunner.Run(Assembly.GetExecutingAssembly()) |> ignore
    else
        // Args given: defer to the switcher so --filter / --list / etc. work.
        BenchmarkSwitcher.FromAssembly(Assembly.GetExecutingAssembly()).Run(argv)
        |> ignore

    0