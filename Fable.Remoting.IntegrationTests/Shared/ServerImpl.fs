module ServerImpl

open SharedTypes 
open System

module Async = 
    let result<'a> (x: 'a) : Async<'a> = 
        async { return x }
// Async.result : 'a -> Async<'a>
// a simple implementation, just return whatever value you get (echo the input)
let implementation : IServer  = {
    // primitive types
    getLength = fun input -> Async.result input.Length
    echoInteger = Async.result
    echoString = Async.result
    echoBool = Async.result
    echoIntOption = Async.result
    echoStringOption = Async.result
    echoGenericUnionInt = Async.result
    echoGenericUnionString = Async.result
    echoSimpleUnionType = Async.result

    echoRecord = Async.result
    echoGenericRecordInt = Async.result
    echoNestedGeneric = Async.result
        
    echoIntList = Async.result
    echoStringList = Async.result
    echoBoolList = Async.result
    echoListOfListsOfStrings = Async.result
    echoListOfGenericRecords = Async.result

    echoResult = Async.result
    echoBigInteger = Async.result
    throwError = fun () -> async { return! failwith "Generating custom server error" }
    echoMap = Async.result
}