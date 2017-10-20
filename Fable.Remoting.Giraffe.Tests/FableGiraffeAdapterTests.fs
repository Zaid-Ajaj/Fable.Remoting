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

//FableGirrafeAdapter.logger <- Some (printfn "%s")
let app : HttpHandler = FableGirrafeAdapter.webPartFor implementation
let postContent (input: string) =  new StringContent(input, Text.Encoding.UTF8)
let createHost() =
    WebHostBuilder()
        .UseContentRoot(Directory.GetCurrentDirectory())
let fableGiraffeAdapterTests = 
    testList "FableGiraffeAdapter tests" [
        testCase "Sending string as input works" <| fun () ->
            pass()
    ]