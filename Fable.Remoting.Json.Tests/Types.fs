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

type UnionWithDateTime = 
    | Date of DateTime
    | Int of int

type AB = A | B

type IProtocol = { 
    getLength : string -> Async<int>  
    echoInteger : int -> Async<int>  
    echoOption : int option -> Async<int>
    echoMonth : System.DateTime -> Async<int>
    echoString : string -> Async<string>
    optionOutput : string -> Async<int option>
    genericUnionInput : Maybe<int> -> Async<int>
    genericUnionOutput : bool -> Async<Maybe<int>>
    simpleUnionInputOutput : AB -> Async<AB>
    recordEcho : Record -> Async<Record>
    listIntegers : int list -> Async<int>
    unitToInts : unit -> Async<int>
    recordListToInt : Record[] -> Async<int>
    floatList : float [] -> Async<float>
}

type CustomerId = CustomerId of int

type Customer = { Id : CustomerId }

type Color = Red | Blue
type ColorDU = ColorType of Color
type ColorRecord = { Color: ColorDU }