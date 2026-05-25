module StjHttpIntegrationTests

// HTTP integration tests for the System.Text.Json opt-in path.
//
// Spins up a parallel TestServer wired with `Remoting.withSerializerOptions
// (FableConverters.create())` and exercises representative round-trips
// through the full Server → wire → Client cycle. This is the end-to-end
// proof that the opt-in surface ships a working serialiser.
//
// The existing Newtonsoft-default tests in FableGiraffeAdapterTests.fs
// continue to pass unchanged (no behaviour change without opting in).

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

let private stjOptions = FableConverters.create ()

// Build the same protocol implementation but wired through STJ.
let private stjGiraffeApp =
    Remoting.createApi()
    |> Remoting.fromValue implementation
    |> Remoting.withSerializerOptions stjOptions
    |> Remoting.buildHttpHandler

let private configureApp (app: IApplicationBuilder) =
    app.UseGiraffe stjGiraffeApp

let private testServer = new TestServer(WebHostBuilder().UseContentRoot(Directory.GetCurrentDirectory()).Configure(Action<IApplicationBuilder> configureApp))
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

let private toJson (x: 'a) = JsonSerializer.Serialize<'a>(x, stjOptions)
let private ofJson<'a> (s: string) = JsonSerializer.Deserialize<'a>(s, stjOptions)

let private pass () = Expect.equal true true ""
let private failUnexpect (x: obj) = Expect.equal false true (sprintf "%A was not expected" x)

let stjHttpIntegrationTests = testList "Phase 4b — STJ HTTP integration (Giraffe)" [
    testCase "Int round-trip via STJ" <| fun () ->
        [-2; -1; 0; 1; 2]
        |> List.map (fun input ->
            makeRequest (postReq "/IProtocol/echoInteger" (toJson input))
            |> ofJson<int>)
        |> function
            | [-2; -1; 0; 1; 2] -> pass()
            | otherwise -> failUnexpect otherwise

    testCase "String round-trip via STJ" <| fun () ->
        let result =
            makeRequest (postReq "/IProtocol/echoString" (toJson "hello"))
            |> ofJson<string>
        Expect.equal result "hello" "string echoes through STJ"

    testCase "Bool round-trip via STJ" <| fun () ->
        let result =
            makeRequest (postReq "/IProtocol/echoBool" (toJson true))
            |> ofJson<bool>
        Expect.equal result true "bool echoes through STJ"

    testCase "Option<int> round-trip via STJ (Some)" <| fun () ->
        let result =
            makeRequest (postReq "/IProtocol/echoIntOption" (toJson (Some 5)))
            |> ofJson<int option>
        Expect.equal result (Some 5) "Some 5 echoes through STJ"

    testCase "Option<int> round-trip via STJ (None)" <| fun () ->
        let result =
            makeRequest (postReq "/IProtocol/echoIntOption" (toJson (None: int option)))
            |> ofJson<int option>
        Expect.equal result None "None echoes through STJ"

    testCase "Record round-trip via STJ" <| fun () ->
        let input : Record = { Prop1 = "hello"; Prop2 = 42; Prop3 = Some 7 }
        let result =
            makeRequest (postReq "/IProtocol/echoRecord" (toJson input))
            |> ofJson<Record>
        Expect.equal result input "record echoes through STJ"

    testCase "Record with None field round-trip via STJ" <| fun () ->
        let input : Record = { Prop1 = "x"; Prop2 = 0; Prop3 = None }
        let result =
            makeRequest (postReq "/IProtocol/echoRecord" (toJson input))
            |> ofJson<Record>
        Expect.equal result input "record with None field echoes through STJ"

    testCase "DU Maybe<int> Just round-trip via STJ" <| fun () ->
        let result =
            makeRequest (postReq "/IProtocol/echoGenericUnionInt" (toJson (Just 42)))
            |> ofJson<Maybe<int>>
        Expect.equal result (Just 42) "Just 42 echoes through STJ"

    testCase "DU Maybe<int> Nothing round-trip via STJ" <| fun () ->
        let result =
            makeRequest (postReq "/IProtocol/echoGenericUnionInt" (toJson (Nothing: Maybe<int>)))
            |> ofJson<Maybe<int>>
        Expect.equal result Nothing "Nothing echoes through STJ"

    testCase "Simple DU AB round-trip via STJ" <| fun () ->
        let result =
            makeRequest (postReq "/IProtocol/echoSimpleUnion" (toJson A))
            |> ofJson<AB>
        Expect.equal result A "A echoes through STJ"

    testCase "int list round-trip via STJ" <| fun () ->
        let input = [1; 2; 3; 4; 5]
        let result =
            makeRequest (postReq "/IProtocol/echoIntList" (toJson input))
            |> ofJson<int list>
        Expect.equal result input "int list echoes through STJ"

    testCase "Record list round-trip via STJ" <| fun () ->
        let input : Record list = [
            { Prop1 = "a"; Prop2 = 1; Prop3 = Some 1 }
            { Prop1 = "b"; Prop2 = 2; Prop3 = None }
        ]
        let result =
            makeRequest (postReq "/IProtocol/echoRecordList" (toJson input))
            |> ofJson<Record list>
        Expect.equal result input "Record list echoes through STJ"

    testCase "Map<string, int> round-trip via STJ" <| fun () ->
        let input = Map.ofList ["a", 1; "b", 2; "c", 3]
        let result =
            makeRequest (postReq "/IProtocol/echoMap" (toJson input))
            |> ofJson<Map<string, int>>
        Expect.equal result input "Map<string,int> echoes through STJ"

    testCase "Map<int*int, int> round-trip via STJ" <| fun () ->
        let input = Map.ofList [(1, 1), 10; (2, 2), 20]
        let result =
            makeRequest (postReq "/IProtocol/echoTupleMap" (toJson input))
            |> ofJson<Map<int*int, int>>
        Expect.equal result input "Map<tuple,int> echoes through STJ"

    testCase "bigint round-trip via STJ" <| fun () ->
        [1I; 100I; -50I; System.Numerics.BigInteger.Parse "99999999999999999999"]
        |> List.iter (fun input ->
            let result =
                makeRequest (postReq "/IProtocol/echoBigInteger" (toJson input))
                |> ofJson<bigint>
            Expect.equal result input "bigint echoes through STJ")

    testCase "Result<int,string> Ok round-trip via STJ" <| fun () ->
        let result =
            makeRequest (postReq "/IProtocol/echoResult" (toJson (Ok 42 : Result<int, string>)))
            |> ofJson<Result<int, string>>
        Expect.equal result (Ok 42) "Ok 42 echoes through STJ"

    testCase "Result<int,string> Error round-trip via STJ" <| fun () ->
        let result =
            makeRequest (postReq "/IProtocol/echoResult" (toJson (Error "fail" : Result<int, string>)))
            |> ofJson<Result<int, string>>
        Expect.equal result (Error "fail") "Error \"fail\" echoes through STJ"

    testCase "binaryInputOutput round-trip via STJ" <| fun () ->
        // Binary input is base64-encoded by the FableJsonConverter wire shape.
        // STJ's byte[] default is also base64 → byte-equivalent.
        let input : byte[] = [| 1uy; 2uy; 3uy; 4uy; 5uy |]
        let result =
            makeRequest (postReq "/IProtocol/binaryInputOutput" (toJson input))
            |> ofJson<byte[]>
        Expect.equal result input "byte[] echoes through STJ"
]
