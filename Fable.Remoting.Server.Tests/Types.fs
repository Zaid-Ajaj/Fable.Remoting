module Types

open System

type Record = { 
    Prop1 : string
    Prop2 : int
    Prop3 : int option
}

type SimpleRec = { name: string; age: int }

type Maybe<'t> = 
    | Just of 't
    | Nothing

type AB = A | B

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
    multiArg: bool[] -> int -> Async<int> 
    simpleRec: SimpleRec -> Async<bool>
}


module TestImplementation = 
    let implementation = {
        getLength = fun input -> async { return input.Length }
        echoInteger = fun n -> async { return n + n }
        echoOption = function 
            | Some n -> async { return n + n }
            | None -> async { return 0 }
        echoMonth = fun date -> async { return date.Month }
        echoString = fun str -> async { return str }
        optionOutput = fun str -> async { return if str <> "" then Some 5 else None }
        genericUnionInput = fun x ->
            match x with
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
        multiArg = fun bools n -> async {
            let m = Array.map (function | true -> 1 | false -> 0) bools |> Array.sum
            return n + m 
        }
        simpleRec = fun record -> async { return record.age > 18 }
    }