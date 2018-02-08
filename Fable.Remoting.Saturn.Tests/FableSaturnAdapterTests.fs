module FableSaturnAdapterTests

open System
open System.Net
open System.Net.Http
open System.IO
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.TestHost
open Microsoft.Extensions.DependencyInjection
open Giraffe.Middleware
open Giraffe.HttpHandlers
open Giraffe.Tasks

open Expecto
open Types
open Mono.Cecil
open Saturn.Application
open Saturn.Router

// Test helpers
open Fable.Remoting.Saturn
FableSaturnAdapter.logger <- Some (printfn "%s")
let equal x y = Expect.equal true (x = y) (sprintf "%A = %A" x y)
let pass () = Expect.equal true true ""   
let fail () = Expect.equal false true ""
let failUnexpect (x: obj) = Expect.equal false true (sprintf "%A was not expected" x) 
let testScope = scope {
    defaultHandlerFor implementation
}

let app = application {
    router testScope
    url "http://127.0.0.1/"
}

let postContent (input: string) =  new StringContent(input, Text.Encoding.UTF8)

let server = app.RunAsync()

let postReq (path : string) (body: string) =
    let url = "http://127.0.0.1" + path
    let request = new HttpRequestMessage(HttpMethod.Post, url)
    request.Content <- postContent body
    request

let runTask task =
    task
    |> Async.AwaitTask
    |> Async.RunSynchronously

let client = new HttpClient()

let makeRequest (request : HttpRequestMessage) =
    task {
        let! response = client.SendAsync request
        let! content = response.Content.ReadAsStringAsync()
        return content
    } |> runTask

let request (path: string) (body: string) = 
    makeRequest (postReq path body)

let ofJson<'t> (input: string) = 
    FableSaturnAdapter.deserialize<'t> input
let toJson (x: obj) = 
    FableSaturnAdapter.json x

let FableSaturnAdapterTests = 
    testList "FableSaturnAdapter tests" [
        testCase "String round trip" <| fun () ->   
            let input = "\"hello\""
            let output = makeRequest (postReq "/IProtocol/echoString" (toJson input))
            match ofJson<string> output with
            | "\"hello\"" -> pass()
            | otherwise -> failUnexpect otherwise 

        testCase "Int round trip" <| fun () ->
            [-2; -1; 0; 1; 2]
            |> List.map (fun input -> makeRequest (postReq "/IProtocol/echoInteger" (toJson input)))
            |> List.map (fun output -> ofJson<int> output)
            |> function 
                | [-2; -1; 0; 1; 2] -> pass()
                | otherwise -> failUnexpect otherwise  

        testCase "Option<int> round first trip" <| fun () ->
            [Some 2; None; Some -2]
            |> List.map (fun input -> makeRequest (postReq "/IProtocol/echoIntOption" (toJson input)))
            |> List.map (fun output -> ofJson<int option> output)
            |> function 
                | [Some 2; None; Some -2] -> pass()
                | otherwise -> failUnexpect otherwise

        testCase "bigint roundtrip" <| fun () ->
            [1I .. 5I]
            |> List.map (fun input -> makeRequest (postReq "/IProtocol/echoBigInteger" (toJson input)))
            |> List.map ofJson<bigint> 
            |> function 
                | xs when xs = [1I .. 5I] -> pass()
                | otherwise -> failUnexpect otherwise

        testCase "Option<string> round first trip" <| fun () ->
            [Some "hello"; None; Some "there"]
            |> List.map (fun input -> makeRequest (postReq "/IProtocol/echoStringOption" (toJson input)))
            |> List.map (fun output -> ofJson<string option> output)
            |> function 
                | [Some "hello"; None; Some "there"] -> pass()
                | otherwise -> failUnexpect otherwise

        testCase "Option<string> round second trip" <| fun () ->
            [Some "hello"; None; Some "there"]
            |> List.map (fun input -> makeRequest (postReq "/IProtocol/echoStringOption" (toJson input)))
            |> List.map ofJson<string option> 
            |> function 
                | [Some "hello"; None; Some "there"] -> pass()
                | otherwise -> failUnexpect otherwise

        testCase "bool round trip" <| fun () ->
            [true; false; true]
            |> List.map (fun input -> makeRequest (postReq "/IProtocol/echoBool" (toJson input)))
            |> List.map (fun output -> ofJson<bool> output)
            |> function 
                | [true; false; true] -> pass()
                | otherwise -> failUnexpect otherwise

        testCase "Generic union Maybe<int> round trip" <| fun () ->
            [Just 5; Nothing; Just -2]
            |> List.map (fun input -> makeRequest (postReq "/IProtocol/echoGenericUnionInt" (toJson input)))
            |> List.map (fun output -> ofJson<Maybe<int>> output)
            |> function 
                | [Just 5; Nothing; Just -2] -> pass()
                | otherwise -> failUnexpect otherwise

        testCase "Generic union Maybe<string> round trip" <| fun () ->
            [Just "hello"; Nothing; Just "there"; Just null]
            |> List.map (fun input -> makeRequest (postReq "/IProtocol/echoGenericUnionString" (toJson input)))
            |> List.map (fun output -> ofJson<Maybe<string>> output)
            |> function 
                | [Just "hello"; Nothing; Just "there"; Just null] -> pass()
                | otherwise -> failUnexpect otherwise

        testCase "Simple union round trip" <| fun () ->
            [A; B]
            |> List.map (fun input -> makeRequest (postReq "/IProtocol/echoSimpleUnion" (toJson input)))
            |> List.map (fun output -> ofJson<AB> output)
            |> function 
                | [A; B] -> pass()
                | otherwise -> failUnexpect otherwise

        testCase "List<int> round first trip" <| fun () ->
            [[]; [1 .. 5]]
            |> List.map (fun input -> makeRequest (postReq "/IProtocol/echoIntList" (toJson input)))
            |> List.map (fun output -> ofJson<int list> output)
            |> function 
                | [[]; [1;2;3;4;5]] -> pass()
                | otherwise -> failUnexpect otherwise

        testCase "List<int> round second trip" <| fun () ->
            [[1.5; 1.5; 3.0]]
            |> List.map (fun input -> makeRequest (postReq "/IProtocol/floatList" (toJson input)))
            |> List.map (fun output -> ofJson<float list> output)
            |> function 
                | [[1.5; 1.5; 3.0]] -> pass()
                | otherwise -> failUnexpect otherwise

        testCase "Unit as input with list result" <| fun () ->
            [(); ()]
            |> List.map (fun input -> makeRequest (postReq "/IProtocol/unitToInts" (toJson input)))
            |> List.map (fun output -> ofJson<int list> output)
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