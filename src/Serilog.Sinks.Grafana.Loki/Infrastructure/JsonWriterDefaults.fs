// Copyright 2020-2026 Mykhailo Shevchuk & Contributors
//
// Licensed under the MIT license;
// you may not use this file except in compliance with the License.
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See LICENSE file in the project root for full license information.

namespace Serilog.Sinks.Grafana.Loki.Infrastructure

open System.Buffers
open System.Text.Encodings.Web
open System.Text.Json

// Single source of truth for every Utf8JsonWriter the sink creates.
//
// The default Utf8JsonWriter encoder is HTML-safe and renders every '"', '<', '>', '&', '\'' and
// all non-ASCII as \uXXXX, which makes the stored Loki log line unreadable. The relaxed encoder
// escapes the JSON-mandatory set (quote, backslash, control chars) plus the line/paragraph
// separators U+2028/U+2029 and DEL, and emits printable non-ASCII verbatim. Output stays valid
// JSON (and valid UTF-8), so consumers are unaffected.
//
// Centralised behind createWriter so escaping cannot drift between writer sites: a writer built
// the default way (`new Utf8JsonWriter(buffer)`) would silently re-introduce \uXXXX over part of
// the payload, and no compiler warning catches the missing options argument.
[<RequireQualifiedAccess>]
module internal JsonWriterDefaults =

    let relaxedOptions =
        JsonWriterOptions(Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping)

    /// Creates a Utf8JsonWriter over the buffer using the relaxed (readable) encoder.
    let createWriter (buffer: PooledByteBufferWriter) =
        new Utf8JsonWriter(buffer :> IBufferWriter<byte>, relaxedOptions)