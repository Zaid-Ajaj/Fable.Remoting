module StjUnionPrototypeTests

// Phase 3 prototype verification. Serialises through the System.Text.Json
// FSharpUnionConverterFactory and asserts byte-equal output to the Newtonsoft
// pins from WireFormatTests. Read-side round-trips the writer's output.
//
// Scope is deliberately narrow: only DU cases whose inner types either
//   (a) are primitives STJ already handles (int, string, bool, float),
//   (b) recurse through a DU (handled by the same factory), or
//   (c) are F# lists (STJ handles via IEnumerable — matches Newtonsoft).
//
// Excluded from Phase 3 (need Phase 4 converters):
//   - DU fields of type int64 (needs Long converter — "+N" string shape).
//   - DU fields of type option (needs Option converter — Newtonsoft inlines Some, emits null for None).
//   - DU fields of type tuple (needs Tuple converter — JSON array of typed elements).
//   - DU fields of type record (handled by STJ defaults, but no byte-compat guarantee yet).

open System
open System.Text.Encodings.Web
open System.Text.Json
open Fable.Remoting.Json.SystemTextJson
open Expecto
open Types
open WireFormatTests

let private stjOptions =
    let o = JsonSerializerOptions(Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping)
    o.Converters.Add(FSharpUnionConverterFactory())
    o

let private serializeStj (value: 'a) = JsonSerializer.Serialize(value, stjOptions)

let private pinStj (expected: string) (value: 'a) =
    let actual = serializeStj value
    Expect.equal actual expected (sprintf "STJ wire mismatch — value was %A" value)

let private deserializeStj<'a> (json: string) : 'a = JsonSerializer.Deserialize<'a>(json, stjOptions)

// Single-field DU with primitive inner. Reused by writer + reader tests.
type IntList = { Items: int list }

let unionStjPrototypeTests = testList "Phase 3 — STJ union converter prototype" [

    testList "writer: byte-equal to Newtonsoft" [
        testCase "no-field — Nothing" <| fun () -> pinStj "\"Nothing\"" (Nothing : Maybe<int>)
        testCase "no-field — Color.Red" <| fun () -> pinStj "\"Red\"" Red
        testCase "no-field — Color.Blue" <| fun () -> pinStj "\"Blue\"" Blue
        testCase "no-field — MultiFieldUnion.Zero" <| fun () -> pinStj "\"Zero\"" Zero

        testCase "single-field int — Just 5" <| fun () -> pinStj "{\"Just\":5}" (Just 5)
        testCase "single-field string — Just \"hello\"" <| fun () -> pinStj "{\"Just\":\"hello\"}" (Just "hello")
        testCase "single-field string — Token \"x\"" <| fun () -> pinStj "{\"Token\":\"x\"}" (Token "x")
        testCase "single-field int — One 5" <| fun () -> pinStj "{\"One\":5}" (One 5)

        testCase "multi-field — Two(5, \"x\")" <| fun () -> pinStj "{\"Two\":[5,\"x\"]}" (Two(5, "x"))
        testCase "multi-field — Three(5, \"x\", true)" <| fun () ->
            pinStj "{\"Three\":[5,\"x\",true]}" (Three(5, "x", true))

        testCase "recursive — TLeaf 5" <| fun () -> pinStj "{\"TLeaf\":5}" (TLeaf 5)
        testCase "recursive — TBranch nested" <| fun () ->
            pinStj "{\"TBranch\":[{\"TLeaf\":5},{\"TLeaf\":10}]}" (TBranch(TLeaf 5, TLeaf 10))

        testCase "generic — Just 5 as Maybe<int>" <| fun () -> pinStj "{\"Just\":5}" (Just 5 : Maybe<int>)

        testCase "private constructor — String50" <| fun () ->
            // Pins that BindingFlags.NonPublic is honoured by both reader and writer paths.
            // Matches the expected Newtonsoft writer output for a single-field DU
            // (the existing Newtonsoft tests only round-trip the array shape).
            pinStj "{\"String50\":\"onur\"}" (String50.Create "onur")

        testCase "list field — Just [1;2;3]" <| fun () ->
            // List inner falls through to STJ's default IEnumerable handling,
            // which matches Newtonsoft's Kind.Other dispatch for FSharpList.
            pinStj "{\"Just\":[1,2,3]}" (Just [1;2;3])
    ]

    testList "reader: round-trips writer output" [
        testCase "no-field — \"Nothing\"" <| fun () ->
            let result = deserializeStj<Maybe<int>> "\"Nothing\""
            Expect.equal result Nothing "Nothing round-trips"

        testCase "no-field — \"Red\"" <| fun () ->
            let result = deserializeStj<Color> "\"Red\""
            Expect.equal result Red "Red round-trips"

        testCase "single-field int — {\"Just\":5}" <| fun () ->
            let result = deserializeStj<Maybe<int>> "{\"Just\":5}"
            Expect.equal result (Just 5) "Just 5 round-trips"

        testCase "single-field string — {\"Token\":\"x\"}" <| fun () ->
            let result = deserializeStj<Token> "{\"Token\":\"x\"}"
            Expect.equal result (Token "x") "Token round-trips"

        testCase "multi-field — Two" <| fun () ->
            let result = deserializeStj<MultiFieldUnion> "{\"Two\":[5,\"x\"]}"
            Expect.equal result (Two(5, "x")) "Two round-trips"

        testCase "multi-field — Three" <| fun () ->
            let result = deserializeStj<MultiFieldUnion> "{\"Three\":[5,\"x\",true]}"
            Expect.equal result (Three(5, "x", true)) "Three round-trips"

        testCase "recursive — TBranch" <| fun () ->
            let result = deserializeStj<RecursiveTree> "{\"TBranch\":[{\"TLeaf\":5},{\"TLeaf\":10}]}"
            Expect.equal result (TBranch(TLeaf 5, TLeaf 10)) "TBranch round-trips"

        testCase "private constructor — String50 round-trips through {\"String50\":...}" <| fun () ->
            let original = String50.Create "onur"
            let serialized = serializeStj original
            let result = deserializeStj<String50> serialized
            Expect.equal (result.Read()) "onur" "String50 inner value round-trips"
    ]
]
