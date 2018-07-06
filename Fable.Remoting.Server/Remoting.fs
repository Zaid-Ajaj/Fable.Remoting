namespace Fable.Remoting.Server

open System.Text
open Fable.Remoting.Json
open Newtonsoft.Json
open Newtonsoft.Json.Linq

module Remoting = 
    
    /// Starts with the default configuration for building an API 
    let createApi()  = 
        { Implementation = Empty 
          RouteBuilder = sprintf "/%s/%s" 
          ErrorHandler = None 
          DiagnosticsLogger = None 
          IoCContainer = None  }

    /// Defines how routes are built using the type name and method name. By default, the generated routes are of the form `/typeName/methodName`.
    let withRouteBuilder builder options = 
        { options with RouteBuilder = builder } 

    /// Enables the diagnostics logger that will log what steps the library is taking when a request comes in. This could help troubleshoot serialization issues but it could be also be interesting to see what is going on under the hood.
    let withLogger logger options = 
        { options with DiagnosticsLogger = Some logger }

    /// Ennables you to define a custom error handler for unhandled exceptions thrown by your remote functions. It can also be used for logging purposes or if you wanted to propagate errors back to client.
    let withErrorHandler handler options = 
        { options with ErrorHandler = Some handler }

    /// Builds the API using the provided static protocol implementation 
    let fromValue (serverImpl: 'implementation) options = 
        DynamicRecord.checkProtocolDefinition typeof<'implementation> 
        { options with Implementation = StaticValue serverImpl }

    /// Builds the API using a function that takes the incoming Http context and returns a protocol implementation. You can use the Http context to read information about the incoming request and also use the Http context to resolve dependencies using the underlying dependency injection mechanism.
    let fromContext (fromContextToValue: 'context -> 'implementation) options = 
        DynamicRecord.checkProtocolDefinition typeof<'implementation>
        { options with Implementation = FromContext fromContextToValue }

[<AutoOpen>]
module SharedCE =
    open FSharp.Reflection
    ///Settings for overriding the response
    type ResponseOverride = {
        StatusCode : int option
        Headers: Map<string,string> option
        Body: string option
        Abort: bool
    } with
        static member Default = {
            StatusCode = None
            Headers = None
            Body = None
            Abort = false
        }
        ///Don't handle the request
        static member Ignore = Some {ResponseOverride.Default with Abort=true}
        ///Defines the status code
        member this.withStatusCode(status) =
            {this with StatusCode=Some status}
        ///Defines the response headers
        member this.withHeaders(headers) =
            {this with Headers = Some headers}
        ///Defines the response body (prevents calling the original async workflow)
        member this.withBody(body) =
            {this with Body = Some body}
    type Response = {
        StatusCode : int
        Headers: Map<string,string>
        Body: string
    }
    type BuilderOptions<'ctx> = {
        Logger : (string -> unit) option
        ErrorHandler: ErrorHandler<'ctx> option
        Builder: string -> string -> string
        CustomHandlers : Map<string, 'ctx -> ResponseOverride option>
    }
    with
        static member Empty : BuilderOptions<'ctx> =
            {Logger = None; ErrorHandler = None; Builder = sprintf "/%s/%s"; CustomHandlers = Map.empty}

    type RemoteBuilderBase<'ctx,'endpoint,'handler>(implementation, endpoints : (string -> ('ctx -> string -> Async<Response> option) -> 'endpoint), joiner : 'endpoint list -> 'handler) =
        let fableConverter = FableJsonConverter()
        let writeLn text (sb: StringBuilder)  = sb.AppendLine(text)
        let toLogger logf = string >> logf
        let rec typePrinter (valueType: System.Type) =
            let simplifyGeneric = function
                | "Microsoft.FSharp.Core.FSharpOption" -> "Option"
                | "Microsoft.FSharp.Collections.FSharpList" -> "FSharpList"
                | "Microsoft.FSharp.Core.FSharpResult" -> "Result"
                | "Microsoft.FSharp.Collections.FSharpMap" -> "Map"
                | otherwise -> otherwise

            match valueType.FullName.Replace("+", ".") with
            | "System.String" -> "string"
            | "System.Boolean" -> "bool"
            | "System.Int32" -> "int"
            | "System.Double" -> "double"
            | "System.Numerics.BigInteger" -> "bigint"
            | "Microsoft.FSharp.Core.Unit" -> "unit"
            | "Suave.Http.HttpContext" -> "HttpContext"
            | "Microsoft.AspNetCore.Http.HttpContext" -> "HttpContext"
            | other ->
                match valueType.GetGenericArguments() with
                | [|  |] -> other
                | genericTypeArguments ->
                    let typeParts = other.Split('`')
                    let typeName = typeParts.[0]
                    Array.map typePrinter genericTypeArguments
                    |> String.concat ", "
                    |> sprintf "%s<%s>" (simplifyGeneric typeName)


        let logDeserializationTypes logger (text: unit -> string) (inputTypes: System.Type[]) =
            logger |> Option.iter(fun logf ->
                StringBuilder()
                |> writeLn "Fable.Remoting:"
                |> writeLn "About to deserialize JSON:"
                |> writeLn (text())
                |> writeLn "Into .NET Types:"
                |> writeLn (sprintf "[%s]" (inputTypes |> Array.map typePrinter |> String.concat ", "))
                |> writeLn ""
                |> toLogger logf)

        /// Deserialize a json string using FableConverter
        let deserialize { Logger = logger } (json: string) (inputTypes: System.Type[]) (context:'ctx) (genericTypes:System.Type[]) =
            let serializer = JsonSerializer()
            serializer.Converters.Add fableConverter
            // ignore the extra null arguments sent by client
            let args = Seq.zip (JArray.Parse json) inputTypes
            // Delayed logging: only log serialized data when a logger is configured
            logDeserializationTypes logger (fun () -> JsonConvert.SerializeObject(Seq.map fst args, fableConverter)) inputTypes
            // create a converter function that converts an array
            // of JSON arguments into a list of concrete .NET types
            let converter =
                match genericTypes with
                |[|a|] -> fun (o:JToken,t:System.Type) ->
                    if a.GUID = t.GUID && a.GUID = typeof<'ctx>.GUID then
                       box context
                    else o.ToObject(t,serializer)
                |_  -> fun (o:JToken,t:System.Type) -> o.ToObject(t,serializer)

            args
            |> Seq.toArray
            |> Array.map converter

        /// Serialize the value into a json string using FableConverter
        let serialize {Logger=logger} value =
          let result = JsonConvert.SerializeObject(value, fableConverter)
          logger |> Option.iter(fun logf ->
              StringBuilder()
              |> writeLn "Fable.Remoting: Returning serialized result back to client"
              |> writeLn result
              |> toLogger logf)
          result

        member __.Run(state) =
            let sb = StringBuilder()
            let t = implementation.GetType()
            let typeName =
                match t.GenericTypeArguments with
                |[||] -> t.Name
                |[|_|] -> t.Name.[0..t.Name.Length-3]
                |_ -> failwith "Only one generic type can be injected"
            sb.AppendLine(sprintf "Building Routes for %s" typeName) |> ignore
            implementation.GetType()
                |> FSharpType.GetRecordFields
                |> Seq.map (fun propInfo ->
                    let methodName = propInfo.Name
                    let fullPath = state.Builder typeName methodName
                    let recordFieldType = DynamicRecord.makeRecordFuncType propInfo.PropertyType
                    let hasArg =
                        match recordFieldType with
                        | NoArguments _ -> false 
                        | SingleArgument (input, _) when input.FullName = "Microsoft.FSharp.Core.Unit" -> false 
                        |_ -> true
                    
                    let inputTypes = 
                        match recordFieldType with 
                        | NoArguments _ -> [| |] 
                        | SingleArgument (input, _) -> [| input |] 
                        | ManyArguments (inputs, _) -> Array.ofList inputs 

                    let result =
                        let n args = 
                            let functions = DynamicRecord.createRecordFuncInfo implementation  
                            let func = Map.find methodName functions 
                            DynamicRecord.invokeAsync func implementation args |> Async.Catch
                        fun ctx s ->
                            let {Abort = abort; Headers = headers ; StatusCode = sc ; Body = body } =
                                state.CustomHandlers |> Map.tryFind methodName
                                |> Option.map (fun ch -> ch ctx) |> Option.flatten
                                |> Option.defaultValue ResponseOverride.Default
                            if abort then None
                            else
                             Some <|
                              async {
                                match body with
                                |Some body ->
                                    return { StatusCode = sc |> Option.defaultValue 200; Body = body; Headers = headers |> Option.defaultValue Map.empty}
                                |None ->
                                    let! r = n (if hasArg then deserialize state s inputTypes ctx t.GenericTypeArguments else [|null|])
                                    match r with
                                    | Choice1Of2 r -> return {StatusCode = sc |> Option.defaultValue 200; Body = (serialize state r); Headers = headers |> Option.defaultValue Map.empty}
                                    | Choice2Of2 ex ->
                                        Option.iter (fun logf -> logf (sprintf "Server error at %s" fullPath)) state.Logger
                                        let routeInfo : RouteInfo<'ctx> =
                                           {  Path = fullPath
                                              MethodName = methodName
                                              HttpContext = ctx  }
                                        match state.ErrorHandler with
                                        | Some handler ->
                                           let result = handler ex routeInfo
                                           match result with
                                           // Server error ignored by error handler
                                           | Ignore ->
                                               let result = { error = "Server error: ignored"; ignored = true; handled = true }
                                               return { StatusCode = sc |> Option.defaultValue 500; Body = serialize state result; Headers = headers |> Option.defaultValue Map.empty}
                                           // Server error mapped into some other `value` by error handler
                                           | Propagate value ->
                                               let result = { error = value; ignored = false; handled = true }
                                               return {StatusCode = sc |> Option.defaultValue 500; Body = serialize state result; Headers = headers |> Option.defaultValue Map.empty}
                                        // There no server handler
                                        | None ->
                                           let result = { error = "Server error: not handled"; ignored = true; handled = false }
                                           return {StatusCode = sc |> Option.defaultValue 500; Body = serialize state result; Headers = headers |> Option.defaultValue Map.empty}}
                    sb.AppendLine(sprintf "Record field %s maps to route %s" methodName fullPath) |> ignore
                    endpoints fullPath result)
                |> List.ofSeq
                |> fun routes ->
                    state.Logger |> Option.iter (fun logf -> string sb |> logf)
                    joiner routes
        member __.Zero() =
            BuilderOptions<'ctx>.Empty
        member __.Yield(_) =
            BuilderOptions<'ctx>.Empty
        /// Defines a custom builder that takes a `builder : (string -> string -> string)` that takes the typeName and methodNameto return a endpoint
        [<CustomOperation("with_builder")>]
        member __.WithBuilder(state,builder)=
            {state with Builder=builder}
        /// Defines a custom builder that takes a `builder : (string -> string -> string)` that takes the typeName and methodNameto return a endpoint
        [<CustomOperation("use_route_builder")>]
        member __.UseRouteBuilder(state,builder)=
            {state with Builder=builder}
        /// Defines a `logger : (string -> unit)`
        [<CustomOperation("use_logger")>]
        member __.UseLogger(state,logger)=
            {state with Logger=Some logger}
        /// Defines an error `handler : ErrorHandler`
        [<CustomOperation("use_error_handler")>]
        member __.UseErrorHandler(state,errorHandler)=
            {state with ErrorHandler=Some errorHandler}
        /// Defines a custom handler for a method that can override the response returning some `ResponseOverride`
        [<CustomOperation("use_custom_handler_for")>]
        member __.UseCustomHandler(state,method,handler) =
            {state with CustomHandlers = state.CustomHandlers |> Map.add method handler }

