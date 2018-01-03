namespace Fable.Remoting.Suave

open Suave
open Suave.Filters
open Suave.Operators
open Suave.Successful

open FSharp.Reflection
open Newtonsoft.Json

open Fable.Remoting.Json
open Fable.Remoting.Server

type RouteInfo = {
    path: string
    methodName: string
}

type ErrorResult = 
    | Ignore 
    | Propagate of obj

type ErrorHandler = System.Exception -> RouteInfo -> ErrorResult

module FableSuaveAdapter = 
    open System.Text

    let mutable logger : (string -> unit) option = None
    let private fableConverter = FableJsonConverter()
    let private writeLn text (sb: StringBuilder)  = sb.AppendLine(text) |> ignore; sb
    let private write  (sb: StringBuilder) text   = sb.AppendLine(text) |> ignore
    let private toString (sb: StringBuilder) = sb.ToString()
    let mutable private onErrorHandler : ErrorHandler option = None 
    let private toLogger (sb: StringBuilder) = 
        logger |> Option.iter(fun logf -> 
            sb
            |> toString
            |> logf
        )

    /// Global error handler that intercepts server errors and decides whether or not to propagate a message back to the client
    let onError (handler: ErrorHandler) = 
        onErrorHandler <- Some handler


    let private logDeserialization (text: string) (inputType: System.Type) = 
        StringBuilder()
        |> writeLn "Fable.Remoting:"
        |> writeLn "About to deserialize JSON:"
        |> writeLn text
        |> writeLn (sprintf "Into .NET Type: %s" (inputType.FullName.Replace("+", ".")))
        |> writeLn ""
        |> toLogger
        

    /// Deserialize a json string using FableConverter
    let deserialize (json: string) (inputType: System.Type) =
        logDeserialization json inputType
        let parameterTypes = [| typeof<string>; typeof<System.Type>; typeof<JsonConverter array> |]
        let deserialize = typeof<JsonConvert>.GetMethod("DeserializeObject", parameterTypes) 
        let result = deserialize.Invoke(null, [| json; inputType; [| fableConverter |] |])
        result
           

    // Get data from request body and deserialize.
    // getResourceFromReq : HttpRequest -> obj
    let private getResourceFromReq (req : HttpRequest) (inputType: System.Type)  =
        let json = System.Text.Encoding.UTF8.GetString req.rawForm
        deserialize json inputType
        
    // serialize an object to json using FableConverter
    // json : string -> WebPart
    let private json value isError =
      let result = JsonConvert.SerializeObject(value, fableConverter)
      
      StringBuilder()
      |> writeLn "Fable.Remoting: Returning serialized result back to client"
      |> writeLn result
      |> toLogger
        
      if not isError 
      then OK result >=> Writers.setMimeType "application/json; charset=utf-8"
      else OK result >=> Writers.setMimeType "application/json; charset=utf-8"
                     >=> Writers.setStatus HttpCode.HTTP_500
        
      
      

    let private handleRequest methodName serverImplementation routePath = 
        let inputType = ServerSide.getInputType methodName serverImplementation
        let hasArg = inputType.FullName <> "Microsoft.FSharp.Core.Unit"
        fun (req: HttpRequest) ->
            Option.iter (fun logf -> logf (sprintf "Fable.Remoting: Invoking method %s" methodName)) logger
            let requestBodyData = 
                // if input is unit
                // then don't bother getting any input from request
                match hasArg with 
                | true  -> getResourceFromReq req inputType
                | false -> null
            let result = ServerSide.dynamicallyInvoke methodName serverImplementation requestBodyData hasArg
            async {
                try
                  let! dynamicResult = result
                  return json dynamicResult false
                with 
                  | ex -> 
                     Option.iter (fun logf -> logf (sprintf "Server error at %s" routePath)) logger
                     let route : RouteInfo = { path = routePath; methodName = methodName  }
                     match onErrorHandler with
                     | Some handler ->
                        let result = handler ex route
                        match result with
                        | Ignore ->  return json "" false
                        | Propagate value ->  return json value true
                     | None -> return json "Server error" true
            }  
            |> Async.RunSynchronously
    
    /// Creates a `WebPart` from the given implementation of a protocol and a route builder to specify how to the paths should be built.
    let webPartWithBuilderFor implementation (routeBuilder: string -> string -> string) : WebPart = 
        let builder = StringBuilder()
        let typeName = implementation.GetType().Name
        write builder (sprintf "Building Routes for %s" typeName)
        implementation.GetType()
        |> FSharpType.GetRecordFields
        |> Seq.map (fun propInfo -> 
            let methodName = propInfo.Name
            let fullPath = routeBuilder typeName methodName
            write builder (sprintf "Record field %s maps to route %s" methodName fullPath)
            POST >=> path fullPath >=> request (handleRequest methodName implementation fullPath)
        )
        |> List.ofSeq
        |> fun routes ->
            builder |> toLogger
            choose routes
    
    /// Creates a WebPart from the given implementation of a protocol. Uses the default route builder: `sprintf "/%s/%s"`.
    let webPartFor implementation : WebPart = 
        webPartWithBuilderFor implementation (sprintf "/%s/%s")