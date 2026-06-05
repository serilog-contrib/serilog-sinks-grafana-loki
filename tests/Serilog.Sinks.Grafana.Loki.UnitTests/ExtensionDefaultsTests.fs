module Serilog.Sinks.Grafana.Loki.Tests.ExtensionDefaultsTests

open Serilog.Sinks.Grafana.Loki
open Swensen.Unquote
open Xunit

// Guards against drift: the GrafanaLoki extension-method literal defaults and
// LokiSinkOptions.Defaults are maintained by hand (two places) and must agree.
// period/retryTimeLimit default to null (-> Defaults at runtime) so they are not
// checked here; they are covered end-to-end by AppSettingsBindingTests.
let private parameters =
    typeof<LoggerConfigurationLokiExtensions>.GetMethod("GrafanaLoki").GetParameters()
    |> Array.map (fun p -> p.Name, p)
    |> dict

let private defaultOf (name: string) = parameters.[name].DefaultValue

[<Fact>]
let ``GrafanaLoki literal defaults match LokiSinkOptions.Defaults`` () =
    let d = LokiSinkOptions.Defaults
    test <@ unbox<int> (defaultOf "batchSizeLimit") = d.BatchSizeLimit @>
    test <@ unbox<int> (defaultOf "queueLimit") = d.QueueLimit @>
    test <@ unbox<bool> (defaultOf "handleLogLevelAsLabel") = d.HandleLogLevelAsLabel @>
    test <@ unbox<bool> (defaultOf "enrichTraceId") = d.EnrichTraceId @>
    test <@ unbox<bool> (defaultOf "enrichSpanId") = d.EnrichSpanId @>
    test <@ unbox<bool> (defaultOf "eagerlyEmitFirstEvent") = d.EagerlyEmitFirstEvent @>