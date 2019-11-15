module SharedTypes

open System

type Record = {
    Prop1 : string
    Prop2 : int
    Prop3 : int option
}

type Maybe<'t> =
    | Just of 't
    | Nothing

type UnionType = One | Two

type GenericRecord<'t> = {
    Value: 't
    OtherValue : int
}

type HighScore = { Name: string; Score: int }


type SingleCase = SingleCase of int

type SingleLongCase = SingleLongCase of int64

type RemoteWork = RemoteWork of string

type RemoteWorkEntity = { RemoteWork : RemoteWork }

type ValidationError = ValidationError of string

type RequiredInputItem<'TInput> =
    | NoUserInputYet
    | InvalidUserInput of ('TInput * ValidationError)
    | ValidUserInput   of 'TInput

module RequiredInput =
    let validOrFail (requestInput:RequiredInputItem<'TInput>) =
        match requestInput with
        | NoUserInputYet     -> failwith "No value has been inputted"
        | InvalidUserInput _ -> failwith "Input is not valid!"
        | ValidUserInput x   -> x

type ISimpleServer = {
    getLength : string -> Async<int>
}

type ICookieServer = {
    checkCookie : unit -> Async<bool>
}

type AccessToken = AccessToken of int

type IAuthServer = {
    // secured by authorization token
    getSecureValue : unit -> Async<int>
}


type RecursiveRecord = {
    Name: string
    Children : RecursiveRecord list
}

type Tree =
    | Leaf of int
    | Branch of Tree * Tree

type RecordAsKey = { Key: int; Value: string }


type IServer = {
    // primitive types
    simpleUnit : unit -> Async<int>
    returnUnit : unit -> Async<unit>
    intToUnit : int -> Async<unit>
    tupleToUnit : int * string -> Async<unit>
    tupleToTuple : int * string -> Async<string * int>
    getLength : string -> Async<int>
    getSeq : unit -> Async<seq<Maybe<int>>>
    echoInteger : int -> Async<int>
    echoString : string -> Async<string>
    echoBool : bool -> Async<bool>
    echoIntOption : int option -> Async<int option>
    echoStringOption : string option -> Async<string option>
    echoRecursiveRecord : RecursiveRecord -> Async<RecursiveRecord>
    // Union types, simple and generic
    echoGenericUnionInt : Maybe<int> -> Async<Maybe<int>>
    echoGenericUnionString : Maybe<string> -> Async<Maybe<string>>
    echoSimpleUnionType : UnionType -> Async<UnionType>
    echoTree : Tree -> Async<Tree>
    // Records, simple and generic
    echoRecord : Record -> Async<Record>
    echoRemoteWorkEntity : RemoteWorkEntity -> Async<RemoteWorkEntity>
    echoGenericRecordInt : GenericRecord<int> -> Async<GenericRecord<int>>
    echoNestedGeneric : GenericRecord<Maybe<int option>> -> Async<GenericRecord<Maybe<int option>>>

    // lists
    echoIntList : int list -> Async<int list>
    echoStringList : string list -> Async<string list>
    echoBoolList : bool list -> Async<bool list>
    echoListOfListsOfStrings : string list list -> Async<string list list>
    echoListOfGenericRecords :  GenericRecord<int> list -> Async<GenericRecord<int> list>

    // arrays
    echoHighScores : HighScore array -> Async<HighScore array>
    getHighScores : unit -> Async<HighScore array>

    echoResult : Result<int, string> -> Async<Result<int, string>>
    echoBigInteger : bigint -> Async<bigint>
    echoGenericMap : Map<string, Maybe<int>> -> Async<Map<string, Maybe<int>>>
    // maps
    echoMap : Map<string, int> -> Async<Map<string, int>>
    echoTupleMap : Map<int * int, int> -> Async<Map<int * int, int>>
    mapRecordAsKey: unit -> Async<Map<RecordAsKey, int>>
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

    // binary responses
    binaryContent : unit -> Async<byte[]>
    binaryInputOutput : byte[] -> Async<byte[]>

    // long (int64) conversion
    echoPrimitiveLong : int64 -> Async<int64>
    echoComplexLong : GenericRecord<Int64> -> Async<GenericRecord<Int64>>
    echoOptionalLong : Option<int64> -> Async<Option<int64>>
    echoSingleDULong : SingleLongCase -> Async<SingleLongCase>
    echoLongInGenericUnion : Maybe<int64> -> Async<Maybe<int64>>
    echoAnonymousRecord : Maybe<{| name: string |}> -> Async<Maybe<{| name: string |}>>
    echoNestedAnonRecord : Maybe<{| nested: {| name: string |} |}> -> Async<Maybe<{| nested: {| name: string |} |}>>
}

let routeBuilder typeName methodName =
    sprintf "/api/%s/%s" typeName methodName