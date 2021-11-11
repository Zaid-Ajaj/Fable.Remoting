namespace Functions

open Fable.Remoting.Server
open Fable.Remoting.AzureFunctions.Worker
open Microsoft.Azure.Functions.Worker
open Microsoft.Azure.Functions.Worker.Http
open Microsoft.Extensions.Logging

type Functions(log:ILogger<Functions>) =
    
    let webApp =
        Remoting.createApi()
        |> Remoting.withRouteBuilder FunctionsRouteBuilder.apiPrefix
        |> Remoting.withErrorHandler (fun ex routeInfo -> Propagate (sprintf "Message: %s, request body: %A" ex.Message routeInfo.requestBodyText))
        |> Remoting.fromValue Types.server
        |> Remoting.buildRequestHandler

    let webAppBinary =
        Remoting.createApi()
        |> Remoting.withRouteBuilder FunctionsRouteBuilder.apiPrefix
        |> Remoting.withErrorHandler (fun ex routeInfo -> Propagate (sprintf "Message: %s, request body: %A" ex.Message routeInfo.requestBodyText))
        |> Remoting.withBinarySerialization
        |> Remoting.fromValue Types.binaryServer
        |> Remoting.buildRequestHandler

    let otherWebApp =
        Remoting.createApi()
        |> Remoting.withRouteBuilder FunctionsRouteBuilder.apiPrefix
        |> Remoting.withErrorHandler (fun ex routeInfo -> Propagate (sprintf "Message: %s, request body: %A" ex.Message routeInfo.requestBodyText))
        |> Remoting.fromValue Types.implementation
        |> Remoting.buildRequestHandler

    [<Function("Index")>]
    member _.Index ([<HttpTrigger(AuthorizationLevel.Anonymous, Route = "{*any}")>] req: HttpRequestData, ctx: FunctionContext) =
        [
            webApp
            webAppBinary
            otherWebApp
        ]
        |> HttpResponseData.fromRequestHandlers req