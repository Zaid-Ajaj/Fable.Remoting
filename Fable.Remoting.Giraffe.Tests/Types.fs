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
    echoOption : int option -> Async<int option>
    echoGenericUnion : Maybe<int> -> Async<Maybe<int>>
    echoBool : bool -> Async<bool>
    echoSimpleUnion : AB -> Async<AB>
    recordEcho : Record -> Async<Record>
    echoIntList : int list -> Async<int list>
    unitToInts : unit -> Async<int>
    echoRecordList : Record list -> Async<Record list>
    floatList : float list -> Async<float list>
}



let pureAsync (x: 'a) : Async<'a> =
    async { return x }

let implementation = { 
    echoInteger = pureAsync
    echoMonth = pureAsync
    echoString = fun x -> async { return x }
    echoOption = pureAsync
    echoGenericUnion = pureAsync
    echoBool = pureAsync
    echoSimpleUnion = pureAsync
    recordEcho = pureAsync
    echoIntList = pureAsync
    unitToInts = fun () -> pureAsync 1
    echoRecordList = pureAsync
    floatList = pureAsync
}
