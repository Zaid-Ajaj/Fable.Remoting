module ServerDynamicInvokeTests

open Expecto
open Fable.Remoting.Server.Proxy
open Types
open Fable.Remoting.Server
open System.IO
open System
open Newtonsoft.Json
open Fable.Remoting.Json

let equal x y = Expect.equal true (x = y) (sprintf "%A = %A" x y)
let pass () = Expect.equal true true ""
let fail () = Expect.equal false true ""

type SimpleRecord = { Int: int; String: string }

type SimpleUnion = One | Two

type TestRec = {
    simpleMethod : string -> Async<int>
    boolMethod : bool -> Async<bool>
    listsMethod : int list -> Async<int>
    genericUnion: Maybe<int> -> Async<int>
    arrayMethod: int[] -> Async<int>
    unitMethod: unit -> Async<int>
    simpleUnionMethod: SimpleUnion -> Async<SimpleUnion>
    intSeq: seq<int> -> Async<int>
    simpleRecordMethod: SimpleRecord -> Async<SimpleRecord>
    multiArgFunc : string -> int -> bool -> Async<int>
    formatDate : DateTime -> Async<string>
    formatTimestamp : DateTimeOffset -> Async<string>
}

let converter = FableJsonConverter()

type InValidRecord = { someFunc: int -> string }
let invalidRecInstance = { someFunc = fun n -> "" }

