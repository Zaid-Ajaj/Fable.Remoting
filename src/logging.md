# Logging

Logging is dead-simple to integrate with `Fable.Remoting`, if you are using Suave, then [Suave.SerilogExtensions](https://github.com/Zaid-Ajaj/Suave.SerilogExtensions) is what we recommend to use where [Serilog](https://github.com/Zaid-Ajaj/Suave.SerilogExtensions) is the logging framework of choice.

> Suave.SerilogExtensions is specially written to work with Fable.Remoting, although it would work just great with any Suave app.
> Logging extensions for Giraffe/Saturn are not yet available.

## Basic use case
1 - Install from Nuget:
```bash
dotnet add package Suave.SerilogExtensions
# or if you are using paket
mono .paket/paket.exe add Suave.SerilogExtensions
```
2 - Wrap your `remoting` WebPart with the Serilog adapter:
```fs
open System
// ...
open Suave.SerilogExtensions
open Suave 

// Log unhandled exceptions 
let errorHandler (ex: Exception) 
                 (routeInfo: RouteInfo<HttpContext>) =
    // get a contextual logger with RequestId attached to it
    let contextLogger = routeInfo.httpContext.Logger()
    // log the exception with relevant data
    let errorMsgTemplate = "Error occured while invoking {MethodName} at {RoutePath}"
    contextLogger.Error(ex, errorMsgTemplate, routeInfo.methodName, routeInfo.path)
    // No need to propagate custom errors back to client
    Ignore

// create the remoting WebPart
let webApp : WebPart = remoting musicStore {
    use_route_builder (sprintfn "/api/%s/%s")
    use_error_handler errorHandler 
}

// Enable logging
let webAppWithLogging : WebPart = SerilogAdapter.Enable(webApp)
```
3 - Create the logger and configure the sinks:
```fs
// configure Serilog
Log.Logger <- 
    LoggerConfiguration() 
      // Suave.SerilogExtensions has native destructuring mechanism
      // this helps Serilog deserialize the fsharp types like unions/records
      .Destructure.FSharpTypes()
      // use package Serilog.Sinks.Console  
      // https://github.com/serilog/serilog-sinks-console
      .WriteTo.Console() 
      // add more sinks etc.
      .CreateLogger() 
```
4 - Start the web server
```
startWebServer defaultConfig webAppWithLogging
```
[Suave.SerilogExtensions](https://github.com/Zaid-Ajaj/Suave.SerilogExtensions) contains many configuration options that you might want to check out. 

## Using logger from remoting functions
Ofcourse, we don't want to just log the http requests and responses but we also want to log application specific and custom logs. To use Serilog from the remoting functions, you need to access to `HttpContext` and call `httpContext.Logger()` to get a contexual logger you can log events from. 

> Logger() is an extension method provided from Suave.SerilogExtensions


Suppose your protocol had the type:
```fs
type IMusicStore = {
    albums : Async<list<Album>>
}
```
You can also gain access to the HttpContext from "outside" the remoting handler using the `context` helper function from Suave:
```fs
let webApp = context <| fun httpContext ->
    let logger = httpContext.Logger()
    let musicStore : IMusicStore = {
        albums = async {
            logger.Information("Retrieving albums from database")
            let! albums = Database.getAllAlbums()
            logger.Information("Read {AlbumCount} albums from database",
            List.length albums)
            return albums
        }
    }

    // create and return the remoting WebPart
    remoting musicStore {
        use_route_builder (sprintfn "/api/%s/%s")
        use_error_handler errorHandler 
    }

// Wrap with Serilog adapter
let webAppWithLogging = SerilogAdapter.Enable(webApp)

startWebServer webAppWithLogging
```