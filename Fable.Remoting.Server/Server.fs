namespace Fable.Remoting.Server

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
            

module Server = 

    open System
    open Fable.Remoting.Reflection

    let dynamicallyInvoke (methodName: string) implementation methodArg =
         let propInfo = implementation.GetType().GetProperty(methodName)
         // A -> Async<B>, extract A and B
         let propType = propInfo.PropertyType 
         let fsharpFuncArgs = propType.GetGenericArguments()
         // A
         let argumentType = fsharpFuncArgs.[0]
         if (argumentType <> methodArg.GetType()) then
            let expectedTypeName = argumentType.Name
            let providedTypeName = methodArg.GetType().Name
            let errorMsg = sprintf "Expected method argument of '%s' but instead got '%s'" expectedTypeName providedTypeName
            failwith errorMsg
         // Async<B>
         let asyncOfB = fsharpFuncArgs.[1]
         // B
         let typeBFromAsyncOfB = asyncOfB.GetGenericArguments().[0]

         let boxer = typedefof<AsyncBoxer<_>>.MakeGenericType(typeBFromAsyncOfB)
                     |> Activator.CreateInstance 
                     :?> IAsyncBoxer

           
         let fsAsync = FSharpRecord.Invoke (methodName, implementation, methodArg)
               
         async { 
            let! asyncResult = boxer.BoxAsyncResult fsAsync
            return asyncResult
         }