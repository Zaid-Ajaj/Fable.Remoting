module Types

open System
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

type SingleLongCase = SingleLongCase of int64

type IProtocol = { 
    getLength : string -> Async<int>  
    echoInteger : int -> Async<int>  
    echoOption : int option -> Async<int>
    echoMonth : DateTime -> Async<int>
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
    echoResult : Result<int, string> -> Async<Result<int, string>>
    echoBigInteger : bigint list -> Async<bigint>
    echoMap : Map<string, int> -> Async<Map<string, int>>

    throwError : string -> Async<int>
    multipleSum : int -> int -> Async<int>
    lotsOfArgs : string -> int -> float -> Async<string>
    echoSingleDULong : SingleLongCase -> Async<SingleLongCase>
    datetimeOffset : DateTimeOffset -> Async<DateTimeOffset>
    maybeDatetimeOffset : Maybe<DateTimeOffset> -> Async<Maybe<DateTimeOffset>>
}

let implementation = {
    getLength = fun input -> async { return input.Length }
    echoInteger = fun n -> async { return n + n }
    echoOption = function 
        | Some n -> async { return n + n }
        | None -> async { return 0 }
    echoMonth = fun date -> async { return date.Month }
    echoString = fun str -> async { return str }
    optionOutput = fun str -> async { return if str <> "" then Some 5 else None }
    genericUnionInput = function
        | Nothing -> async { return 0 }
        | Just x -> async { return x }
    genericUnionOutput = fun b -> async { return if b then Just 5 else Nothing }
    simpleUnionInputOutput = fun union ->
        async {
            return if union = A then B else A
        }
    recordEcho = fun r -> async { return { r with Prop2 = r.Prop2 + 10 } }
    listIntegers = fun xs -> async { return Seq.sum xs }
    unitToInts = fun () -> async { return Seq.sum [1..10] }
    recordListToInt = fun records -> records |> Seq.map (fun r -> r.Prop2) |> Seq.sum |> fun res -> async { return res }
    floatList = fun xs -> Seq.sum xs |> fun result -> async {return Math.Round(result, 2) }
    echoResult = fun x -> async { return x }
    echoBigInteger = fun xs -> async { return Seq.sum xs }
    echoMap = fun x -> async { return x }
    throwError = fun x -> async { failwith "I am thrown from adapter function"; return 1 }
    multipleSum = fun a b -> async {return a + b}
    lotsOfArgs = fun s i f -> async {return sprintf "string: %s; int: %i; float: %f" s i f}
    echoSingleDULong = fun x -> async { return x } 
    datetimeOffset = fun x -> async { return x }
    maybeDatetimeOffset = fun x -> async { return x }
}