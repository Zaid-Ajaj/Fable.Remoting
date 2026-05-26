module LegacyNewtonsoftIntegrationTests

// Phase 8 (gap #1, #6): explicit coverage for the legacy Newtonsoft path
// after Phase 5's default-flip.
//
// Mirrors the structure of `StjHttpIntegrationTests.fs` but pins the server
// to the legacy backend via `|> Remoting.withNewtonsoftJson`. Catches any
// regression in the Newtonsoft branch of `Server.Proxy.fs` (especially
// `parseArgumentArray`'s JToken parsing and `deserialiseArgWithBackend`'s
// `newtonsoftArgSettings` — which preserves DateTimeOffset offsets).

#nowarn "44"

open System
open System.IO
open System.Net.Http
open System.Text
open System.Text.Json
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.TestHost
open Giraffe
open Fable.Remoting.Server
open Fable.Remoting.Giraffe
open Fable.Remoting.Json.SystemTextJson
open Expecto
open Types

let private legacyApp =
    Remoting.createApi()
    |> Remoting.fromValue implementation
    |> Remoting.withNewtonsoftJson
    |> Remoting.buildHttpHandler

let private configureApp (app: IApplicationBuilder) =
    app.UseGiraffe legacyApp

let private testServer =
    new TestServer(WebHostBuilder().UseContentRoot(Directory.GetCurrentDirectory()).Configure(Action<IApplicationBuilder> configureApp))
let private client = testServer.CreateClient()

let private postReq (path: string) (body: string) =
    let request = new HttpRequestMessage(HttpMethod.Post, "http://127.0.0.1" + path)
    request.Content <- new StringContent(sprintf "[%s]" body, Encoding.UTF8)
    request

let private makeRequest (request: HttpRequestMessage) =
    task {
        let! response = client.SendAsync request
        let! content = response.Content.ReadAsStringAsync()
        return content
    } |> Async.AwaitTask |> Async.RunSynchronously

// Client-side fixtures use STJ for consistency with byte-compat tests —
// the server-side backend choice is what's under test, not the client.
let private stjOptions = FableConverters.create ()
let private toJson (x: 'a) = JsonSerializer.Serialize<'a>(x, stjOptions)
let private ofJson<'a> (s: string) = JsonSerializer.Deserialize<'a>(s, stjOptions)

let legacyNewtonsoftGiraffeTests =
    testList "Phase 8 — Legacy Newtonsoft HTTP integration (Giraffe)" [

        testCase "Int round-trip via legacy Newtonsoft" <| fun () ->
            let result =
                makeRequest (postReq "/IProtocol/echoInteger" (toJson 42))
                |> ofJson<int>
            Expect.equal result 42 "int round-trips via Newtonsoft server"

        testCase "String round-trip via legacy Newtonsoft" <| fun () ->
            let result =
                makeRequest (postReq "/IProtocol/echoString" (toJson "hello"))
                |> ofJson<string>
            Expect.equal result "hello" "string round-trips via Newtonsoft server"

        testCase "Option<int> None round-trip via legacy Newtonsoft" <| fun () ->
            let result =
                makeRequest (postReq "/IProtocol/echoIntOption" (toJson (None: int option)))
                |> ofJson<int option>
            Expect.equal result None "None round-trips via Newtonsoft server"

        testCase "DU Maybe<int> Just round-trip via legacy Newtonsoft" <| fun () ->
            let result =
                makeRequest (postReq "/IProtocol/echoGenericUnionInt" (toJson (Just 42)))
                |> ofJson<Maybe<int>>
            Expect.equal result (Just 42) "Just 42 round-trips via Newtonsoft server"

        testCase "Record with None field round-trip via legacy Newtonsoft" <| fun () ->
            let input : Record = { Prop1 = "x"; Prop2 = 0; Prop3 = None }
            let result =
                makeRequest (postReq "/IProtocol/echoRecord" (toJson input))
                |> ofJson<Record>
            Expect.equal result input "record with None field round-trips via Newtonsoft server"

        testCase "Map<int*int, int> round-trip via legacy Newtonsoft" <| fun () ->
            let input = Map.ofList [(1, 1), 10; (2, 2), 20]
            let result =
                makeRequest (postReq "/IProtocol/echoTupleMap" (toJson input))
                |> ofJson<Map<int*int, int>>
            Expect.equal result input "Map<tuple,int> round-trips via Newtonsoft server"
    ]
