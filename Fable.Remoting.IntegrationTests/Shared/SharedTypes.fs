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

type SingleCase = SingleCase of int

type SingleLongCase = SingleLongCase of int64

type ISimpleServer = {
    getLength : string -> Async<int>
}

type RecursiveRecord = {
    Name: string
    Children : RecursiveRecord list
}

type Tree = 
    | Leaf of int 
    | Branch of Tree * Tree 

type IServer = {
    // primitive types
    simpleUnit : unit -> Async<int>
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
    echoGenericMap : Map<string, Maybe<int>> -> Async<Map<string, Maybe<int>>>
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

let routeBuilder typeName methodName =
    sprintf "/api/%s/%s" typeName methodName