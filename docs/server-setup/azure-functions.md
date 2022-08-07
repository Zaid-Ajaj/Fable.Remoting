### Azure Functions

To use Azure Functions in _isolated_ mode with custom `HttpTrigger` as serverless remoting server, just install:
```
dotnet add package Fable.Remoting.AzureFunctions.Worker
```

Since Azure Functions don't know anything about [HttpHandler](https://github.com/giraffe-fsharp/Giraffe/blob/master/DOCUMENTATION.md#httphandler) we need to use built-in `HttpRequestData` and `HttpResponseData` objects. Luckily we have `Remoting.buildRequestHandler` and `HttpResponseData.fromRequestHandler` functions to the rescue:
```fsharp
open Fable.Remoting.Server
open Fable.Remoting.AzureFunctions.Worker
open Microsoft.Azure.Functions.Worker
open Microsoft.Azure.Functions.Worker.Http
open Microsoft.Extensions.Logging

type Functions(log:ILogger<Functions>) =
    
    [<Function("Index")>]
    member _.Index ([<HttpTrigger(AuthorizationLevel.Anonymous, Route = "{*any}")>] req: HttpRequestData, ctx: FunctionContext) =
        Remoting.createApi()
        |> Remoting.withRouteBuilder FunctionsRouteBuilder.apiPrefix
        |> Remoting.fromValue myImplementation
        |> Remoting.buildRequestHandler
        |> HttpResponseData.fromRequestHandler req
```

Of course, having one implementation per Function App is not ideal, so `HttpResponseData.fromRequestHandlers` is here to the rescue:

```fsharp
type Functions(log:ILogger<Functions>) =
    
    [<Function("Index")>]
    member _.Index ([<HttpTrigger(AuthorizationLevel.Anonymous, Route = "{*any}")>] req: HttpRequestData, ctx: FunctionContext) =
        let handlerOne =
            Remoting.createApi()
            |> Remoting.withRouteBuilder FunctionsRouteBuilder.apiPrefix
            |> Remoting.fromValue myImplementationOne
            |> Remoting.buildRequestHandler
        
        let handlerTwo =
            Remoting.createApi()
            |> Remoting.withRouteBuilder FunctionsRouteBuilder.apiPrefix
            |> Remoting.fromValue myImplementationTwo
            |> Remoting.buildRequestHandler
        
        [ handlerOne; handlerTwo ] |> HttpResponseData.fromRequestHandlers req
```
