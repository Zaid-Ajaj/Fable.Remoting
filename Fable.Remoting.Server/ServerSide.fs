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
    let rec getFsharpFuncArgs (propType:System.Type) = [|
            match propType.GetGenericArguments() with
            |[|a; b|] when b.GUID = (typeof<FSharpFunc<_,_>>).GUID -> yield a;yield! getFsharpFuncArgs b
            |a -> yield! a |]

    let getInputType (methodName: string) implementation =
          let arr =
            implementation
                .GetType()
                .GetProperty(methodName)
                .PropertyType
            |> getFsharpFuncArgs

          arr.[..arr.Length-2]

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

         let fsAsync = FSharpRecord.Invoke (methodName, implementation, methodArgs)

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
    type BuilderOptions = {
        Logger : (string -> unit) option
        ErrorHandler: ErrorHandler option
        Builder: string -> string -> string
    }
    with
        static member Empty =
            {Logger = None; ErrorHandler = None; Builder = sprintf "/%s/%s"}

    type RemoteBuilder<'a>(implementation: 'a) =
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
        /// Exposes the implementation to the builder
        member __.Implementation = implementation

        /// Deserialize a json string using FableConverter
        member __.Deserialize {Logger=logger} (json: string) (inputType: System.Type[]) =
            logDeserializationTypes logger json inputType
            let args = JArray.Parse json
            let serializer = JsonSerializer()
            serializer.Converters.Add fableConverter
            Seq.zip args inputType |> Seq.toArray |> Array.map (fun (o,t) -> o.ToObject(t,serializer))
        /// Serialize the value into a json string using FableConverter
        member __.Json {Logger=logger} value =
          let result = JsonConvert.SerializeObject(value, fableConverter)
          logger |> Option.iter(fun logf ->
              StringBuilder()
              |> writeLn "Fable.Remoting: Returning serialized result back to client"
              |> writeLn result
              |> toLogger logf)
          result

        member __.Zero() =
            BuilderOptions.Empty
        member __.Yield(_) =
            BuilderOptions.Empty
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
    /// Computation expression to create a remoting server. Needs to open Fable.Remoting.Suave or Fable.Remoting.Giraffe for actual implementation
    /// Usage:
    /// `let server = remoting implementation {()}` for default options at /typeName/methodName
    /// `let server = remoting implementation = remoting {`
    /// `    with_builder builder` to set a `builder : (string -> string -> string)`
    /// `    use_logger logger` to set a `logger : (string -> unit)`
    /// `    use_error_handler handler` to set a `handler : (System.Exception -> RouteInfo -> ErrorResult)` in case of a server error
    /// `}`
    let remoting = RemoteBuilder