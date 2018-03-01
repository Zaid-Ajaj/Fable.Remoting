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
         // A
         let argumentType = fsharpFuncArgs.[0]

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