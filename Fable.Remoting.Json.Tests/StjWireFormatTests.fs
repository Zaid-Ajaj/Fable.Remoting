module StjWireFormatTests

// Phase 4 — runs the full Phase 2 byte-compat gallery through the System.Text.Json
// converter set. Every assertion in WireFormatTests.fs must hold byte-equally
// against the STJ serializer.

open System.Text.Json
open Fable.Remoting.Json.SystemTextJson
open Expecto

let private stjSerializer : WireFormatTests.ISerializer =
    let options = FableConverters.create ()
    { new WireFormatTests.ISerializer with
        member _.Serialize<'a>(value: 'a) = JsonSerializer.Serialize<'a>(value, options)
        member _.Deserialize<'a>(json: string) : 'a = JsonSerializer.Deserialize<'a>(json, options) }

let stjWireFormatTests =
    WireFormatTests.buildWireFormatTests "Phase 4 — wire format byte-compat (STJ)" stjSerializer

/// STJ-only deserialisation tests for cases where the Newtonsoft path has
/// a known bug. The STJ converter set fixes these silently by virtue of the
/// default reference-type null handling on JsonConverter<T>.
///
/// Pre-existing Newtonsoft bug:
///   `JsonConvert.DeserializeObject<Map<string,int>>("null", FableJsonConverter())`
///   crashes with InvalidCastException at FableConverter.fs:669 — the
///   `Kind.MapWithStringKey` else-branch (array-of-pairs fallback) tries to
///   cast `JValue(null)` to `JArray` without a null guard.
///
/// The STJ converter pair (FSharpMapStringKeyConverter +
/// FSharpMapNonStringKeyConverter) inherits STJ's default HandleNull=false
/// for ref-typed JsonConverter<T>: STJ returns the null reference directly
/// without invoking the converter. No code path to crash on.
let stjFixesNewtonsoftNullBug = testList "Phase 4c — STJ fixes Newtonsoft null bugs" [
    testCase "deserialise null → Map<string,int> null (Newtonsoft crashes here)" <| fun () ->
        let m = stjSerializer.Deserialize<Map<string, int>> "null"
        Expect.isNull (box m) "STJ returns null reference, no crash"

    testCase "deserialise null → Map<Color,int> null (non-string key path)" <| fun () ->
        let m = stjSerializer.Deserialize<Map<Types.Color, int>> "null"
        Expect.isNull (box m) "STJ returns null reference for non-string-key map"
]
