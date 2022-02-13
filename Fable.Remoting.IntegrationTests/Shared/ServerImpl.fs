module ServerImpl

open SharedTypes
open System
open System.Threading.Tasks

module Async =
    let result<'a> (x: 'a) : Async<'a> =
        async { return x }


let simpleServer : ISimpleServer = {
    getLength = fun input -> Async.result input.Length
}

let cookieServer readCookie : ICookieServer = {
    checkCookie = fun () -> readCookie () |> Async.result
}

let getInt : unit -> int =
    let mutable i = 0
    fun () ->
        i <- i + 1
        i

let serverBinary : IBinaryServer = {
    // primitive types
    simpleUnit = fun () -> async { return 42 }
    returnUnit = fun () -> async { return () }
    intToUnit = fun n -> async { return () }
    tupleToUnit = fun (a, b) -> async { return () }
    tupleToTuple = fun (a,b) -> async { return (b, a) }
    getLength = fun input -> Async.result input.Length
    binaryContent = fun () -> async { return [| byte 1; byte 2; byte 3 |] }
    binaryInputOutput = Async.result
    privateConstructor = Async.result
    echoInteger = Async.result
    echoString = Async.result
    echoEnum = Async.result
    echoStringEnum = Async.result
    echoBool = Async.result
    echoTimeSpan = Async.result
    echoIntWithMeasure = Async.result
    echoInt16WithMeasure = Async.result
    echoInt64WithMeasure = Async.result
    echoDecimalWithMeasure = Async.result
    echoFloatWithMeasure = Async.result
    echoDateTime = Async.result
    echoDateTimeOffset = Async.result
    echoGuid = Async.result
    echoIntOption = Async.result
    echoIntOptionOption = Async.result
    echoStringValueOption = Async.result
    echoStringOption = Async.result
    echoGenericUnionInt = Async.result
    echoGenericUnionString = Async.result
    echoSimpleUnionType = Async.result
    echoGenericMap = Async.result
    genericDictionary = fun () -> async { return Map.ofList [ "firstKey", Just 5; "secondKey", Nothing ] |> System.Collections.Generic.Dictionary<_, _> }
    echoRecord = Async.result
    echoRemoteWorkEntity = Async.result
    echoAnonymousRecord = Async.result
    echoNestedAnonRecord = Async.result
    echoRecordWithStringOption = Async.result
    echoTree = Async.result
    echoGenericRecordInt = Async.result
    echoNestedGeneric = Async.result
    echoRecursiveRecord = Async.result
    echoOtherDataC = Async.result
    echoIntList = Async.result
    echoStringList = Async.result
    echoMaybeBoolList = Async.result
    echoBoolList = Async.result
    mapRecordAsKey = fun () -> async { return Map.ofList [ { Key = 1; Value = "Value" }, 1 ] }
    setRecordAsValue = fun () -> async { return Set.ofList [ { Key = 1; Value = "Value" } ] }
    echoListOfListsOfStrings = Async.result
    echoListOfGenericRecords = Async.result
    tuplesAndLists = fun (dict, xs) ->
        xs
        |> List.map (fun x -> x, x.Length)
        |> List.append (Map.toList dict)
        |> Map.ofList
        |> Async.result

    echoResult = Async.result
    echoSingleCase = Async.result
    echoHighScores = Async.result
    echoArray3tuples = Async.result
    getHighScores = fun () -> async {
        return [|
            { Name = "alfonsogarciacaro"; Score =  100 }
            { Name = "theimowski"; Score =  28 }
        |]
    }
    echoBigInteger = Async.result
    throwError = fun () -> async { return! failwith "Generating custom server error" }
    throwBinaryError = fun () -> async { return! failwith "Generating custom server error for binary response" }
    echoMap = Async.result
    echoTupleMap = Async.result
    echoSet = Async.result
    echoTupleSet = Async.result
    multiArgFunc = fun str n b -> async { return str.Length + n + (if b then 1 else 0) }
    overriddenFunction = fun str -> async { return! failwith str }
    customStatusCode = fun () -> async {return "No content"}
    pureAsync = async {return 42}
    asyncNestedGeneric = async {
        return {
            Value = Just (Some "value")
            OtherValue = 10
        }
    }

    multiArgComplex = fun flag value -> Async.result value
    echoPrimitiveLong = Async.result
    echoComplexLong = Async.result
    echoOptionalLong =  Async.result
    echoSingleDULong = Async.result
    echoLongInGenericUnion = Async.result
    mapDateTimeOffsetAsKey = Async.result
    echoBigIntKeyMap = Async.result
    echoDecimalKeyMap = Async.result
    echoLongKeyMap = Async.result
    echoIntKeyMap = Async.result
    echoTimeOnlyMap = Async.result
    echoDateOnlyMap = Async.result
    echoRecordWithChar = Async.result

    pureTask = Task.FromResult 42
    echoMapTask = fun map -> async { return map } |> Async.StartAsTask }

