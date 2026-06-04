module Serilog.Sinks.Grafana.Loki.Tests.LabelsTests

open System
open Swensen.Unquote
open Xunit
open Serilog.Events
open Serilog.Sinks.Grafana.Loki
open Serilog.Sinks.Grafana.Loki.Tests.Helpers

// Shared fixtures used throughout
let private noGlobals = Map.empty<string, string>
let private noProps = [||]

// ── sanitizeLabelKey (via buildLabelSet — inline helper, tested through behaviour) ──

// sanitizeLabelKey is `inline` and cannot be called directly from an external
// assembly. Its behaviour is verified through buildLabelSet: numeric property
// names must appear with the "param" prefix in the resulting label set.

[<Fact>]
let ``sanitizeLabelKey via buildLabelSet: numeric property name gets param prefix`` () =
    let reserved = buildReservedKeys noGlobals false
    let event = mkInfo [ "0", box "v" ]
    let labels = buildLabelSet noGlobals reserved [| "0" |] false event
    test <@ Map.containsKey "param0" labels @>
    test <@ not (Map.containsKey "0" labels) @>

[<Fact>]
let ``sanitizeLabelKey via buildLabelSet: non-numeric key unchanged`` () =
    let reserved = buildReservedKeys noGlobals false
    let event = mkInfo [ "app", box "svc" ]
    let labels = buildLabelSet noGlobals reserved [| "app" |] false event
    test <@ Map.containsKey "app" labels @>

// ── logLevelToLabel (via buildLabelSet) ───────────────────────────────────────

// logLevelToLabel is `inline`. Level mapping is verified through buildLabelSet
// with handleLevel=true — the "level" label value must match Grafana vocabulary.

[<Theory>]
[<InlineData(0, "trace")>] // Verbose
[<InlineData(1, "debug")>] // Debug
[<InlineData(2, "info")>] // Information
[<InlineData(3, "warning")>] // Warning
[<InlineData(4, "error")>] // Error
[<InlineData(5, "fatal")>] // Fatal — maps to "fatal", not "critical"
let ``logLevelToLabel via buildLabelSet: all Serilog levels map to Grafana vocabulary``
    (levelInt: int)
    (expected: string)
    =
    let level = enum<LogEventLevel> levelInt
    let reserved = buildReservedKeys noGlobals true
    let event = mkEvent level []
    let labels = buildLabelSet noGlobals reserved noProps true event
    test <@ labels["level"] = expected @>

// ── toUnixNanoseconds (via timestampToNs shim) ────────────────────────────────

// toUnixNanoseconds is `inline` — timestampToNs is a non-inline shim that
// delegates to it, exposed specifically for cross-assembly precision tests.

[<Fact>]
let ``toUnixNanoseconds: unix epoch returns 0`` () =
    test <@ timestampToNs DateTimeOffset.UnixEpoch = 0L @>

[<Fact>]
let ``toUnixNanoseconds: one second after epoch returns 1 000 000 000`` () =
    let ts = DateTimeOffset.UnixEpoch.AddSeconds(1.0)
    test <@ timestampToNs ts = 1_000_000_000L @>

[<Fact>]
let ``toUnixNanoseconds: 1 tick = 100 ns`` () =
    let ts = DateTimeOffset.UnixEpoch.AddTicks(1L)
    test <@ timestampToNs ts = 100L @>

[<Fact>]
let ``toUnixNanoseconds: known timestamp matches expected nanoseconds`` () =
    // 2021-05-25T12:00:00Z = 1 621 944 000 seconds since epoch
    let ts = DateTimeOffset(2021, 5, 25, 12, 0, 0, TimeSpan.Zero)
    test <@ timestampToNs ts = 1_621_944_000_000_000_000L @>

// ── buildLabelSet ─────────────────────────────────────────────────────────────

[<Fact>]
let ``buildLabelSet: global labels always appear in result`` () =
    let globals = Map.ofList [ "app", "my-service"; "env", "prod" ]
    let reserved = buildReservedKeys globals false
    let event = mkInfo []
    let labels = buildLabelSet globals reserved noProps false event
    test <@ Map.containsKey "app" labels @>
    test <@ Map.containsKey "env" labels @>
    test <@ labels["app"] = "my-service" @>
    test <@ labels["env"] = "prod" @>

[<Fact>]
let ``buildLabelSet: level label added when handleLevel is true`` () =
    let reserved = buildReservedKeys noGlobals true
    let event = mkEvent LogEventLevel.Warning []
    let labels = buildLabelSet noGlobals reserved noProps true event
    test <@ Map.containsKey "level" labels @>
    test <@ labels["level"] = "warning" @>

