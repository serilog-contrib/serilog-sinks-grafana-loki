namespace Serilog.Sinks.Grafana.Loki.V9

open System
open System.Diagnostics
open Serilog
open Serilog.Events

module Types =
    
    type LogEventProperties = Map<string, LogEventPropertyValue>
    
    module LogEventProperties =
        let rec renamePropertyIfPresent (propertyName: string) (renamingStrategy: string -> string) (properties: LogEventProperties) =
            properties
            |> Map.tryFind propertyName
            |> function
                | None -> properties
                | Some value ->
                    let newName = renamingStrategy propertyName
                    properties
                    |> Map.remove propertyName
                    |> renamePropertyIfPresent newName renamingStrategy 
                    |> Map.add newName value
    
    type LokiLogEvent = {
        Timestamp: DateTimeOffset
        InternalTimestamp: DateTimeOffset
        Level: LogEventLevel
        MessageTemplate: MessageTemplate
        Exception: exn option
        Properties: LogEventProperties
        TraceId: ActivityTraceId option
        SpanId: ActivitySpanId option
    }
    
    [<CLIMutable>]
    type LokiLabel = {
        Key: string
        Value: string
    }
    
    type Infra = {
        GlobalLabels: Map<string, string>
    }
    
    /// {
    ///     "streams": [
    ///     {
    ///         "stream": {
    ///             "label": "value"
    ///             },
    ///         "values": [
    ///             [ "unix epoch in nanoseconds", "log line" ],
    ///             [ "unix epoch in nanoseconds", "log line" ]
    ///         ]
    ///     }
    ///     ]
    /// }

module Experiments =
        
    open Types
    
    type IReservedPropertyRenamingStrategy =
        abstract member Rename : originalName: string -> string
    
    let temp
        (renamingStrategy: IReservedPropertyRenamingStrategy)
        (logEvents: LogEvent list) : string =   
        
        // Renames map at the beginning of the app?
        let addLevelAsPropertySafely (logEvent: LokiLogEvent) =
            let toGrafanaLogLevel level =
                match level with
                | LogEventLevel.Verbose -> "trace" 
                | LogEventLevel.Debug -> "debug"
                | LogEventLevel.Information -> "info"
                | LogEventLevel.Warning -> "warning"
                | LogEventLevel.Error -> "error"
                | LogEventLevel.Fatal -> "fatal"
                | _ -> "unknown"
            let properties = logEvent.Properties                          
                            |> LogEventProperties.renamePropertyIfPresent "level" renamingStrategy.Rename
                            |> Map.add "level" (logEvent.Level |> toGrafanaLogLevel |> ScalarValue :> LogEventPropertyValue)
            { logEvent with Properties = properties }                
            
        failwith "todo"