// Async.result : 'a -> Async<'a>
// a simple implementation, just return whatever value you get (echo the input)
let server : IServer  = {
    // primitive types
    simpleUnit = fun () -> async { return 42 }
    returnUnit = fun () -> async { return () }
    intToUnit = fun n -> async { return () }
    tupleToUnit = fun (a, b) -> async { return () }
    tupleToTuple = fun (a,b) -> async { return (b, a) }
    getLength = fun input -> Async.result input.Length
    getSeq = fun () -> async { return seq { yield (Just 5); yield Nothing }  }
    binaryContent = fun () -> async { return [| byte 1; byte 2; byte 3 |] }
    binaryInputOutput = Async.result
    privateConstructor = Async.result
    echoInteger = Async.result
    echoString = Async.result
    echoBool = Async.result
    echoDateTime = Async.result
    echoDateTimeOffset = Async.result
    echoIntOption = Async.result
    echoUnionOfOtherUnions = Async.result
    echoToken = Async.result
    echoStringOption = Async.result
    echoGenericUnionInt = Async.result
    echoGenericUnionString = Async.result
    echoSimpleUnionType = Async.result
    echoGenericMap = Async.result
    echoTestCommand = Async.result
    echoRecord = Async.result
    echoRemoteWorkEntity = Async.result
    echoAnonymousRecord = Async.result
    echoNestedAnonRecord = Async.result
    echoTree = Async.result
    echoGenericRecordInt = Async.result
    echoNestedGeneric = Async.result
    echoOtherDataC = Async.result
    echoRecursiveRecord = Async.result
    echoIntList = Async.result
    echoStringList = Async.result
    echoBoolList = Async.result
    mapRecordAsKey = fun () -> async { return Map.ofList [ { Key = 1; Value = "Value" }, 1 ] }
    setRecordAsValue = fun () -> async { return Set.ofList [ { Key = 1; Value = "Value" } ] }
    echoListOfListsOfStrings = Async.result
    echoListOfGenericRecords = Async.result
    echoRecordWithStringOption = Async.result
    tuplesAndLists = fun (dict, xs) ->
        xs
        |> List.map (fun x -> x, x.Length)
        |> List.append (Map.toList dict)
        |> Map.ofList
        |> Async.result

    echoResult = Async.result
    echoSingleCase = Async.result
    echoHighScores = Async.result
    getHighScores = fun () -> async {
        return [|
            { Name = "alfonsogarciacaro"; Score =  100 }
            { Name = "theimowski"; Score =  28 }
        |]
    }
    echoBigInteger = Async.result
    throwError = fun () -> async { return! failwith "Generating custom server error" }
    throwBinaryError = fun () -> async { return! failwith "Generating custom server error for binary response" }
    echoMap = Async.result
    echoTupleMap = Async.result
    echoSet = Async.result
    echoTupleSet = Async.result
    multiArgFunc = fun str n b -> async { return str.Length + n + (if b then 1 else 0) }
    overriddenFunction = fun str -> async { return! failwith str }
    customStatusCode = fun () -> async {return "No content"}
    pureAsync = async {return 42}
    asyncNestedGeneric = async {
        return {
            Value = Just (Some "value")
            OtherValue = 10
        }
    }

    multiArgComplex = fun flag value -> Async.result value
    echoPrimitiveLong = Async.result
    echoComplexLong = Async.result
    echoOptionalLong =  Async.result
    echoSingleDULong = Async.result
    echoLongInGenericUnion = Async.result
    command = fun (label, id, command) -> async {
        return Some "Operation error"
    }

    echoPosition = Async.result
    mapDateTimeOffsetAsKey = Async.result
    echoBigIntKeyMap = Async.result
    echoDecimalKeyMap = Async.result
    echoLongKeyMap = Async.result
    echoIntKeyMap = Async.result
    echoTimeOnlyMap = Async.result
    echoDateOnlyMap = Async.result
    simulateLongComputation = fun delay -> Async.Sleep delay
    echoRecordWithChar = Async.result
}