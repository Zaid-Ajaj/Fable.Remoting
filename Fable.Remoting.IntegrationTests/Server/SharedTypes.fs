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

type IServer = { 
    // primitive types
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

}


let routeBuilder typeName methodName = 
    sprintf "/api/%s/%s" typeName methodName