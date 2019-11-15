module Types

open System

type Record = {
    Prop1 : string
    Prop2 : int
    Prop3 : int option
}

type Maybe<'t> =
    | Just of 't
    | Nothing

type AB = A | B

type IProtocol = {
    echoInteger : int -> Async<int>
    echoMonth : DateTime -> Async<DateTime>
    echoString : string -> Async<string>
    echoIntOption : int option -> Async<int option>
    echoStringOption : string option -> Async<string option>
    echoGenericUnionInt : Maybe<int> -> Async<Maybe<int>>
    echoGenericUnionString: Maybe<string> -> Async<Maybe<string>>
    echoBool : bool -> Async<bool>
    echoSimpleUnion : AB -> Async<AB>
    echoRecord : Record -> Async<Record>
        // binary responses
    binaryContent : unit -> Async<byte[]>
    binaryInputOutput : byte[] -> Async<byte[]>

    echoIntList : int list -> Async<int list>
    unitToInts : unit -> Async<int list>
    echoRecordList : Record list -> Async<Record list>
    floatList : float list -> Async<float list>
    echoResult : Result<int, string> -> Async<Result<int, string>>
    echoBigInteger : bigint -> Async<bigint>
    echoMap : Map<string, int> -> Async<Map<string, int>>
    echoTupleMap : Map<int*int, int> -> Async<Map<int*int, int>>

}



let pureAsync (x: 'a) : Async<'a> =
    async { return x }

let implementation = {
    binaryContent = fun () -> async { return [| byte 1; byte 2; byte 3 |] }
    binaryInputOutput = pureAsync
    echoInteger = pureAsync
    echoMonth = pureAsync
    echoString = pureAsync
    echoIntOption = pureAsync
    echoGenericUnionInt = pureAsync
    echoGenericUnionString = pureAsync
    echoBool = pureAsync
    echoStringOption = pureAsync
    echoSimpleUnion = pureAsync
    echoRecord = pureAsync
    echoIntList = pureAsync
    unitToInts = fun () -> pureAsync [1; 2; 3; 4; 5]
    echoRecordList = pureAsync
    floatList = pureAsync
    echoResult = pureAsync
    echoBigInteger = pureAsync
    echoMap = pureAsync
    echoTupleMap = pureAsync
}


open System

type UnionType = One | Two

type GenericRecord<'t> = {
    Value: 't
    OtherValue : int
}

type SingleCase = SingleCase of int

type SingleLongCase = SingleLongCase of int64

type ISimpleServer = {
    getLength : string -> Async<int>
}

type IServer = {
    // primitive types
    simpleUnit : unit -> Async<int>
    getLength : string -> Async<int>
    echoInteger : int -> Async<int>
    echoString : string -> Async<string>
    echoBool : bool -> Async<bool>
    echoIntOption : int option -> Async<int option>
    echoStringOption : string option -> Async<string option>

    // Union types, simple and generic
    echoGenericUnionInt : Maybe<int> -> Async<Maybe<int>>
    echoGenericUnionString : Maybe<string> -> Async<Maybe<string>>
    echoSimpleUnionType : UnionType -> Async<UnionType>

    // Records, simple and generic
    echoRecord : Record -> Async<Record>
    echoGenericRecordInt : GenericRecord<int> -> Async<GenericRecord<int>>
    echoNestedGeneric : GenericRecord<Maybe<int option>> -> Async<GenericRecord<Maybe<int option>>>

    // lists
    echoIntList : int list -> Async<int list>
    echoStringList : string list -> Async<string list>
    echoBoolList : bool list -> Async<bool list>
    echoListOfListsOfStrings : string list list -> Async<string list list>
    echoListOfGenericRecords :  GenericRecord<int> list -> Async<GenericRecord<int> list>

    echoResult : Result<int, string> -> Async<Result<int, string>>
    echoBigInteger : bigint -> Async<bigint>

    // maps
    echoMap : Map<string, int> -> Async<Map<string, int>>
    // errors
    throwError : unit -> Async<string>

    echoSingleCase : SingleCase -> Async<SingleCase>
    // mutli-arg functions
    multiArgFunc : string -> int -> bool -> Async<int>

    // tuples
    tuplesAndLists : Map<string, int> * string list -> Async<Map<string, int>>
    // overridden function
    overriddenFunction : string -> Async<int>

    customStatusCode : unit -> Async<string>
    //Pure async
    pureAsync : Async<int>
    asyncNestedGeneric : Async<GenericRecord<Maybe<Option<string>>>>

    // edge cases
    multiArgComplex : bool -> GenericRecord<Maybe<Option<string>>> -> Async<GenericRecord<Maybe<Option<string>>>>

    // long (int64) conversion
    echoPrimitiveLong : int64 -> Async<int64>
    echoComplexLong : GenericRecord<Int64> -> Async<GenericRecord<Int64>>
    echoOptionalLong : Option<int64> -> Async<Option<int64>>
    echoSingleDULong : SingleLongCase -> Async<SingleLongCase>
    echoLongInGenericUnion : Maybe<int64> -> Async<Maybe<int64>>
}


module Async =
    let result<'a> (x: 'a) : Async<'a> =
        async { return x }


let simpleServer : ISimpleServer = {
    getLength = fun input -> Async.result input.Length
}

// Async.result : 'a -> Async<'a>
// a simple implementation, just return whatever value you get (echo the input)
let server : IServer  = {
    // primitive types
    simpleUnit = fun () -> async { return 42 }
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
    tuplesAndLists = fun (dict, xs) ->
        xs
        |> List.map (fun x -> x, x.Length)
        |> List.append (Map.toList dict)
        |> Map.ofList
        |> Async.result

    echoResult = Async.result
    echoSingleCase = Async.result
    echoBigInteger = Async.result
    throwError = fun () -> async { return! failwith "Generating custom server error" }
    echoMap = Async.result
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
}


type IReaderTest = { getPath: Async<string> }

