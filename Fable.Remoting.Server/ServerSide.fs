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
        member __.Implementation = implementation
        
        /// Deserialize a json string using FableConverter
        member __.Deserialize {Logger=logger} (json: string) (inputType: System.Type[]) =
            logDeserializationTypes logger json inputType
            let args = JArray.Parse json
            let serializer = JsonSerializer()
            serializer.Converters.Add fableConverter
            Seq.zip args inputType |> Seq.toArray |> Array.map (fun (o,t) -> o.ToObject(t,serializer))
        
        member __.Json {Logger=logger} value =
          let result = JsonConvert.SerializeObject(value, fableConverter)
          logger |> Option.iter(fun logf ->
              StringBuilder()
              |> writeLn "Fable.Remoting: Returning serialized result back to client"
              |> writeLn result
              |> toLogger logf)            
          result
           
        member __.Yield(_) =  
            BuilderOptions.Empty
            
        [<CustomOperation("with_builder")>]
        member __.WithBuilder(state,builder)=
            {state with Builder=builder}
        [<CustomOperation("with_default")>]
        member __.WithDefault(state)=
            {state with Builder=BuilderOptions.Empty.Builder}
        [<CustomOperation("use_logger")>]
        member __.UseLogger(state,logger)=
            {state with Logger=Some logger}
        [<CustomOperation("use_some_logger")>]
        member __.UseSomeLogger(state,logger)=
            {state with Logger=logger}
        [<CustomOperation("use_error_handler")>]
        member __.UseErrorHandler(state,errorHandler)=
            {state with ErrorHandler=Some errorHandler}
        [<CustomOperation("use_some_error_handler")>]
        member __.UseSomeErrorHandler(state,errorHandler)=
            {state with ErrorHandler=errorHandler}



    let remoting = RemoteBuilder