let invoke<'out, 'impl> (funcName: string) (record: 'impl) (input: Choice<obj[], string>) =
    let options = Remoting.createApi () |> Remoting.fromValue record |> Remoting.withRouteBuilder (fun _ m -> m)

    let proxy = makeApiProxy options
    use inp = new MemoryStream ()
    use output = new MemoryStream ()

    let inputBytes =
        match input with
        | Choice1Of2 x -> JsonConvert.SerializeObject (x, converter) |> System.Text.Encoding.UTF8.GetBytes
        | Choice2Of2 x -> System.Text.Encoding.UTF8.GetBytes x
    inp.Write (inputBytes, 0, inputBytes.Length)
    inp.Position <- 0L

    match (proxy { ImplementationBuilder = (fun () -> record); Input = inp; EndpointName = funcName; HttpVerb = "POST"; InputContentType = "application/json"; IsProxyHeaderPresent = true; Output = output }).Result with
    | Success _ -> JsonConvert.DeserializeObject<'out>(System.Text.Encoding.UTF8.GetString (output.ToArray ()), converter)
    | InvocationResult.Exception (e, _, _) -> raise e
    | InvalidHttpVerb -> failwithf "Function %s does not expect POST" funcName
    | EndpointNotFound -> failwithf "Function %s was not found" funcName

let proxyTests =
    let testRec = {
        genericUnion = fun x -> async { match x with Just x -> return x | _ -> return 0 }
        simpleMethod = fun input -> async { return input.Length }
        listsMethod = fun xs -> async { return Seq.sum xs }
        arrayMethod = fun xs -> async { return Array.sum xs }
        unitMethod = fun () -> async { return 5 }
        boolMethod = fun x -> async { return not x }
        simpleUnionMethod = fun x -> async { match x with One -> return Two | Two -> return One }
        intSeq = fun xs -> async { return Seq.sum xs }
        simpleRecordMethod = fun record -> async { return { record with Int = record.Int + 10 } }
        multiArgFunc = fun str n b -> async { return str.Length + n + (if b then 1 else 0) }
        formatDate = fun date -> async { return date.ToString() }
        formatTimestamp = fun timestamp -> async { return timestamp.ToString("o") }
    }

    testList "Proxy tests" [

        testCase "Exception is thrown when invalid records types are used" <| fun () ->
            try
                invoke<string, InValidRecord> "someFunc" invalidRecInstance (Choice1Of2 [| box 1 |]) |> ignore
                fail()
            with | _ -> pass()

        testCase "Exception is thrown when invalid invalid implementation is used" <| fun () ->
            try
                invoke<string, string> "someFunc" "blah" (Choice1Of2 [| box 1 |]) |> ignore
                fail()
            with | _ -> pass()

        testCase "Invoking listsMethod" <| fun () ->
            let input = [1 .. 10]
            let output = invoke "listsMethod" testRec (Choice1Of2 [| box input |])
            equal 55 output

        testCase "Invoking simpleMethod" <| fun () ->
            let input = "hello"
            let output = invoke "simpleMethod" testRec (Choice1Of2 [| box input |])
            equal 5 output

        testCase "Invkoing genericUnion on record dynamically works" <| fun () ->
            let input = Just 5
            let output = invoke "genericUnion" testRec (Choice1Of2 [| box input |])
            equal 5 output

        testCase "Invoking arrayMethod" <| fun () ->
            let input = [| 1 .. 10 |]
            let output = invoke "arrayMethod" testRec (Choice1Of2 [| box input |])
            equal 55 output

        testCase "Invoking multi-arg function works" <| fun () ->
            let input = [| box "hello"; box 10; box false |]
            let output = invoke "multiArgFunc" testRec (Choice1Of2 input)
            equal 15 output

            let input = [| box "byebye"; box 5; box true |]
            let output = invoke "multiArgFunc" testRec (Choice1Of2 input)
            equal 12 output

        testCase "Invoking arrayMethod with empty array" <| fun () ->
            let input : int []= [| |]
            let output = invoke "arrayMethod" testRec (Choice1Of2 [| box input |])
            equal 0 output

        testCase "Invoking unitMethod" <| fun () ->
            let input = ()
            let output = invoke "unitMethod" testRec (Choice1Of2 [| box input |])
            equal 5 output

        testCase "Invoking with SimpleRecord method" <| fun () ->
            let input = { Int = 10; String = "hello" }
            let output: SimpleRecord = invoke "simpleRecordMethod" testRec (Choice1Of2 [| box input |])
            equal 20 output.Int
            equal "hello" output.String

        testCase "Invoking intSeq method" <| fun () ->
            let input = seq { 1 .. 10 }
            let output = invoke "intSeq" testRec (Choice1Of2 [| box input |])
            equal 55 output

        testCase "Invoking boolMethod" <| fun () ->
            let input = true
            let output1 = invoke "boolMethod" testRec (Choice1Of2 [| box input |])
            equal false output1

            let inputFalse = false
            let output2 = invoke "boolMethod" testRec (Choice1Of2 [| box inputFalse |])
            equal output2 true

        testCase "Invoking simpleUnionMethod" <| fun () ->
            let input = One
            let outputOne = invoke "simpleUnionMethod" testRec (Choice1Of2 [| box input |])
            equal Two outputOne

            let input = Two
            let outputTwo = invoke "simpleUnionMethod" testRec (Choice1Of2 [| box input |])
            equal One outputTwo

        testCaseAsync "Json array: Invoking when input is option some" <| async {
            let input = "[5]"
            let result = invoke "echoOption" TestImplementation.implementation (Choice2Of2 input)
            equal 10 result
        }

        testCaseAsync "Invoking with input is option none" <| async {
            let input : Option<int> = None
            let result = invoke "echoOption" TestImplementation.implementation (Choice1Of2 [| box input |])
            equal 0 result
        }

        testCaseAsync "Invoking when input is simple union: A" <| async {
            let input = A
            let output = invoke "simpleUnionInputOutput" TestImplementation.implementation (Choice1Of2 [| box input |])
            equal B output
        }

        testCaseAsync "Invoking when input is simple union: B" <| async {
            let input = B
            let output = invoke "simpleUnionInputOutput" TestImplementation.implementation (Choice1Of2 [| box input |])
            equal A output
        }

        testCaseAsync "Generic union input: Maybe<int>" <| async {
            let input = Just 5
            let output = invoke "genericUnionInput" TestImplementation.implementation (Choice1Of2 [| box input |])
            equal 5 output
        }

        testCaseAsync "JSON: Generic union input array Maybe<int>" <| async {
            let input = "[{ \"Just\": 5 }]"
            let output = invoke "genericUnionInput" TestImplementation.implementation (Choice2Of2 input)
            equal 5 output
        }

        testCaseAsync "JSON: invoking when input is an array and function has single input" <| async {
            let input = "[[1.0, 2.0, 3.0, 4.0, 5.0]]"
            let output = invoke "floatList" TestImplementation.implementation (Choice2Of2 input)
            equal 15.0 output
        }

        testCaseAsync "JSON: invoke multiArg function" <| async {
            let input = "[[false, true, false], 5]"
            let output = invoke "multiArg" TestImplementation.implementation (Choice2Of2 input)
            equal 6 output
        }

        testCaseAsync "JSON: invoke multiArg function with too many arguments" <| async {
            let input = "[[false, true, false], 3, 4]"
            try
                invoke "multiArg" TestImplementation.implementation (Choice2Of2 input) |> ignore
                fail ()
            with e -> pass ()
        }

        testCaseAsync "JSON: invoke multiArg function with too few arguments" <| async {
            let input = "[[false, true, false]]"
            try
                invoke "multiArg" TestImplementation.implementation (Choice2Of2 input) |> ignore
                fail ()
            with _ -> pass ()
        }

        testCaseAsync "JSON: invoke single record as array" <| async {
            let input = "[{ \"name\":\"john\", \"age\": 21 }]"
            let output = invoke "simpleRec" TestImplementation.implementation (Choice2Of2 input)
            equal true output
        }

        testCaseAsync "No JSON is needed for input parameter of unit" <| async {
            let input = "" // or any thing
            let output = invoke "unitToInts" TestImplementation.implementation (Choice2Of2 input)
            equal 55 output
        }

        testCaseAsync "Invoking list of records" <| async {
            let input = "[[{\"Prop1\":\"\",\"Prop2\":15,\"Prop3\":null}, {\"Prop1\":\"\",\"Prop2\":10,\"Prop3\":null}]]"
            let output = invoke "recordListToInt" TestImplementation.implementation (Choice2Of2 input)
            equal 25 output
        }

        testCaseAsync "Invoking list of integers" <| async {
            let input = "[[1,2,3,4,5]]"
            let output = invoke "listIntegers" TestImplementation.implementation (Choice2Of2 input)
            equal 15 output
        }
    ]

let threadSafeCell =
    testList "Thread safe cell tests" [
        testCaseAsync "computeOnce works" <| async {
            let magicNumbers = new ResizeArray<int>()
            let expensiveFunction() =
                System.Threading.Thread.Sleep(2000)
                magicNumbers.Add(42)
                42
            let getExpensiveResult = ThreadSafeCell.computeOnce expensiveFunction
            let getManyTimes = Async.Parallel [for _ in 1 .. 1000 -> async { return! getExpensiveResult() }]
            let! values = getManyTimes
            Expect.equal (42 * 1000) (Seq.sum values) "Expensive result was retrieved 100 times"
            Expect.equal 1 magicNumbers.Count "Adding numbers was called once"
        }
    ]

let docs = Docs.createFor<TestRec>()

let docsTests = testList "Docs tests" [
    test "API with DateTime as input works" {
        let example =
            docs.route <@ fun api -> api.formatDate @>
            |> docs.alias "Format Date"
            |> docs.description "Formats the given date"
            |> docs.example <@ fun api -> api.formatDate (DateTime.Now) @>
            |> docs.example <@ fun api -> api.formatDate (DateTime(2020, 1, 1)) @>

        Expect.equal example.Route (Some "formatDate") "The name of the route is correct"
        Expect.equal example.Description (Some "Formats the given date") "The description of the route is correct"
        Expect.equal example.Examples.Length 2 "There are two examples"
    }

    test "API with DateTimeOffset as input works" {
        let example =
            docs.route <@ fun api -> api.formatTimestamp @>
            |> docs.description "Formats the given timestamp"
            |> docs.example <@ fun api -> api.formatTimestamp (DateTimeOffset.Now) @>

        Expect.equal example.Route (Some "formatTimestamp") "The name of the route is correct"
        Expect.equal example.Description (Some "Formats the given timestamp") "The description of the route is correct"
        Expect.equal example.Examples.Length 1 "There is one examples"
    }
]

let allTests =
    testList "All Tests " [
        proxyTests
        threadSafeCell
        docsTests
    ]