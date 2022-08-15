# Asp.NET Core Integration Testing 

The following example demonstrates how the [Dotnet client](/client-setup/dotnet.md) can be used for integration testing of your Asp.Net core app whether it is Giraffe, Saturn or simple Asp.NET Core middleware. It uses the [Microsoft.AspNetCore.TestHost](https://www.nuget.org/packages/Microsoft.AspNetCore.TestHost) package to give us a custom `HttpClient` for testing. Then, we give this `HttpClient` to our dotnet client proxy: 

```fsharp
open System
open System.IO
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.TestHost
open Microsoft.AspNetCore.Http
open Fable.Remoting.Server
open Fable.Remoting.AspNetCore
open Fable.Remoting.DotnetClient
open Expecto
open Types
open System.Net
open Expecto.Logging
open Newtonsoft.Json.Linq

// the route builder 
let builder = sprintf "/api/%s/%s"

// protocol implementations 
let server : IServer = (* *)
let otherProtocol : IProcotol = (*  *)

let webApp = 
    Remoting.createApi()
    |> Remoting.withRouteBuilder builder
    |> Remoting.withErrorHandler (fun ex routeInfo -> Propagate ex.Message)
    |> Remoting.fromValue server

let otherWebApp = 
    Remoting.createApi()
    |> Remoting.withRouteBuilder builder 
    |> Remoting.withErrorHandler (fun ex routeInfo -> Propagate ex.Message)
    |> Remoting.fromValue otherProtocol 

// configure asp.net core middleware
let configureApp (app : IApplicationBuilder) =
    app.UseRemoting(webApp)
    app.UseRemoting(otherWebApp)  

// Creates a Host
let createHost() =
    WebHostBuilder()
        .UseContentRoot(Directory.GetCurrentDirectory())
        .Configure(Action<IApplicationBuilder> configureApp)

let testServer = new TestServer(createHost())

// custom HttpClient for testing
let client : HttClient = testServer.CreateClient()

// Create different proxies to different API's
let serverProxy : Proxy<IServer> = Proxy.custom<IServer> builder client
let protocolProxy : Proxy<IProtocol> = Proxy.custom<IProtocol> builder client 

// Now write your tests
let tests = 
    testList "IServer tests" [
        testCaseAsync "IServer.echoResult works" <| async {
            let input = Ok 42 
            let! output = serverProxy.call <@ fun server -> server.echoResult input @>
            Expect.equal input output "The results are the same" 
        }
    ]
```
This is using the old `Proxy.custom` function which takes in a route builder and a `HttpClient`, you can also use the new API with `Remoting.createApi` as follows:
```fsharp
// custom HttpClient for testing
let client : HttClient = testServer.CreateClient()

// Create a proxy for IServer
let server = 
    Remoting.createApi (client.BaseAddress.ToString())
    |> Remoting.withRouteBuilder builder
    |> Remoting.withHttpClient client
    |> Remoting.buildProxy<IServer>

// Now write your tests
let tests = 
    testList "IServer tests" [
        testCaseAsync "IServer.echoResult works" <| async {
            let input = Ok 42 
            let! output = server.echoResult input
            Expect.equal input output "The results are the same" 
        }
    ]
```