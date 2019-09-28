open System
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.Extensions.Logging
open Giraffe
open ServerImpl
open SharedTypes
open Fable.Remoting.Server
open Fable.Remoting.Giraffe

let errorHandler (ex : Exception) (logger : ILogger) =
    logger.LogError(EventId(), ex, "An unhandled exception has occurred while executing the request.")
    clearResponse >=> setStatusCode 500 >=> text ex.Message


let docs = Docs.createFor<IServer>()

let serverDocs = 
  Remoting.documentation "Server Docs" [
    docs.route <@ fun api -> api.getLength @>
    |> docs.alias "Get Length"
    |> docs.description "Returns the length of the input string"
    |> docs.example <@ fun api -> api.getLength "example string" @>
    |> docs.example <@ fun api -> api.getLength "yet another example" @>
    |> docs.example <@ fun api -> api.getLength "" @>
    
    docs.route <@ fun api -> api.simpleUnit @>
    |> docs.alias "Simple Unit"
    |> docs.description "Unit as input"
  ]

let webApp = 
    Remoting.createApi() 
    |> Remoting.fromValue server 
    |> Remoting.withRouteBuilder routeBuilder 
    |> Remoting.withDocs "/api/server/docs" serverDocs
    |> Remoting.buildHttpHandler

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