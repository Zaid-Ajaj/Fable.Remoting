module ServerImpl

open SharedTypes
open System

module Async =
    let result<'a> (x: 'a) : Async<'a> =
        async { return x }
// Async.result : 'a -> Async<'a>
// a simple implementation, just return whatever value you get (echo the input)
let server : IServer  = {
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
    multiArgFunc = fun str n b -> async { return str.Length + n + (if b then 1 else 0) }
    overriddenFunction = fun str -> async { return! failwith str }
    customStatusCode = fun () -> async {return "No content"}
}

let versionTestServer : IVersionTestServer = {
    v4 = fun () -> async {return "v4"}
    v3 = fun () -> async {return "v3"}
    v2 = fun () -> async {return "v2"}
    v1 = fun () -> async {return "v1"}
}
