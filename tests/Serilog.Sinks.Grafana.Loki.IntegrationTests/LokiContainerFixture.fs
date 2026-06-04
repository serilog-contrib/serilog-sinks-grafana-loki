module Serilog.Sinks.Grafana.Loki.IntegrationTests.LokiContainerFixture

open System.Threading.Tasks
open DotNet.Testcontainers.Builders
open Xunit

/// Starts a real Loki container once per test class and exposes its base URI.
/// Tests obtain it via IClassFixture<LokiFixture>.
type LokiFixture() =

    let container =
        ContainerBuilder("grafana/loki:3.7.2")
            .WithPortBinding(3100, true)
            .WithWaitStrategy(
                Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(fun r -> r.ForPath("/ready").ForPort(3100us))
            )
            .Build()

    let mutable lokiUri = ""

    /// Base URI of the Loki container, e.g. "http://localhost:49832".
    member _.Uri = lokiUri

    interface IAsyncLifetime with
        member _.InitializeAsync() : Task =
            task {
                do! container.StartAsync()
                lokiUri <- $"http://localhost:{container.GetMappedPublicPort(3100)}"
            }

        member _.DisposeAsync() : Task =
            task { do! container.DisposeAsync().AsTask() }