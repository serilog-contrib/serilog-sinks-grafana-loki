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

open System
open System.Buffers

/// Reusable IBufferWriter<byte> backed by ArrayPool<byte>.Shared.
/// Call Clear() to reset between uses; Dispose() to return the buffer to the pool.
[<Sealed>]
type internal PooledByteBufferWriter(initialCapacity: int) =

    let mutable buffer: byte[] = ArrayPool<byte>.Shared.Rent(initialCapacity)
    let mutable writtenCount = 0

    let ensureCapacity (sizeHint: int) =
        let hint = max sizeHint 1
        let remaining = buffer.Length - writtenCount

        if remaining < hint then
            let newSize = max (writtenCount + hint) (buffer.Length * 2)
            let newBuffer = ArrayPool<byte>.Shared.Rent(newSize)
            Buffer.BlockCopy(buffer, 0, newBuffer, 0, writtenCount)
            ArrayPool<byte>.Shared.Return(buffer)
            buffer <- newBuffer

    member _.WrittenMemory = ReadOnlyMemory<byte>(buffer, 0, writtenCount)
    member _.WrittenSpan = ReadOnlySpan<byte>(buffer, 0, writtenCount)
    member _.WrittenCount = writtenCount

    member _.Clear() = writtenCount <- 0

    interface IBufferWriter<byte> with
        member _.Advance(count: int) =
            if count < 0 then
                raise (ArgumentOutOfRangeException(nameof count, "Count must be non-negative."))

            writtenCount <- writtenCount + count

        member _.GetMemory(sizeHint: int) =
            ensureCapacity sizeHint
            Memory<byte>(buffer, writtenCount, buffer.Length - writtenCount)

        member _.GetSpan(sizeHint: int) =
            ensureCapacity sizeHint
            Span<byte>(buffer, writtenCount, buffer.Length - writtenCount)

    interface IDisposable with
        member _.Dispose() =
            ArrayPool<byte>.Shared.Return(buffer)
            buffer <- Array.empty
            writtenCount <- 0