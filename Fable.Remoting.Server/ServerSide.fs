﻿namespace Fable.Remoting.Server

open Fable.Remoting.Json
open System.Text
open Newtonsoft.Json.Linq
open Newtonsoft.Json

type IAsyncBoxer =
    abstract BoxAsyncResult : obj -> Async<obj>

type AsyncBoxer<'T>() =
    interface IAsyncBoxer with
        member __.BoxAsyncResult(boxedAsync: obj) : Async<obj> =
            match boxedAsync with
            | :? Async<'T> as unboxedAsyncOfGenericValueT ->
                async {
                    // this is of type 'T
                    let! unwrappedGenericValueTfromAsync  = unboxedAsyncOfGenericValueT
                    return box unwrappedGenericValueTfromAsync
                }
            | _ -> failwith "Invalid boxed value"


module ServerSide =

    open System
    open Fable.Remoting.Reflection
    let rec getFsharpFuncArgs (propType:System.Type) =
        if propType.GUID = typeof<Async<_>>.GUID then
            [|propType|]
        else
          [|match propType.GetGenericArguments() with
            |[|a; b|] when b.GUID = (typeof<FSharpFunc<_,_>>).GUID -> yield a;yield! getFsharpFuncArgs b
            |a -> yield! a |]

    let getInputType (methodName: string) implementation =
          let arr =
            implementation
                .GetType()
                .GetProperty(methodName)
                .PropertyType
            |> getFsharpFuncArgs
          match arr with
          |[|_|] -> [||]
          |arr -> arr.[..arr.Length-2]

    let dynamicallyInvoke (methodName: string) implementation methodArgs =
         let propInfo = implementation.GetType().GetProperty(methodName)
         // A -> Async<B>, extract A and B
         let propType = propInfo.PropertyType

         let fsharpFuncArgs = getFsharpFuncArgs propType
         // Async<B>
         let asyncOfB = fsharpFuncArgs |> Array.last
         // B
         let typeBFromAsyncOfB = asyncOfB.GetGenericArguments().[0]

         let boxer = typedefof<AsyncBoxer<_>>.MakeGenericType(typeBFromAsyncOfB)
                     |> Activator.CreateInstance
                     :?> IAsyncBoxer

         let fsAsync =
            match fsharpFuncArgs with
            |[|_|] -> propInfo.GetValue(implementation,null)
            |_ ->  FSharpRecord.Invoke (methodName, implementation, methodArgs)

         async {
            let! asyncResult = boxer.BoxAsyncResult fsAsync
            return asyncResult
         }

[<AutoOpen>]
module SharedCE =
    type RouteInfo<'ctx> = {
        path: string
        methodName: string
        httpContext: 'ctx
    }

    type ErrorResult =
        | Ignore
        | Propagate of obj

    type ErrorHandler<'ctx> = System.Exception -> RouteInfo<'ctx> -> ErrorResult

    type CustomErrorResult<'a> =
        { error: 'a;
          ignored: bool;
          handled: bool; }
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

    type BuilderOptions<'ctx> = {
        Logger : (string -> unit) option
        ErrorHandler: ErrorHandler<'ctx> option
        Builder: string -> string -> string
        CustomHandlers : Map<string, 'ctx -> ResponseOverride option>
    }
    with
        static member Empty : BuilderOptions<'ctx> =
            {Logger = None; ErrorHandler = None; Builder = sprintf "/%s/%s"; CustomHandlers = Map.empty}
    
    
    
    let internal fableConverter = FableJsonConverter()
    let internal writeLn text (sb: StringBuilder)  = sb.AppendLine(text)
    let internal toLogger logf = string >> logf
    let rec internal typePrinter (valueType: System.Type) = 
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


    let internal logDeserializationTypes logger (text: unit -> string) (inputTypes: System.Type[]) =
        logger |> Option.iter(fun logf ->
            StringBuilder()
            |> writeLn "Fable.Remoting:"
            |> writeLn "About to deserialize JSON:"
            |> writeLn (text())
            |> writeLn "Into .NET Types:"
            |> writeLn (sprintf "[%s]" (inputTypes |> Array.map typePrinter |> String.concat ", "))
            |> writeLn ""
            |> toLogger logf)
    
    [<AbstractClass>]
    type RemoteBuilderBase<'ctx,'handler>() =
        /// Deserialize a json string using FableConverter
        member __.Deserialize { Logger = logger } (json: string) (inputTypes: System.Type[]) (context:'ctx) (genericTypes:System.Type[]) =
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
        member __.Json {Logger=logger} value =
          let result = JsonConvert.SerializeObject(value, fableConverter)
          logger |> Option.iter(fun logf ->
              StringBuilder()
              |> writeLn "Fable.Remoting: Returning serialized result back to client"
              |> writeLn result
              |> toLogger logf)
          result

        abstract member Run : BuilderOptions<'ctx> -> 'handler
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
        [<CustomOperation("use_custom_handler_for")>]
        /// Defines a custom handler for a method that can override the response returning some `ResponseOverride`
        member __.UseCustomHandler(state,method,handler) =
            {state with CustomHandlers = state.CustomHandlers |> Map.add method handler }
    
    type SocketBuilderOptions = {
        SocketBuilder: string -> string
    }
    with
        static member Empty : SocketBuilderOptions =
            { SocketBuilder = sprintf "/%s"}

    type Result = {
        Id: int
        Result: obj
    }

    type Request = {
        Id: int
        Method: string
        Arguments: obj array
    }
   
    [<AbstractClass>]
    type SocketBuilderBase<'handler>(implementation) as sbb =
        let mailbox =
          MailboxProcessor<Request>.Start (
            fun mb ->
                let rec loop () =
                    async {
                        let! ({Id=id;Method=methodName;Arguments=args}) = mb.Receive()
                        let result = ServerSide.dynamicallyInvoke methodName implementation args
                        async {
                            let! r = result
                            let rsp = {Id=id;Result=r}
                            let srl = sbb.Json rsp
                            do! sbb.Send srl
                        } |> Async.Start

                        return! loop ()
                    }
                loop ())
        
        abstract member Send: string -> Async<unit>
        abstract member CreateWebSocket: MailboxProcessor<Request> -> string -> 'handler
        
        /// Deserialize a json string using FableConverter
        member __.Deserialize (json: string) =            
            JsonConvert.DeserializeObject(json,typeof<Request>,fableConverter) :?> Request

        /// Serialize the value into a json string using FableConverter
        member __.Json value =
          JsonConvert.SerializeObject(value, fableConverter)
          

        member sbb.Run {SocketBuilder = builder} =   
            let name =      
                let t = implementation.GetType()
                match t.GenericTypeArguments with
                |[||] -> t.Name
                |_ -> failwith "No support for generic record"
            name |>
            builder |>         
            sbb.CreateWebSocket mailbox 
        member __.Zero() =
            SocketBuilderOptions.Empty
        member __.Yield(_) =
            SocketBuilderOptions.Empty
        /// Defines a custom builder that takes a `builder : (string -> string -> string)` that takes the typeName and methodNameto return a endpoint
        [<CustomOperation("with_builder")>]
        member __.WithBuilder(state,builder)=
            {state with SocketBuilder=builder}
        /// Defines a custom builder that takes a `builder : (string -> string -> string)` that takes the typeName and methodNameto return a endpoint
        [<CustomOperation("use_socket_builder")>]
        member __.UseSocketBuilder(state,builder)=
            {state with SocketBuilder=builder}