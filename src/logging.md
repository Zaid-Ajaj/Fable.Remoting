# Logging

Logging is easy to integrate with `Fable.Remoting`, in this section, [Serilog](https://github.com/serilog/serilog) will be the recommended logging framework of choice. Dependending on which web framework you are using, you can pick one of:
 - [Suave.SerilogExtensions](https://github.com/Zaid-Ajaj/Suave.SerilogExtensions) for Suave
 - [Giraffe.SerilogExtensions](https://github.com/Zaid-Ajaj/Giraffe.SerilogExtensions) for Giraffe/Saturn

Since both serilog extensions have the same API, I will only go through an example for Suave, the exact same code can be used with Giraffe if you just change `Remoting.buildWebPart` to `Remoting.buildHttpHandler` and `WebPart` type signatures to `HttpHandler`:

## Basic use case
1 - Install from Nuget:
```bash
dotnet add package Suave.SerilogExtensions
# or if you are using paket
.paket/paket.exe add Suave.SerilogExtensions
```
2 - Wrap your `WebPart` given by `Remoting` with `SerilogAdaper.Enable` function:
```fs  
open System
// ...
open Suave.SerilogExtensions
open Suave  
 
// Log unhandled exceptions 
let errorHandler (ex: Exception)  
                 (routeInfo: RouteInfo<HttpContext>) =
    // get a contextual logger with RequestId attached to it
    // .Logger() is an extension from Suave.SerilogExtensions
    let contextLogger = routeInfo.httpContext.Logger()
    // log the exception with relevant data
    let errorMsgTemplate = "Error occured while invoking {MethodName} at {RoutePath}"
    contextLogger.Error(ex, errorMsgTemplate, routeInfo.methodName, routeInfo.path)
    // No need to propagate custom errors back to client
    Ignore 

// create the WebPart
let webApp : WebPart = 
    Remoting.createApi()
    |> Remoting.fromValue musicStore
    |> Remoting.withRouteBuilder (sprintfn "/api/%s/%s") 
    |> Remoting.withErrorHandler errorHandler 
    |> Remoting.buildWebPart

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
Ofcourse, we don't want to just log the http requests and responses but we also want to log application specific and custom logs. To use Serilog from the remote functions, you need to [Access to `HttpContext`](request-context.md) and call `httpContext.Logger()` to get a contexual logger you can log events from. 

> Logger() is an extension method provided from Suave.SerilogExtensions


Suppose your protocol had the type:
```fs
type IMusicStore = {
    albums : Async<list<Album>>
}
```
To gain access to the `HttpContext`, use it as a parameter to end up with `HttpContext -> IMusicStore`:
```fs
// The musicStore value is dependent on the ILogger
let musicStore (logger: ILogger) : IMusicStore = {
    albums = async {
        logger.Information("Retrieving albums from database")
        let! albums = Database.getAllAlbums()
        logger.Information("Read {AlbumCount} albums from database", List.length albums)
        return albums
    }
} 

// Get the logger from the context
let musicStoreApi (context: HttpContext) : IMusicStore = 
    let logger : ILogger = context.Logger()
    musicStore logger 

// create the WebPart using 'fromContext' instead of 'fromValue' to gain access to the context
let webApp : WebPart = 
    Remoting.createApi()
    |> Remoting.fromContext musicStoreApi
    |> Remoting.withRouteBuilder (sprintfn "/api/%s/%s") 
    |> Remoting.withErrorHandler errorHandler 
    |> Remoting.buildWebPart

// Wrap with Serilog adapter
let webAppWithLogging = SerilogAdapter.Enable(webApp)

startWebServer defaultConfig webAppWithLogging
```