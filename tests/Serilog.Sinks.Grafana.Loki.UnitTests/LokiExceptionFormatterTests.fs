module Serilog.Sinks.Grafana.Loki.Tests.LokiExceptionFormatterTests

open System
open Swensen.Unquote
open Xunit
open Serilog.Sinks.Grafana.Loki
open Serilog.Sinks.Grafana.Loki.Tests.Helpers

let private fmt = LokiExceptionFormatter()

// Unquote quotations cannot capture `use`-bound IDisposable variables.
// Pattern: extract the values you want to assert into plain let-bindings first,
// then dispose the document, then use `test <@ ... @>` on the extracted values.

// ── Field presence ────────────────────────────────────────────────────────────

[<Fact>]
let ``Format: Type field contains fully-qualified exception type name`` () =
    let ex = ArgumentNullException("param")
    use doc = serializeException fmt ex

    let typeName =
        doc.RootElement |> tryProp "Type" |> Option.map (fun v -> v.GetString())

    test <@ typeName = Some(typeof<ArgumentNullException>.FullName) @>

[<Fact>]
let ``Format: Message field contains exception message`` () =
    let ex = InvalidOperationException("something went wrong")
    use doc = serializeException fmt ex

    let message =
        doc.RootElement |> tryProp "Message" |> Option.map (fun v -> v.GetString())

    test <@ message = Some "something went wrong" @>

[<Fact>]
let ``Format: StackTrace field present for thrown exception`` () =
    let ex =
        try
            raise (InvalidOperationException("boom"))
            failwith "unreachable"
        with e ->
            e

    use doc = serializeException fmt ex
    let hasTrace = doc.RootElement |> tryProp "StackTrace" |> Option.isSome
    test <@ hasTrace @>

[<Fact>]
let ``Format: Source field absent when exception source is null`` () =
    // Not thrown — Source stays null, formatter should omit the field
    let ex = Exception("no source")
    use doc = serializeException fmt ex
    let sourceVal = doc.RootElement |> tryProp "Source"
    // Field absent OR non-null value (never emit a null JSON field for Source)
    match sourceVal with
    | None -> () // absent — correct
    | Some v ->
        let kind = v.ValueKind
        test <@ kind <> System.Text.Json.JsonValueKind.Null @>

// ── Inner exception ───────────────────────────────────────────────────────────

[<Fact>]
let ``Format: InnerException field present when exception has inner`` () =
    let outer = Exception("outer", InvalidOperationException("inner"))
    use doc = serializeException fmt outer
    let hasInner = doc.RootElement |> tryProp "InnerException" |> Option.isSome
    test <@ hasInner @>

[<Fact>]
let ``Format: InnerException contains correct type and message`` () =
    let inner = ArgumentException("bad arg")
    let outer = Exception("wrapper", inner)
    use doc = serializeException fmt outer

    let innerType =
        doc.RootElement
        |> tryProp "InnerException"
        |> Option.bind (tryProp "Type")
        |> Option.map (fun v -> v.GetString())

    let innerMsg =
        doc.RootElement
        |> tryProp "InnerException"
        |> Option.bind (tryProp "Message")
        |> Option.map (fun v -> v.GetString())

    test <@ innerType = Some(typeof<ArgumentException>.FullName) @>
    test <@ innerMsg = Some "bad arg" @>

[<Fact>]
let ``Format: InnerException absent when exception has no inner`` () =
    let ex = Exception("standalone")
    use doc = serializeException fmt ex
    let hasInner = doc.RootElement |> tryProp "InnerException" |> Option.isSome
    test <@ not hasInner @>

// ── AggregateException ────────────────────────────────────────────────────────

[<Fact>]
let ``Format: AggregateException uses InnerExceptions array not InnerException`` () =
    let agg = AggregateException("aggregate", [| Exception("a"); Exception("b") |])
    use doc = serializeException fmt agg
    let hasArray = doc.RootElement |> tryProp "InnerExceptions" |> Option.isSome
    let hasSingular = doc.RootElement |> tryProp "InnerException" |> Option.isSome
    test <@ hasArray @>
    test <@ not hasSingular @>

[<Fact>]
let ``Format: AggregateException InnerExceptions array has correct count`` () =
    let agg =
        AggregateException("agg", [| Exception("a"); Exception("b"); Exception("c") |])

    use doc = serializeException fmt agg

    let count =
        doc.RootElement
        |> tryProp "InnerExceptions"
        |> Option.map (fun v -> v.GetArrayLength())

    test <@ count = Some 3 @>

[<Fact>]
let ``Format: AggregateException inner items each have Message`` () =
    let agg = AggregateException("agg", [| InvalidOperationException("inner") :> exn |])
    use doc = serializeException fmt agg

    let msg =
        doc.RootElement
        |> tryProp "InnerExceptions"
        |> Option.map (fun arr -> arr[0])
        |> Option.bind (tryProp "Message")
        |> Option.map (fun v -> v.GetString())

    test <@ msg = Some "inner" @>

// ── Deep nesting ──────────────────────────────────────────────────────────────

[<Fact>]
let ``Format: deeply nested inner exceptions are recursively serialized`` () =
    let deep = Exception("deep")
    let mid = Exception("middle", deep)
    let top = Exception("top", mid)
    use doc = serializeException fmt top

    let deepMsg =
        doc.RootElement
        |> tryProp "InnerException"
        |> Option.bind (tryProp "InnerException")
        |> Option.bind (tryProp "Message")
        |> Option.map (fun v -> v.GetString())

    test <@ deepMsg = Some "deep" @>
