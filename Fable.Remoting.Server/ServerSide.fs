namespace Fable.Remoting.Server

open FSharp.Reflection

type IAsyncBoxer =  
    abstract BoxAsyncResult : obj -> Async<obj>

type AsyncBoxer<'T>() = 
    interface IAsyncBoxer with
        member this.BoxAsyncResult(boxedAsync: obj) : Async<obj> = 
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
    
    let getInputType (methodName: string) implementation = 
            implementation
                .GetType()
                .GetProperty(methodName)
                .PropertyType
                .GetGenericArguments().[0]

    let dynamicallyInvoke (methodName: string) implementation methodArg hasArg =
         let propInfo = implementation.GetType().GetProperty(methodName)
         // A -> Async<B>, extract A and B
         let propType = propInfo.PropertyType 
         let fsharpFuncArgs = propType.GetGenericArguments()
         // A
         let argumentType = fsharpFuncArgs.[0]

         // Async<B>
         let asyncOfB = fsharpFuncArgs.[1]
         // B
         let typeBFromAsyncOfB = asyncOfB.GetGenericArguments().[0]

         let boxer = typedefof<AsyncBoxer<_>>.MakeGenericType(typeBFromAsyncOfB)
                     |> Activator.CreateInstance 
                     :?> IAsyncBoxer

         let fsAsync = FSharpRecord.Invoke (methodName, implementation, methodArg, hasArg)
        
         async { 
            let! asyncResult = boxer.BoxAsyncResult fsAsync
            return asyncResult
         }