namespace Fable.Remoting.Server

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
    open FSharp.Reflection
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

    type BuilderOptions<'ctx> = {
        Logger : (string -> unit) option
        ErrorHandler: ErrorHandler<'ctx> option
        Builder: string -> string -> string
    }
    with
        static member Empty : BuilderOptions<'ctx> =
            {Logger = None; ErrorHandler = None; Builder = sprintf "/%s/%s"}

    type RemoteBuilderBase<'ctx,'endpoint,'handler>(implementation, endpoints : (string -> ('ctx -> string -> Async<Choice<string,string>>) -> 'endpoint), joiner : 'endpoint list -> 'handler) =
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
                    let inputType = ServerSide.getInputType methodName implementation
                    let hasArg =
                        match inputType with
                        |[|inputType;_|] when inputType.FullName = "Microsoft.FSharp.Core.Unit" -> false
                        |_ -> true
                    let result =
                        let n args = ServerSide.dynamicallyInvoke methodName implementation args |> Async.Catch
                        fun ctx s ->
                            async {
                                let! r = n (if hasArg then deserialize state s inputType ctx t.GenericTypeArguments else [|null|])
                                match r with
                                | Choice1Of2 r -> return Choice1Of2 (serialize state r)
                                | Choice2Of2 ex ->
                                    Option.iter (fun logf -> logf (sprintf "Server error at %s" fullPath)) state.Logger
                                    let routeInfo : RouteInfo<'ctx> =
                                       {  path = fullPath
                                          methodName = methodName
                                          httpContext = ctx  }
                                    match state.ErrorHandler with
                                    | Some handler ->
                                       let result = handler ex routeInfo
                                       match result with
                                       // Server error ignored by error handler
                                       | Ignore ->
                                           let result = { error = "Server error: ignored"; ignored = true; handled = true }
                                           return Choice2Of2(serialize state result)
                                       // Server error mapped into some other `value` by error handler
                                       | Propagate value ->
                                           let result = { error = value; ignored = false; handled = true }
                                           return Choice2Of2(serialize state result)
                                    // There no server handler
                                    | None ->
                                       let result = { error = "Server error: not handled"; ignored = true; handled = false }
                                       return Choice2Of2(serialize state result)}

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

