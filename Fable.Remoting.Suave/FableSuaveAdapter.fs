namespace Fable.Remoting.Suave

open Suave
open Suave.Filters
open Suave.Operators
open Suave.Successful

open FSharp.Reflection
open Newtonsoft.Json
open Fable.Remoting.Json
open Fable.Remoting.Server

module FableSuaveAdapter = 
    open System.Text

    let mutable logger : (string -> unit) option = None

    let private fableConverter = new FableJsonConverter()

    let private writeLn text (sb: StringBuilder)  = sb.AppendLine(text) |> ignore; sb
    let private write  (sb: StringBuilder) text   = sb.AppendLine(text) |> ignore

    let private logDeserialization (text: string) (inputType: System.Type) = 
        logger 
        |> Option.iter (fun log ->  
            StringBuilder()
            |> writeLn "Fable.Remoting:"
            |> writeLn "About to deserialize JSON:"
            |> writeLn text
            |> writeLn (sprintf "Into .NET Type: %s" inputType.FullName)
            |> writeLn ""
            |> fun sb -> log (sb.ToString())
        )
        

    /// Deserialize a json string using FableConverter
    let deserialize (json: string) (inputType: System.Type) =
        logDeserialization json inputType
        let parameterTypes = [| typeof<string>; typeof<System.Type>; typeof<JsonConverter array> |]
        let deserialize = typeof<JsonConvert>.GetMethod("DeserializeObject", parameterTypes) 
        let result = deserialize.Invoke(null, [| json; inputType; [| fableConverter |] |])
        result
           

    // Get data from request body and deserialize.
    // getResourceFromReq : HttpRequest -> obj
    let private getResourceFromReq (req : HttpRequest) (inputType: System.Type) =
        let json = System.Text.Encoding.UTF8.GetString req.rawForm
        deserialize json inputType
        
    // serialize an object to json using FableConverter
    // json : string -> WebPart
    let private json value =
      let result = JsonConvert.SerializeObject(value, fableConverter)
      
      StringBuilder()
      |> writeLn "Fable.Remoting: Returning serialized result back to client"
      |> writeLn result
      |> fun builder -> Option.iter (fun logf -> logf (builder.ToString())) logger
        
      OK result
      >=> Writers.setMimeType "application/json; charset=utf-8"

    let private handleRequest methodName serverImplementation = 
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
                let! dynamicResult = result
                return json dynamicResult
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
            POST >=> path fullPath >=> request (handleRequest methodName implementation)
        )
        |> List.ofSeq
        |> fun routes ->
            logger |> Option.iter (fun logf -> logf (builder.ToString()))
            choose routes
    
    /// Creates a WebPart from the given implementation of a protocol. Uses the default route builder: `sprintf "/%s/%s"`.
    let webPartFor implementation : WebPart = 
        webPartWithBuilderFor implementation (sprintf "/%s/%s")