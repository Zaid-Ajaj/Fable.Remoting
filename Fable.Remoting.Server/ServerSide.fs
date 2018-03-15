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
        member this.withStatusCode(status) =
            {this with StatusCode=Some status}
        member this.withHeaders(headers) = 
            {this with Headers = Some headers}
        member this.withBody(body) = 
            {this with Body = Some body}
    
    type BuilderOptions<'ctx> = {
        Logger : (string -> unit) option
        ErrorHandler: ErrorHandler option
        Builder: string -> string -> string
        CustomHandlers : Map<string, 'ctx -> ResponseOverride option>
    }
    with
        static member Empty : BuilderOptions<'ctx> =
            {Logger = None; ErrorHandler = None; Builder = sprintf "/%s/%s"; CustomHandlers = Map.empty}
    [<AbstractClass>]
    type RemoteBuilderBase<'ctx,'handler>() =
        let fableConverter = FableJsonConverter()
        let writeLn text (sb: StringBuilder)  = sb.AppendLine(text)
        let toLogger logf = string >> logf
        let logDeserializationTypes logger (text: string) (inputType: System.Type[]) =
            logger |> Option.iter(fun logf ->
                StringBuilder()
                |> writeLn "Fable.Remoting:"
                |> writeLn "About to deserialize JSON:"
                |> writeLn text
                |> writeLn (sprintf "Into .NET Types: [%s]" (inputType |> Array.map (fun e -> e.FullName.Replace("+", ".")) |> String.concat ", "))
                |> writeLn ""
                |> toLogger logf)

        /// Deserialize a json string using FableConverter
        member __.Deserialize {Logger=logger} (json: string) (inputType: System.Type[]) (context:'ctx) (genericTypes:System.Type[]) =
            logDeserializationTypes logger json inputType
            let args = JArray.Parse json
            let serializer = JsonSerializer()
            serializer.Converters.Add fableConverter
            let converter = 
                match genericTypes with
                |[|a|] -> fun (o:JToken,t:System.Type) ->
                    if a.GUID = t.GUID && a.GUID = typeof<'ctx>.GUID then
                       box context
                    else o.ToObject(t,serializer)
                |_  -> fun (o:JToken,t:System.Type) -> o.ToObject(t,serializer)
            Seq.zip args inputType |> Seq.toArray |> Array.map converter
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
        /// Defines a `logger : (string -> unit)`
        [<CustomOperation("use_logger")>]
        member __.UseLogger(state,logger)=
            {state with Logger=Some logger}
        /// Defines an optional `logger : (string -> unit)` for backward compatibility
        [<CustomOperation("use_some_logger")>]
        [<System.Obsolete("For backward compatibility only.")>]
        member __.UseSomeLogger(state,logger)=
            {state with Logger=logger}
        /// Defines an error `handler : ErrorHandler`
        [<CustomOperation("use_error_handler")>]
        member __.UseErrorHandler(state,errorHandler)=
            {state with ErrorHandler=Some errorHandler}
        /// Defines an optional error `handler : ErrorHandler` for backward compatibility
        [<CustomOperation("use_some_error_handler")>]
        [<System.Obsolete("For backward compatibility only.")>]
        member __.UseSomeErrorHandler(state,errorHandler)=
            {state with ErrorHandler=errorHandler}
        [<CustomOperation("use_custom_handler_for")>]
        member __.UseCustomHandler(state,method,handler) =
            {state with CustomHandlers = state.CustomHandlers |> Map.add method handler }
            
    