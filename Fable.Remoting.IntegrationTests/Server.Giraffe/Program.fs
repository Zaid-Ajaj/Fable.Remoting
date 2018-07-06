open System
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Server.Kestrel
open Microsoft.Extensions.Logging
open Giraffe
open ServerImpl
open SharedTypes
open Fable.Remoting.Server
open Fable.Remoting.Giraffe

let errorHandler (ex : Exception) (logger : ILogger) =
    logger.LogError(EventId(), ex, "An unhandled exception has occurred while executing the request.")
    clearResponse >=> setStatusCode 500 >=> text ex.Message

let webApp = 
    Remoting.createApi() 
    |> Remoting.fromValue server 
    |> Remoting.withRouteBuilder routeBuilder 
    |> Remoting.buildHttpHanlder

let configureApp (app : IApplicationBuilder) =
    app.UseGiraffeErrorHandler(errorHandler)
       .UseGiraffe(choose [ webApp ])


[<EntryPoint>]
let main _ =
    WebHostBuilder()
        .UseKestrel()
        .Configure(Action<IApplicationBuilder> configureApp)
        .UseUrls("http://localhost:8080")
        .Build()
        .Run()
    0