module FableGiraffeAdapterTests

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
open Fable.Remoting.Giraffe
open System.Net.Http
open System
open Expecto
open Types

// Test helpers

let equal x y = Expect.equal true (x = y) (sprintf "%A = %A" x y)
let pass () = Expect.equal true true ""   
let fail () = Expect.equal false true ""
let failUnexpect (x: obj) = Expect.equal false true (sprintf "%A was not expected" x) 
//FableGirrafeAdapter.logger <- Some (printfn "%s")
let giraffeApp : HttpHandler = FableGiraffeAdapter.webPartFor implementation
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
    request.Content <- postContent body
    request

let runTask task =
    task
    |> Async.AwaitTask
    |> Async.RunSynchronously

let makeRequest (request : HttpRequestMessage) =
    use server = new TestServer(createHost())
    use client = server.CreateClient()
    request
    |> client.SendAsync
    |> runTask
    |> fun response ->
        let content = response.Content.ReadAsStringAsync() |> runTask
        content

let json (x: obj) = 
    FableGiraffeAdapter.json x

let fableGiraffeAdapterTests = 
    testList "FableGiraffeAdapter tests" [
        testCase "String round trip" <| fun () ->   
            let input = "\"hello\""
            let output = makeRequest (postReq "/echoString" (json input))
            match output with
            | "\"hello\"" -> pass()
            | otherwise -> failUnexpect otherwise 
    ]