[<Fact>]
let ``buildLabelSet: level label absent when handleLevel is false`` () =
    let reserved = buildReservedKeys noGlobals false
    let event = mkInfo []
    let labels = buildLabelSet noGlobals reserved noProps false event
    test <@ not (Map.containsKey "level" labels) @>

[<Fact>]
let ``buildLabelSet: property promoted to label when in propertiesAsLabels`` () =
    let reserved = buildReservedKeys noGlobals false
    let event = mkInfo [ "RequestPath", box "/health" ]
    let labels = buildLabelSet noGlobals reserved [| "RequestPath" |] false event
    test <@ Map.containsKey "RequestPath" labels @>
    test <@ labels["RequestPath"] = "/health" @>

[<Fact>]
let ``buildLabelSet: global label wins over matching property`` () =
    let globals = Map.ofList [ "app", "global-value" ]
    let reserved = buildReservedKeys globals false
    let event = mkInfo [ "app", box "property-value" ]
    let labels = buildLabelSet globals reserved [| "app" |] false event
    test <@ labels["app"] = "global-value" @>

[<Fact>]
let ``buildLabelSet: level label wins over property named level`` () =
    let reserved = buildReservedKeys noGlobals true
    let event = mkEvent LogEventLevel.Error [ "level", box "custom" ]
    let labels = buildLabelSet noGlobals reserved [| "level" |] true event
    // The synthetic level label ("error") must win, not the property value ("custom")
    test <@ labels["level"] = "error" @>

[<Fact>]
let ``buildLabelSet: numeric property name gets param prefix`` () =
    let reserved = buildReservedKeys noGlobals false
    let event = mkInfo [ "0", box "value" ]
    let labels = buildLabelSet noGlobals reserved [| "0" |] false event
    test <@ Map.containsKey "param0" labels @>
    test <@ not (Map.containsKey "0" labels) @>

[<Fact>]
let ``buildLabelSet: missing property produces no label`` () =
    let reserved = buildReservedKeys noGlobals false
    let event = mkInfo [] // no properties at all
    let labels = buildLabelSet noGlobals reserved [| "MissingProp" |] false event
    test <@ not (Map.containsKey "MissingProp" labels) @>

[<Fact>]
let ``buildLabelSet: result contains exactly globals + level + promoted properties`` () =
    let globals = Map.ofList [ "env", "prod" ]
    let reserved = buildReservedKeys globals true
    let event = mkEvent LogEventLevel.Debug [ "service", box "api"; "ignored", box "x" ]
    let labels = buildLabelSet globals reserved [| "service" |] true event
    test <@ Map.count labels = 3 @> // env + level + service
    test <@ labels["env"] = "prod" @>
    test <@ labels["level"] = "debug" @>
    test <@ labels["service"] = "api" @>
    test <@ not (Map.containsKey "ignored" labels) @>

// ── buildGlobalLabelMap ───────────────────────────────────────────────────────

[<Fact>]
let ``buildGlobalLabelMap: converts LokiLabel array to immutable map`` () =
    let labels = [| { Key = "app"; Value = "svc" }; { Key = "env"; Value = "dev" } |]
    let result = buildGlobalLabelMap labels
    test <@ result["app"] = "svc" @>
    test <@ result["env"] = "dev" @>

[<Fact>]
let ``buildGlobalLabelMap: empty array returns empty map`` () =
    test <@ buildGlobalLabelMap [||] = Map.empty @>

[<Fact>]
let ``buildGlobalLabelMap: last writer wins on duplicate keys`` () =
    let labels = [| { Key = "k"; Value = "first" }; { Key = "k"; Value = "second" } |]
    test <@ (buildGlobalLabelMap labels)["k"] = "second" @>

// ── buildReservedKeys ─────────────────────────────────────────────────────────

[<Fact>]
let ``buildReservedKeys: contains all global label keys`` () =
    let globals = Map.ofList [ "app", "v"; "env", "v" ]
    let reserved = buildReservedKeys globals false
    test <@ Set.contains "app" reserved @>
    test <@ Set.contains "env" reserved @>

[<Fact>]
let ``buildReservedKeys: includes level when handleLevel is true`` () =
    let reserved = buildReservedKeys noGlobals true
    test <@ Set.contains "level" reserved @>

[<Fact>]
let ``buildReservedKeys: excludes level when handleLevel is false`` () =
    let reserved = buildReservedKeys noGlobals false
    test <@ not (Set.contains "level" reserved) @>
