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
    echoIntList : int list -> Async<int list>
    unitToInts : unit -> Async<int list>
    echoRecordList : Record list -> Async<Record list>
    floatList : float list -> Async<float list>
}



let pureAsync (x: 'a) : Async<'a> =
    async { return x }

let implementation = { 
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
}
