namespace Fable.Remoting.Giraffe

open FSharp.Reflection
open Newtonsoft.Json
open Fable.Remoting.Json
open Fable.Remoting.Server
open Microsoft.AspNetCore.Http
open Giraffe.HttpHandlers
open Giraffe.Tasks

type RouteInfo = {
    path: string
    methodName: string
}

type ErrorResult = 
    | Ignore 
    | Propagate of obj

type ErrorHandler = System.Exception -> RouteInfo -> ErrorResult

type CustomErrorResult<'a> =
    { error: 'a; 
      ignored: bool;
      handled: bool; }

module FableGiraffeAdapter =

    open System.Text
    open System.IO
    let mutable logger : (string -> unit) option = None
    let private fableConverter = FableJsonConverter()
    let private writeLn text (sb: StringBuilder)  = sb.AppendLine(text) |> ignore; sb
    let private write  (sb: StringBuilder) text   = sb.AppendLine(text) |> ignore
    let private toString (sb: StringBuilder) = sb.ToString()
    let mutable private onErrorHandler : ErrorHandler option = None 
    /// Global error handler that intercepts server errors and decides whether or not to propagate a message back to the client
    let onError (handler: ErrorHandler) = 
        onErrorHandler <- Some handler
    let private toLogger (sb: StringBuilder) = 
        logger |> Option.iter(fun logf -> 
            sb
            |> toString
            |> logf
        )

    let private logDeserialization (text: string) (inputType: System.Type) = 
        StringBuilder()
        |> writeLn "Fable.Remoting:"
        |> writeLn "About to deserialize JSON:"
        |> writeLn text
        |> writeLn (sprintf "Into .NET Type: %s" (inputType.FullName.Replace("+", ".")))
        |> writeLn ""
        |> toLogger

    let private logSerializedResult (json: string) = 
        StringBuilder()
        |> writeLn "Fable.Remoting: Returning serialized result back to client"
        |> writeLn json
        |> toLogger
        

    /// Deserialize a json string using FableConverter
    let deserializeByType (json: string) (inputType: System.Type) =
        logDeserialization json inputType
        let parameterTypes = [| typeof<string>; typeof<System.Type>; typeof<JsonConverter array> |]
        let deserialize = typeof<JsonConvert>.GetMethod("DeserializeObject", parameterTypes) 
        let result = deserialize.Invoke(null, [| json; inputType; [| fableConverter |] |])
        result

    let deserialize<'t> (json: string) : 't = 
        JsonConvert.DeserializeObject<'t>(json, fableConverter)

    // serialize an object to json using FableConverter
    // json : string -> WebPart
    let json value =
      let result = JsonConvert.SerializeObject(value, fableConverter)
      logSerializedResult result
      result

    // Get data from request body and deserialize.
    // getResourceFromReq : HttpRequest -> obj
    let getResourceFromReq (ctx : HttpContext) (inputType: System.Type) =
        let requestBodyStream = ctx.Request.Body
        use streamReader = new StreamReader(requestBodyStream)
        let requestBodyContent = streamReader.ReadToEnd()
        deserializeByType requestBodyContent inputType
    
    let handleRequest methodName serverImplementation routePath = 
        let inputType = ServerSide.getInputType methodName serverImplementation
        let hasArg = inputType.FullName <> "Microsoft.FSharp.Core.Unit"
        fun (next : HttpFunc) (ctx : HttpContext) ->
            Option.iter (fun logf -> logf (sprintf "Fable.Remoting: Invoking method %s" methodName)) logger
            let requestBodyData = 
                match hasArg with 
                | true  -> getResourceFromReq ctx inputType
                | false -> null
                
            let result = ServerSide.dynamicallyInvoke methodName serverImplementation requestBodyData hasArg

            task {
                try 
                  let! unwrappedFromAsync = result
                  let serializedResult = json unwrappedFromAsync
                  ctx.Response.StatusCode <- 200
                  return! text serializedResult next ctx
                with
                  | ex ->
                     ctx.Response.StatusCode <- 500
                     Option.iter (fun logf -> logf (sprintf "Server error at %s" routePath)) logger
                     match onErrorHandler with
                     | Some handler -> 
                        let routeInfo = { path = routePath; methodName = methodName }
                        match handler ex routeInfo with
                        | Ignore -> 
                            let result = { error = "Server error: ignored"; ignored = true; handled = true }
                            return! text (json result) next ctx
                        | Propagate value -> 
                            let result = { error = value; ignored = false; handled = true }
                            return! text (json result) next ctx
                     | None -> 
                        let result = { error = "Server error: not handled"; ignored = false; handled = true }
                        return! text (json result) next ctx
            }

    let httpHandlerWithBuilderFor<'t> (implementation: 't) (routeBuilder: string -> string -> string) : HttpHandler = 
            let builder = StringBuilder()
            let typeName = implementation.GetType().Name
            write builder (sprintf "Building Routes for %s" typeName)
            implementation.GetType()
            |> FSharpType.GetRecordFields
            |> Seq.map (fun propInfo -> 
                let methodName = propInfo.Name
                let fullPath = routeBuilder typeName methodName
                write builder (sprintf "Record field %s maps to route %s" methodName fullPath)
                POST >=> route fullPath 
                     >=> warbler (fun _ -> handleRequest methodName implementation fullPath)
            )
            |> List.ofSeq
            |> fun routes ->
                builder |> toLogger
                choose routes

    let httpHandlerFor<'t> (implementation : 't) : HttpHandler = 
        httpHandlerWithBuilderFor implementation (sprintf "/%s/%s")