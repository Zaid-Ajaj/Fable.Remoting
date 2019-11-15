module FableGiraffeAdapterTests

open System
open System.Net.Http
open System.IO
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.TestHost
open Giraffe
open FSharp.Control.Tasks.V2.ContextInsensitive
open Fable.Remoting.Server
open Fable.Remoting.Giraffe
open Expecto
open Types
open Newtonsoft.Json
open Fable.Remoting.Json

let equal x y = Expect.equal true (x = y) (sprintf "%A = %A" x y)
let pass () = Expect.equal true true ""
let fail () = Expect.equal false true ""
let failUnexpect (x: obj) = Expect.equal false true (sprintf "%A was not expected" x)

let giraffeApp =
    Remoting.createApi()
    |> Remoting.fromValue implementation
    |> Remoting.buildHttpHandler

let postContent (input: string) =  new StringContent(input, Text.Encoding.UTF8)
let configureApp (app : IApplicationBuilder) =
    app.UseGiraffe giraffeApp

let createHost() =
    WebHostBuilder()
        .UseContentRoot(Directory.GetCurrentDirectory())
        .Configure(Action<IApplicationBuilder> configureApp)

let postReq (path : string) (body: string) =
    let url = "http://127.0.0.1" + path
    let request = new HttpRequestMessage(HttpMethod.Post, url)
    request.Content <- postContent (sprintf "[%s]" body)
    request

let runTask task =
    task
    |> Async.AwaitTask
    |> Async.RunSynchronously

let testServer = new TestServer(createHost())
let client = testServer.CreateClient()

let makeRequest (request : HttpRequestMessage) =
    task {
        let! response = client.SendAsync request
        let! content = response.Content.ReadAsStringAsync()
        return content
    } |> runTask

let request (path: string) (body: string) =
    makeRequest (postReq path body)

let private fableConverter = FableJsonConverter()

let ofJson<'t> (input: string) =
    JsonConvert.DeserializeObject<'t>(input, fableConverter)
let toJson (x: obj) =
    JsonConvert.SerializeObject(x, fableConverter)

