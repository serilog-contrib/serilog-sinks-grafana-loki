// Copyright 2020-2026 Mykhailo Shevchuk & Contributors
//
// Licensed under the MIT license;
// you may not use this file except in compliance with the License.
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See LICENSE file in the project root for full license information.

namespace Serilog.Sinks.Grafana.Loki

open System
open System.Text.Json

/// Default ILokiExceptionFormatter.
/// Recursively serializes Type, Message, Source, StackTrace and inner exceptions.
type LokiExceptionFormatter() =

    // Pre-encode property names once at startup — avoids per-call encoding on the hot path.
    static let pType = JsonEncodedText.Encode "Type"
    static let pMessage = JsonEncodedText.Encode "Message"
    static let pSource = JsonEncodedText.Encode "Source"
    static let pStackTrace = JsonEncodedText.Encode "StackTrace"
    static let pInnerException = JsonEncodedText.Encode "InnerException"
    static let pInnerExceptions = JsonEncodedText.Encode "InnerExceptions"

    // depth guards against cyclic InnerException chains (possible with custom exceptions)
    // which would otherwise cause an unbounded recursion and StackOverflowException.
    static member private Write(writer: Utf8JsonWriter, ex: exn, depth: int) =
        writer.WriteStartObject()
        writer.WriteString(pType, ex.GetType().FullName)
        writer.WriteString(pMessage, ex.Message)

        if not (String.IsNullOrEmpty ex.Source) then
            writer.WriteString(pSource, ex.Source)

        if not (String.IsNullOrEmpty ex.StackTrace) then
            writer.WriteString(pStackTrace, ex.StackTrace)

        if depth < 20 then
            match ex with
            | :? AggregateException as agg when agg.InnerExceptions.Count > 0 ->
                writer.WritePropertyName(pInnerExceptions)
                writer.WriteStartArray()

                for inner in agg.InnerExceptions do
                    LokiExceptionFormatter.Write(writer, inner, depth + 1)

                writer.WriteEndArray()
            | _ when not (isNull ex.InnerException) ->
                writer.WritePropertyName(pInnerException)
                LokiExceptionFormatter.Write(writer, ex.InnerException, depth + 1)
            | _ -> ()

        writer.WriteEndObject()

    interface ILokiExceptionFormatter with
        member _.Format(writer, ex) =
            LokiExceptionFormatter.Write(writer, ex, 0)