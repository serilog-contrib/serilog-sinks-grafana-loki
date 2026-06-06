// Copyright 2020-2026 Mykhailo Shevchuk & Contributors
//
// Licensed under the MIT license;
// you may not use this file except in compliance with the License.
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See LICENSE file in the project root for full license information.

/// Deterministic LogEvent generation shared by the V8 and V9 benchmark projects.
/// Uses only the Serilog.Events / Serilog.Parsing surface that is stable across
/// Serilog 2.x (V8's closure) and 4.x (V9's closure), so the same source compiles
/// against both. Both versions therefore process byte-for-byte identical inputs.
module Benchmarks.Shared.EventGen

open System
open Serilog.Events
open Serilog.Parsing

let private parser = MessageTemplateParser()

let inline private scalar (key: string) (value: obj) =
    LogEventProperty(key, ScalarValue(value))

/// Builds n immutable Information events with a realistic mix of scalar property
/// types (int, string, float, bool, Guid) — exercising every branch of V9's
/// value writer and V8's JsonValueFormatter.
let buildSimple (n: int) : LogEvent[] =
    let template =
        parser.Parse("User {UserId} performed {Action} on resource {ResourceId}")

    Array.init n (fun i ->
        let props =
            [
                scalar "UserId" (1000 + i)
                scalar "Action" (if i % 2 = 0 then "login" else "logout")
                scalar "ResourceId" (sprintf "resource-%d" (i % 64))
                scalar "Elapsed" (0.25 * float i)
                scalar "Success" (i % 7 <> 0)
                scalar "CorrelationId" (Guid("00000000-0000-0000-0000-" + i.ToString("D12")))
            ]

        LogEvent(DateTimeOffset.UnixEpoch.AddMilliseconds(float i), LogEventLevel.Information, null, template, props))

/// Raises and catches ex so its StackTrace is populated, mirroring a real thrown
/// exception (the heavy string the exception serializers must encode).
let private capture (message: string) (inner: exn) : exn =
    let ex: exn =
        if isNull (box inner) then
            ApplicationException(message)
        else
            ApplicationException(message, inner)

    try
        raise ex
    with e ->
        e

/// Builds n Error events each carrying a two-level nested exception with real
/// stack traces — exercising V9's LokiExceptionFormatter vs V8's SerializeException.
let buildWithException (n: int) : LogEvent[] =
    let template = parser.Parse("Operation {OperationId} on {Service} failed")

    Array.init n (fun i ->
        let inner = capture (sprintf "inner failure %d" i) null
        let ex = capture (sprintf "outer failure %d" i) inner

        let props =
            [
                scalar "OperationId" i
                scalar "Service" "billing"
                scalar "Retryable" (i % 2 = 0)
            ]

        LogEvent(DateTimeOffset.UnixEpoch.AddMilliseconds(float i), LogEventLevel.Error, ex, template, props))