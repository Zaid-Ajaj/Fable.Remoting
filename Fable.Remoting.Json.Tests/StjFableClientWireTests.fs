module StjFableClientWireTests

// Phase 10 — pins the wire shapes a Fable browser client (Fable.SimpleJson)
// can send to a Fable.Remoting server, exercising the **READ** side of the
// System.Text.Json converter set. Together with the write-side byte-pin
// gallery (WireFormatTests + StjWireFormatTests), these tests prevent the
// IntegrationTests CI regression from re-surfacing:
//
//   - Map<int,_>, Map<decimal,_>, Map<TimeOnly,_> previously failed because
//     the non-string-key Map reader misclassified each type's wire form,
//     producing a JSON token shape the inner deserialise step rejected.
//   - byte[] arguments from Fable.SimpleJson arrive as a JSON array of
//     numbers ([1,2,3]). The STJ default reader expects a base64 string,
//     so without ByteArrayConverter the binary-IO endpoints break.
//   - Direct decimal arguments come through as JSON strings ("3.14"),
//     which only NumberHandling.AllowReadingFromString lets STJ accept.
//
// The fixtures below replay the *exact* wire bytes Fable.SimpleJson emits
// for each shape — the source of truth for the wire is
// `packages/client/Fable.SimpleJson/fable/Json.Converter.fs:serialize`
// (the Map branch at line 786 + the per-type primitive branches at
// lines 656–688 + `quote.js`). Any future change to the converter set
// that breaks one of these reads should fail here, not in the
// IntegrationTests UITests run.

open System
open System.Text.Json
open Fable.Remoting.Json.SystemTextJson
open Expecto

let private opts = FableConverters.create ()

let private deserialize<'a> (json: string) : 'a =
    JsonSerializer.Deserialize<'a>(json, opts)