let fableGiraffeAdapterTests =
    testList "FableGiraffeAdapter tests" [
        testCase "String round trip" <| fun () ->
            let input = "\"hello\""
            let output = makeRequest (postReq "/IProtocol/echoString" (toJson input))
            match ofJson<string> output with
            | "\"hello\"" -> pass()
            | otherwise -> failUnexpect otherwise

        testCase "Int round trip" <| fun () ->
            [-2; -1; 0; 1; 2]
            |> List.map (fun input -> makeRequest (postReq "/IProtocol/echoInteger" (toJson input)) |> ofJson<int>)
            |> function
                | [-2; -1; 0; 1; 2] -> pass()
                | otherwise -> failUnexpect otherwise

        testCase "Map<int * int, int> round trip" <| fun () ->
            [(1,1), 1]
            |> Map.ofList
            |> toJson
            |> (postReq "/IProtocol/echoTupleMap" >> makeRequest)
            |> ofJson<Map<int * int, int>>
            |> fun dict ->
                match Map.toList dict with
                | [(1,1), 1] -> pass()
                | otherwise -> failUnexpect otherwise

        testCase "Option<int> round first trip" <| fun () ->
            [Some 2; None; Some -2]
            |> List.map (fun input -> makeRequest (postReq "/IProtocol/echoIntOption" (toJson input))|> ofJson<int option>)
            |> function
                | [Some 2; None; Some -2] -> pass()
                | otherwise -> failUnexpect otherwise

        testCase "bigint roundtrip" <| fun () ->
            [1I .. 5I]
            |> List.map (fun input -> makeRequest (postReq "/IProtocol/echoBigInteger" (toJson input))|> ofJson<bigint>)
            |> function
                | xs when xs = [1I .. 5I] -> pass()
                | otherwise -> failUnexpect otherwise

        testCase "Option<string> round first trip" <| fun () ->
            [Some "hello"; None; Some "there"]
            |> List.map (fun input -> makeRequest (postReq "/IProtocol/echoStringOption" (toJson input))|> ofJson<string option>)
            |> function
                | [Some "hello"; None; Some "there"] -> pass()
                | otherwise -> failUnexpect otherwise

        testCase "Option<string> round second trip" <| fun () ->
            [Some "hello"; None; Some "there"]
            |> List.map (fun input -> makeRequest (postReq "/IProtocol/echoStringOption" (toJson input))|> ofJson<string option>)
            |> function
                | [Some "hello"; None; Some "there"] -> pass()
                | otherwise -> failUnexpect otherwise

        testCase "bool round trip" <| fun () ->
            [true; false; true]
            |> List.map (fun input -> makeRequest (postReq "/IProtocol/echoBool" (toJson input)) |> ofJson<bool>)
            |> function
                | [true; false; true] -> pass()
                | otherwise -> failUnexpect otherwise

        testCase "Generic union Maybe<int> round trip" <| fun () ->
            [Just 5; Nothing; Just -2]
            |> List.map (fun input -> makeRequest (postReq "/IProtocol/echoGenericUnionInt" (toJson input))|> ofJson<Maybe<int>>)
            |> function
                | [Just 5; Nothing; Just -2] -> pass()
                | otherwise -> failUnexpect otherwise

        testCase "Generic union Maybe<string> round trip" <| fun () ->
            [Just "hello"; Nothing; Just "there"; Just null]
            |> List.map (fun input -> makeRequest (postReq "/IProtocol/echoGenericUnionString" (toJson input))|> ofJson<Maybe<string>>)
            |> function
                | [Just "hello"; Nothing; Just "there"; Just null] -> pass()
                | otherwise -> failUnexpect otherwise

        testCase "Simple union round trip" <| fun () ->
            [A; B]
            |> List.map (fun input -> makeRequest (postReq "/IProtocol/echoSimpleUnion" (toJson input)) |> ofJson<AB>)
            |> function
                | [A; B] -> pass()
                | otherwise -> failUnexpect otherwise

        testCase "List<int> round first trip" <| fun () ->
            [[]; [1 .. 5]]
            |> List.map (fun input -> makeRequest (postReq "/IProtocol/echoIntList" (toJson input)) |> ofJson<int list>)
            |> function
                | [[]; [1;2;3;4;5]] -> pass()
                | otherwise -> failUnexpect otherwise

        testCase "List<int> round second trip" <| fun () ->
            [[1.5; 1.5; 3.0]]
            |> List.map (fun input -> makeRequest (postReq "/IProtocol/floatList" (toJson input))|> ofJson<float list>)
            |> function
                | [[1.5; 1.5; 3.0]] -> pass()
                | otherwise -> failUnexpect otherwise

        testCase "Unit as input with list result" <| fun () ->
            [(); ()]
            |> List.map (fun input -> makeRequest (postReq "/IProtocol/unitToInts" (toJson input)) |> ofJson<int list>)
            |> function
                | [[1;2;3;4;5]; [1;2;3;4;5]] -> pass()
                | otherwise -> failUnexpect otherwise

        testCase "Result<int, string> roundtrip works with Ok" <| fun _ ->
            makeRequest (postReq "/IProtocol/echoResult" (toJson (Ok 15)))
            |> ofJson<Result<int, string>>
            |> function
                | Ok 15 -> pass()
                | otherwise -> fail()

        testCase "Result<int, string> roundtrip works with Error" <| fun _ ->
            makeRequest (postReq "/IProtocol/echoResult" (toJson (Error "hello")))
            |> ofJson<Result<int, string>>
            |> function
                | Error "hello" -> pass()
                | otherwise -> fail()

        testCase "Record round trip" <| fun () ->
            [{ Prop1 = "hello"; Prop2 = 10; Prop3 = Some 5 }
             { Prop1 = "";      Prop2 = 1;  Prop3 = None }]
            |> List.map (toJson >> request "/IProtocol/echoRecord" >> ofJson<Record>)
            |> function
                | [{ Prop1 = "hello"; Prop2 = 10; Prop3 = Some 5 }
                   { Prop1 = "";      Prop2 = 1;  Prop3 = None   } ] -> pass()
                | otherwise -> failUnexpect otherwise

        testCase "Map<string, int> roundtrip" <| fun () ->
            ["one",1; "two",2]
            |> Map.ofList
            |> toJson
            |> request "/IProtocol/echoMap"
            |> ofJson<Map<string, int>>
            |> Map.toList
            |> function
                | ["one",1; "two",2] -> pass()
                | otherwise -> fail()
    ]