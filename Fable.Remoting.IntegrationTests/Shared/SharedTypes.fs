module SharedTypes

open System
open Fable.Core

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

type OtherDataA = {
    Text : string
    Value : string
}

type OtherDataB = {
    MataA : string
    MataC : string
    MataB : Map<Guid,OtherDataA>
}
type SomeData = {
    CataA : string
    CataB : Map<Guid, OtherDataB>
    CataC : string
}

type TestCommand = {
    Data : SomeData
}

[<Measure>]
type SomeUnit

type SomeEnum =
    | Val0 = 0
    | Val1 = 1
    | Val2 = 2

[<StringEnum>]
type SomeStringEnum =
    | FirstString
    | SecondString

type HighScore = { Name: string; Score: int }

type String50 =
    private String50 of string

    with
        member this.Read() =
            match this with
            | String50 content -> content

        static member Create(content: string) = String50 content

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

type CommandLabel = CommandLabel of string
type ClientId = ClientId of string
type RobotIdToken = RobotId of string
type FeedbackChannelIfOutOfSync = FeedbackChannelIfOutOfSync of ClientId
type OperationErrorMessage = string option
type RequesterIdentifier =
    | IOwnRobot of RobotIdToken * FeedbackChannelIfOutOfSync
    | IWantResponsesOn of ClientId

type Address = Address of int

[<Struct>]
type CoordCartesian = {
    x: float
    y: float
    z: float
    w: float
    p: float
    r: float
}

type CartesianConfig = CartesianConfig of string

type Position =
    | Cartesian of CoordCartesian * CartesianConfig
    | NotSet

[<RequireQualifiedAccess>]
module Requests =
    type Command =
        | PositionSet of Address * Position
        | RawCommand of string

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

type SomeOtherDU =
    | SomeOtherCase

type MyDU =
    | SomeCase
    | CustomCase of Set<SomeOtherDU>

type Token = Token of string

let rec createRecursiveRecord childCount levels =
    if levels > 0 then
        let children = [ 1 .. childCount ] |> List.map (fun _ -> createRecursiveRecord childCount (levels - 1))
        { Name = "Test name";  Children = children }
    else
        { Name = "Leaf"; Children = [] }

type Tree =
    | Leaf of int
    | Branch of Tree * Tree

type RecordAsKey = { Key: int; Value: string }

type IBinaryServer = {
    // primitive types
    simpleUnit : unit -> Async<int>
    returnUnit : unit -> Async<unit>
    intToUnit : int -> Async<unit>
    privateConstructor : String50 -> Async<String50>
    tupleToUnit : int * string -> Async<unit>
    tupleToTuple : int * string -> Async<string * int>
    getLength : string -> Async<int>
    echoInteger : int -> Async<int>
    echoString : string -> Async<string>
    echoBool : bool -> Async<bool>
    echoEnum : SomeEnum -> Async<SomeEnum>
    echoStringEnum : SomeStringEnum -> Async<SomeStringEnum>
    echoTimeSpan : TimeSpan -> Async<TimeSpan>
    echoIntOption : int option -> Async<int option>
    echoIntOptionOption : int option option -> Async<int option option>
    echoStringValueOption : string voption -> Async<string voption>
    echoIntWithMeasure : int<SomeUnit> -> Async<int<SomeUnit>>
    echoInt16WithMeasure : int16<SomeUnit> -> Async<int16<SomeUnit>>
    echoInt64WithMeasure : int64<SomeUnit> -> Async<int64<SomeUnit>>
    echoDecimalWithMeasure : decimal<SomeUnit> -> Async<decimal<SomeUnit>>
    echoFloatWithMeasure : float<SomeUnit> -> Async<float<SomeUnit>>
    echoStringOption : string option -> Async<string option>
    echoRecursiveRecord : RecursiveRecord -> Async<RecursiveRecord>
    echoDateTime : DateTime -> Async<DateTime>
    echoDateTimeOffset : DateTimeOffset -> Async<DateTimeOffset>
    echoGuid : Guid -> Async<Guid>
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
    echoMaybeBoolList : Maybe<bool> list -> Async<Maybe<bool> list>
    echoListOfListsOfStrings : string list list -> Async<string list list>
    echoListOfGenericRecords :  GenericRecord<int> list -> Async<GenericRecord<int> list>

    // arrays
    echoHighScores : HighScore array -> Async<HighScore array>
    echoArray3tuples : (int64 * string * DateTime) array -> Async<(int64 * string * DateTime) array>
    getHighScores : unit -> Async<HighScore array>

    echoResult : Result<int, string> -> Async<Result<int, string>>
    echoBigInteger : bigint -> Async<bigint>
    genericDictionary : unit -> Async<System.Collections.Generic.Dictionary<string, Maybe<int>>>
    echoGenericMap : Map<string, Maybe<int>> -> Async<Map<string, Maybe<int>>>
    // maps
    echoMap : Map<string, int> -> Async<Map<string, int>>
    echoTupleMap : Map<int * int, int> -> Async<Map<int * int, int>>
    mapRecordAsKey: unit -> Async<Map<RecordAsKey, int>>

    // sets
    echoSet : Set<string> -> Async<Set<string>>
    echoTupleSet : Set<int * int> -> Async<Set<int * int>>
    setRecordAsValue: unit -> Async<Set<RecordAsKey>>

    // errors
    throwError : unit -> Async<string>
    throwBinaryError : unit -> Async<byte[]>

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

type IServer = {
    // primitive types
    simpleUnit : unit -> Async<int>
    returnUnit : unit -> Async<unit>
    intToUnit : int -> Async<unit>
    privateConstructor : String50 -> Async<String50>
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
    echoDateTime : DateTime -> Async<DateTime>
    echoDateTimeOffset : DateTimeOffset -> Async<DateTimeOffset>
    // Union types, simple and generic
    echoGenericUnionInt : Maybe<int> -> Async<Maybe<int>>
    echoGenericUnionString : Maybe<string> -> Async<Maybe<string>>
    echoSimpleUnionType : UnionType -> Async<UnionType>
    echoUnionOfOtherUnions : MyDU -> Async<MyDU>
    echoToken : Token -> Async<Token>
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
    echoTestCommand : TestCommand -> Async<TestCommand>
    // sets
    echoSet : Set<string> -> Async<Set<string>>
    echoTupleSet : Set<int * int> -> Async<Set<int * int>>
    setRecordAsValue: unit -> Async<Set<RecordAsKey>>

    // errors
    throwError : unit -> Async<string>
    throwBinaryError : unit -> Async<byte[]>

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

    // misc
    command: CommandLabel * RequesterIdentifier * Requests.Command -> Async<OperationErrorMessage>
    echoPosition : Position -> Async<Position>
}

let routeBuilder typeName methodName =
    sprintf "/api/%s/%s" typeName methodName