let stjFableClientWireTests =
    testList "Phase 10 — Fable.SimpleJson wire compatibility (STJ read side)" [

        // -- Map<K, V> non-string-key — Fable.SimpleJson wire shape ----------
        //
        // For `K` with primitive wire form, Fable.SimpleJson emits
        //   {"<unquoted>": <value>, ...}
        // The property name strips its JSON delimiters when the parser
        // extracts it; the reader has to re-add the quotes only for types
        // whose deserialiser expects a JsonTokenType.String token, and
        // leave numeric types untouched so STJ reads them as JSON Number.

        testCase "Map<int, int> from Fable.SimpleJson object-form wire" <| fun () ->
            let m = deserialize<Map<int, int>> "{\"10\":10,\"20\":20}"
            Expect.equal m (Map.ofList [10, 10; 20, 20]) "int keys must round-trip"

        testCase "Map<decimal, int> from Fable.SimpleJson object-form wire" <| fun () ->
            let m = deserialize<Map<decimal, int>> "{\"10\":10,\"20\":20}"
            Expect.equal m (Map.ofList [10M, 10; 20M, 20]) "decimal keys must round-trip"

        testCase "Map<int16, int> from Fable.SimpleJson object-form wire" <| fun () ->
            let m = deserialize<Map<int16, int>> "{\"10\":10}"
            Expect.equal m (Map.ofList [10s, 10]) "int16 keys must round-trip"

        testCase "Map<byte, int> from Fable.SimpleJson object-form wire" <| fun () ->
            let m = deserialize<Map<byte, int>> "{\"10\":10}"
            Expect.equal m (Map.ofList [10uy, 10]) "byte keys must round-trip"

        testCase "Map<float, int> from Fable.SimpleJson object-form wire" <| fun () ->
            let m = deserialize<Map<float, int>> "{\"3.14\":1}"
            Expect.equal m (Map.ofList [3.14, 1]) "float keys must round-trip"

        testCase "Map<int64, int> from Fable.SimpleJson object-form wire" <| fun () ->
            // Fable.SimpleJson serialises int64 as a quoted string with +
            // prefix, so prop.Name in the JSON is "+10" (without literal
            // quotes around it — the `"`s are JSON delimiters).
            let m = deserialize<Map<int64, int>> "{\"+10\":10}"
            Expect.equal m (Map.ofList [10L, 10]) "int64 keys must round-trip"

        testCase "Map<bigint, int> from Fable.SimpleJson object-form wire" <| fun () ->
            let m = deserialize<Map<bigint, int>> "{\"42\":1}"
            Expect.equal m (Map.ofList [42I, 1]) "bigint keys must round-trip"

        testCase "Map<TimeOnly, TimeOnly> from Fable.SimpleJson object-form wire" <| fun () ->
            // Wire: {"<ticks>":"<ticks>"} — the property name is the bare
            // ticks string (no escaped quotes), because Fable.SimpleJson's
            // serializedKey for TimeOnly already contains the literal `"`
            // characters that act as JSON property-name delimiters.
            let oneHourTicks = TimeOnly(1, 0, 0).Ticks
            let elevenAmTicks = TimeOnly(11, 0, 0).Ticks
            let json =
                sprintf "{\"%d\":\"%d\"}" oneHourTicks elevenAmTicks
            let m = deserialize<Map<TimeOnly, TimeOnly>> json
            Expect.equal m (Map.ofList [TimeOnly(1, 0, 0), TimeOnly(11, 0, 0)])
                "TimeOnly keys + values must round-trip"

        testCase "Map<DateOnly, DateOnly> from Fable.SimpleJson object-form wire" <| fun () ->
            // Wire: {"<day-number>":<day-number>} — both key and value as
            // bare numbers (DateOnly is in Fable.SimpleJson's number group).
            let m = deserialize<Map<DateOnly, DateOnly>> "{\"739251\":739252}"
            Expect.equal m (Map.ofList [DateOnly.FromDayNumber 739251, DateOnly.FromDayNumber 739252])
                "DateOnly keys + values must round-trip"

        testCase "Map<DateTimeOffset, int> from Fable.SimpleJson object-form wire" <| fun () ->
            let m = deserialize<Map<DateTimeOffset, int>> "{\"2024-01-15T00:00:00+00:00\":1}"
            Expect.equal
                m
                (Map.ofList [DateTimeOffset(2024, 1, 15, 0, 0, 0, TimeSpan.Zero), 1])
                "DateTimeOffset keys must round-trip"

        testCase "Map<Guid, int> from Fable.SimpleJson object-form wire" <| fun () ->
            let g = Guid.Parse "12345678-1234-5678-1234-567812345678"
            let m = deserialize<Map<Guid, int>> "{\"12345678-1234-5678-1234-567812345678\":1}"
            Expect.equal m (Map.ofList [g, 1]) "Guid keys must round-trip"

        // -- byte[] — Fable.SimpleJson sends arrays of numbers ---------------

        testCase "byte[] from Fable.SimpleJson [n,n,n] array form" <| fun () ->
            let v = deserialize<byte[]> "[1,2,3]"
            Expect.equal v [| 1uy; 2uy; 3uy |] "byte[] array form must read"

        testCase "byte[] from base64 string (matches our writer output)" <| fun () ->
            let v = deserialize<byte[]> "\"AQID\""
            Expect.equal v [| 1uy; 2uy; 3uy |] "byte[] base64 form must read"

        testCase "byte[] empty array" <| fun () ->
            let v = deserialize<byte[]> "[]"
            Expect.equal v [||] "empty byte[] array form must read"

        testCase "byte[] empty base64 string" <| fun () ->
            let v = deserialize<byte[]> "\"\""
            Expect.equal v [||] "empty byte[] base64 form must read"

        testCase "byte[] null" <| fun () ->
            let v = deserialize<byte[]> "null"
            Expect.isNull v "byte[] null must return null"

        // -- Numeric primitives from Fable.SimpleJson's quoted-string form --
        //
        // Fable.SimpleJson serialises int64 / uint64 / bigint / decimal /
        // Guid / DateTime / DateTimeOffset / TimeOnly / Char as JSON strings.
        // Some of these (int64, bigint, etc.) we already had custom
        // converters for. Decimal in particular was previously broken on
        // the direct-argument path because STJ default rejects the quoted
        // form. NumberHandling.AllowReadingFromString restores Newtonsoft's
        // leniency.

        testCase "decimal from quoted string (Fable.SimpleJson direct-arg form)" <| fun () ->
            let v = deserialize<decimal> "\"32313213121.1415926535\""
            Expect.equal v 32313213121.1415926535m "decimal from quoted string must read"

        testCase "decimal from bare number (server-side STJ writer form)" <| fun () ->
            let v = deserialize<decimal> "32313213121.1415926535"
            Expect.equal v 32313213121.1415926535m "decimal from bare number must read"

        testCase "int from quoted string" <| fun () ->
            // Used by Map<int,_>'s post-quoting deserialise path when the
            // map reader DOES wrap the key (e.g. for legacy wire shapes).
            // After our refactor, int isn't on the wrap list anymore — but
            // AllowReadingFromString keeps the path open for safety.
            let v = deserialize<int> "\"42\""
            Expect.equal v 42 "int from quoted string must read"

        // NaN / Infinity / -Infinity from JSON strings ("NaN", etc.) is a
        // follow-up enhancement — Fable.SimpleJson emits these for the few
        // double values they cover, but they aren't on the IntegrationTests
        // regression path that triggered Phase 10. Skipped here intentionally.

        // -- Outer argument-array slicing — the per-arg JSON text for a -----
        //    Map / byte[] argument matches what Fable.SimpleJson would send.
        //
        // These pins are the "what the request body looks like" anchors —
        // they catch a regression where the per-arg slicer accidentally
        // changes the bytes handed to the per-type deserialise call.

        testCase "outer-array slice — Map<int,int> single arg" <| fun () ->
            // Fable.SimpleJson wraps 1 arg in TypeInfo.Tuple([T]) and
            // serialises as `[<element>]`. For Map<int,int>:
            let body = "[{\"10\":10,\"20\":20}]"
            use doc = JsonDocument.Parse(body)
            let arg = doc.RootElement.EnumerateArray() |> Seq.head
            let m = JsonSerializer.Deserialize<Map<int, int>>(arg.GetRawText(), opts)
            Expect.equal m (Map.ofList [10, 10; 20, 20]) "argument-array slice + reader"

        testCase "outer-array slice — byte[] + record + int64 + byte[] (multiByteArrays)" <| fun () ->
            // Fable.SimpleJson, when serialising multi-arg invocations,
            // produces `[arg0, arg1, arg2, arg3]`. multiByteArrays takes
            // HighScore -> byte[] -> int64 -> byte[]. The byte[] args go
            // through as JSON number arrays; the int64 as a quoted string.
            // This pin verifies all four slices deserialise correctly.
            let body =
                "[{\"Name\":\"Alice\",\"Score\":42},[1,2,3],\"+1234\",[4,5,6]]"
            use doc = JsonDocument.Parse(body)
            let elements = doc.RootElement.EnumerateArray() |> Seq.toArray
            let bytes1 = JsonSerializer.Deserialize<byte[]>(elements.[1].GetRawText(), opts)
            let num = JsonSerializer.Deserialize<int64>(elements.[2].GetRawText(), opts)
            let bytes2 = JsonSerializer.Deserialize<byte[]>(elements.[3].GetRawText(), opts)
            Expect.equal bytes1 [|1uy; 2uy; 3uy|] "first byte[] arg"
            Expect.equal num 1234L "int64 arg"
            Expect.equal bytes2 [|4uy; 5uy; 6uy|] "second byte[] arg"
    ]
