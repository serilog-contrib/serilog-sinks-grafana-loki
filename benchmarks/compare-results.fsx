// Builds a markdown comparison table from two BenchmarkDotNet result sets.
//
// Reads the `*-report-brief.json` files (JsonExporter.Brief, see Shared/BenchConfig.fs)
// under each directory, joins benchmarks by FullName (all benchmark projects share the
// `Benchmarks` namespace by design) and prints a markdown table to stdout.
//
//   dotnet fsi compare-results.fsx <baselineDir> <candidateDir> [baselineLabel] [candidateLabel]
//
// Δ markers: `Allocated` is exact, so anything beyond ±2% is flagged; `Mean` on shared
// CI runners is noisy, so only ±10% gets a marker. 🔺 regression, 🟢 improvement.

open System
open System.Globalization
open System.IO
open System.Text.Json

let args = fsi.CommandLineArgs |> Array.tail

if args.Length < 2 then
    eprintfn "usage: dotnet fsi compare-results.fsx <baselineDir> <candidateDir> [baselineLabel] [candidateLabel]"
    exit 2

let baselineDir, candidateDir = args[0], args[1]
let baselineLabel = if args.Length > 2 then args[2] else "baseline"
let candidateLabel = if args.Length > 3 then args[3] else "candidate"

let inv = CultureInfo.InvariantCulture

type Result = { MeanNs: float; AllocB: float }

/// FullName -> Result for every benchmark in every brief-JSON report under dir.
let readResults dir =
    Directory.EnumerateFiles(dir, "*-report-brief.json", SearchOption.AllDirectories)
    |> Seq.collect (fun path ->
        use doc = JsonDocument.Parse(File.ReadAllText path)

        doc.RootElement.GetProperty("Benchmarks").EnumerateArray()
        |> Seq.map (fun b ->
            b.GetProperty("FullName").GetString(),
            {
                MeanNs = b.GetProperty("Statistics").GetProperty("Mean").GetDouble()
                AllocB = b.GetProperty("Memory").GetProperty("BytesAllocatedPerOperation").GetDouble()
            })
        // Materialize before JsonDocument is disposed at the end of this lambda.
        |> Seq.toList)
    |> Map.ofSeq

// All numbers use the invariant culture so the table is identical regardless of locale.
let fmtBytes (b: float) =
    if b >= 1048576.0 then
        String.Format(inv, "{0:N2} MB", b / 1048576.0)
    elif b >= 1024.0 then
        String.Format(inv, "{0:N1} KB", b / 1024.0)
    else
        String.Format(inv, "{0:N0} B", b)

let fmtTime (ns: float) =
    if ns >= 1e6 then
        String.Format(inv, "{0:N2} ms", ns / 1e6)
    elif ns >= 1e3 then
        String.Format(inv, "{0:N2} us", ns / 1e3)
    else
        String.Format(inv, "{0:N0} ns", ns)

let fmtDelta (baseValue: float) (newValue: float) (markerPct: float) =
    if baseValue <= 0.0 then
        "n/a"
    else
        let pct = (newValue - baseValue) / baseValue * 100.0
        let s = String.Format(inv, "{0:+0.0;−0.0;+0.0}%", pct)

        if pct >= markerPct then $"{s} 🔺"
        elif pct <= -markerPct then $"{s} 🟢"
        else s

let baseline = readResults baselineDir
let candidate = readResults candidateDir

let keys =
    baseline
    |> Map.toSeq
    |> Seq.map fst
    |> Seq.filter candidate.ContainsKey
    |> Seq.sort
    |> Seq.toList

if List.isEmpty keys then
    printfn "_No common benchmarks between the two result sets._"
else
    printfn
        "| Benchmark | %s alloc | %s alloc | Δ alloc | %s mean | %s mean | Δ mean |"
        baselineLabel
        candidateLabel
        baselineLabel
        candidateLabel

    printfn "|---|--:|--:|--:|--:|--:|--:|"

    for k in keys do
        let b = baseline[k]
        let c = candidate[k]

        // Strip the shared namespace prefix for readability; escape pipes for markdown.
        let name =
            let stripped =
                if k.StartsWith "Benchmarks." then
                    k.Substring "Benchmarks.".Length
                else
                    k

            stripped.Replace("|", "\\|")

        printfn
            "| %s | %s | %s | %s | %s | %s | %s |"
            name
            (fmtBytes b.AllocB)
            (fmtBytes c.AllocB)
            (fmtDelta b.AllocB c.AllocB 2.0)
            (fmtTime b.MeanNs)
            (fmtTime c.MeanNs)
            (fmtDelta b.MeanNs c.MeanNs 10.0)