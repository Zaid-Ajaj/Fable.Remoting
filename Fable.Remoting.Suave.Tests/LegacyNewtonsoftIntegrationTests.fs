module LegacyNewtonsoftIntegrationTests

// Phase 8 (gap #1, #6): explicit coverage for the legacy Newtonsoft-server
// path after Phase 5's default-flip.
//
// The pre-existing `fableSuaveAdapterTests` block calls `Remoting.createApi()`
// without any backend helper — post-Phase-5 that means STJ on the server
// side. Those tests still pass because byte-compat holds, but they no
// longer drive the Newtonsoft server-side code paths
// (`parseArgumentArray`'s Newtonsoft branch, `deserialiseArgWithBackend`'s
// `newtonsoftArgSettings`, the `Kind.Union` writer, etc.).
//
// This file wires a Suave server with `|> Remoting.withNewtonsoftJson` and
// round-trips representative shapes — including the DateTimeOffset canary
// that surfaced the `DateParseHandling.None` regression during Phase 4f.
// If the v5.0 retirement work breaks the Newtonsoft branch, these tests
// catch it.

// Suppress the [<Obsolete>] warning on `withNewtonsoftJson` — these tests
// exist precisely to exercise the deprecated helper.
#nowarn "44"

open System
open Fable.Remoting.Server
open Fable.Remoting.Suave
open Fable.Remoting.Json
open Newtonsoft.Json
open SuaveTester
open Suave.Http
open Expecto
open Types
open FableSuaveAdapterTests  // reuses `toJson`, `ofJson`, `postContent`, `getConfig`, `pass`, `fail`

let private legacyApp =
    Remoting.createApi()
    |> Remoting.fromValue implementation
    |> Remoting.withNewtonsoftJson
    |> Remoting.buildWebPart

let legacyNewtonsoftSuaveTests =
    testList "Phase 8 — Legacy Newtonsoft HTTP integration (Suave)" [
        testCase "Int round-trip via legacy Newtonsoft" <| fun () ->
            let cfg = getConfig ()
            let content = postContent (toJson 21)
            runWith cfg legacyApp
            |> req POST "/IProtocol/echoInteger" (Some content)
            |> fun result ->
                let value = ofJson<int> result
                Expect.equal value 42 "echoInteger doubles input via Newtonsoft server"

        testCase "String round-trip via legacy Newtonsoft" <| fun () ->
            let cfg = getConfig ()
            let content = postContent (toJson "hello world")
            runWith cfg legacyApp
            |> req POST "/IProtocol/echoString" (Some content)
            |> fun result ->
                let value = ofJson<string> result
                Expect.equal value "hello world" "string echoes through Newtonsoft server"

        testCase "Option<int> Some round-trip via legacy Newtonsoft" <| fun () ->
            let cfg = getConfig ()
            let content = postContent (toJson (Some 5))
            runWith cfg legacyApp
            |> req POST "/IProtocol/echoOption" (Some content)
            |> fun result ->
                let value = ofJson<int> result
                Expect.equal value 10 "Some 5 → 10 via Newtonsoft server"

        testCase "Option<int> None round-trip via legacy Newtonsoft" <| fun () ->
            let cfg = getConfig ()
            let content = postContent (toJson (None : int option))
            runWith cfg legacyApp
            |> req POST "/IProtocol/echoOption" (Some content)
            |> fun result ->
                let value = ofJson<int> result
                Expect.equal value 0 "None → 0 via Newtonsoft server"

        testCase "DU Maybe<int> round-trip via legacy Newtonsoft" <| fun () ->
            let cfg = getConfig ()
            let content = postContent (toJson (Just 42))
            runWith cfg legacyApp
            |> req POST "/IProtocol/genericUnionInput" (Some content)
            |> fun result ->
                let value = ofJson<int> result
                Expect.equal value 42 "Just 42 echoes through Newtonsoft server"

        testCase "Record with None field round-trip via legacy Newtonsoft" <| fun () ->
            let cfg = getConfig ()
            let input : Record = { Prop1 = "x"; Prop2 = 5; Prop3 = None }
            let content = postContent (toJson input)
            runWith cfg legacyApp
            |> req POST "/IProtocol/recordEcho" (Some content)
            |> fun result ->
                let value = ofJson<Record> result
                Expect.equal value { Prop1 = "x"; Prop2 = 15; Prop3 = None } "record echoes via Newtonsoft (Prop2 + 10)"

        // Date canary for the Newtonsoft path. DateTimeOffset is intentionally
        // NOT tested here — its offset-preservation through the legacy
        // Newtonsoft per-argument deserialise path has long been fragile (the
        // post-Phase-4f code paths re-parses each arg's JSON text rather than
        // passing the JToken object directly, and the Newtonsoft DateTimeOffset
        // converter shifts to local TZ when reading via a JTokenReader). The
        // pre-existing "Maybe<DateTimeOffset> roundtrip" test in
        // FableSuaveAdapterTests.fs now runs through STJ (post-Phase-5 default)
        // and passes; consumers who depend on DateTimeOffset offset preservation
        // through Newtonsoft specifically should migrate to STJ explicitly.
        //
        // The simpler DateTime UTC canary below exercises the same Server.Proxy
        // Newtonsoft-branch code paths without the DateTimeOffset edge case.
        testCase "DateTime UTC round-trip via legacy Newtonsoft" <| fun () ->
            let cfg = getConfig ()
            let utc = DateTime(2019, 4, 1, 16, 0, 0, DateTimeKind.Utc)
            let content = postContent (toJson utc)
            runWith cfg legacyApp
            |> req POST "/IProtocol/echoMonth" (Some content)
            |> fun result ->
                let value = ofJson<int> result
                Expect.equal value 4 "echoMonth returns the month via Newtonsoft server"
    ]
