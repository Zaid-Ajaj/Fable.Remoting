module ServerDynamicInvokeTests

open Expecto
open Fable.Remoting.Server
open Types
open Fable.Remoting.Server

let equal x y = Expect.equal true (x = y) (sprintf "%A = %A" x y)
let pass () = Expect.equal true true ""
let fail () = Expect.equal false true ""

type SimpleRecord = { Int: int; String: string }

type SimpleUnion = One | Two

type TestRec = {
    simpleMethod : string -> int
    boolMethod : bool -> bool
    listsMethod : int list -> int
    genericUnion: Maybe<int> -> int
    arrayMethod: int[] -> int
    unitMethod: unit -> int
    simpleUnionMethod: SimpleUnion -> SimpleUnion
    intSeq: seq<int> -> int
    simpleRecordMethod: SimpleRecord -> SimpleRecord
    multiArgFunc : string -> int -> bool -> int
}

type InValidRecord = { someFunc: int -> string }
let invalidRecInstance = { someFunc = fun n -> "" }
let fsharpRecordTests =

    let invoke (funcName: string) (record: 't) (input: obj[]) (_: bool) =
        let recordFunctions = DynamicRecord.createRecordFuncInfo (record.GetType())
        match Map.tryFind funcName recordFunctions with
        | Some func -> DynamicRecord.invoke func record input
        | None -> failwithf "Function %s was not found" funcName

    let testRec = {
        genericUnion = function Just x -> x | _ -> 0
        simpleMethod = fun input -> input.Length
        listsMethod = fun xs -> Seq.sum xs
        arrayMethod = fun xs -> Array.sum xs
        unitMethod = fun () -> 5
        boolMethod = not
        simpleUnionMethod = function One -> Two | Two -> One
        intSeq = fun xs -> Seq.sum xs
        simpleRecordMethod = fun record -> { record with Int = record.Int + 10 }
        multiArgFunc = fun str n b -> str.Length + n + (if b then 1 else 0)
    }

    testList "FSharpRecord tests" [

        testCase "Exception is thrown when invalid records types are used" <| fun () ->
            try
                DynamicRecord.checkProtocolDefinition invalidRecInstance
                fail()
            with | _ -> pass()

        testCase "Invoking listsMethod" <| fun () ->
            let input = [1 .. 10]
            let output = invoke "listsMethod" testRec [| box input |] true
            equal 55 (unbox<int> output)

        testCase "Invoking simpleMethod" <| fun () ->
            let input = "hello"
            let output = invoke "simpleMethod" testRec [| box input |] true
            equal 5 (unbox<int> output)

        testCase "Invkoing genericUnion on record dynamically works" <| fun () ->
            let input = Just 5
            let output = invoke "genericUnion" testRec [| box input |] true
            equal 5 (unbox<int> output)

        testCase "Invoking arrayMethod" <| fun () ->
            let input = [| 1 .. 10 |]
            let output = invoke "arrayMethod" testRec [| box input |] true
            equal 55 (unbox<int> output)

        testCase "Invoking multi-arg function works" <| fun () ->
            let input = [| box "hello"; box 10; box false |]
            let output = invoke "multiArgFunc" testRec input true
            equal 15 (unbox<int> output)

            let input = [| box "byebye"; box 5; box true |]
            let output = invoke "multiArgFunc" testRec input true
            equal 12 (unbox<int> output)

        testCase "Invoking arrayMethod with empty array" <| fun () ->
            let input : int []= [| |]
            let output = invoke "arrayMethod" testRec ([|box input|]) true
            equal 0 (unbox<int> output)

        testCase "Invoking unitMethod" <| fun () ->
            let input = ()
            let output = invoke "unitMethod" testRec ([|box input|]) false
            equal 5 (unbox<int> output)

        testCase "Invoking with SimpleRecord method" <| fun () ->
            let input = { Int = 10; String = "hello" }
            let output = invoke "simpleRecordMethod" testRec ([|box input|]) true
            let outputRecord = unbox<SimpleRecord> output
            equal 20 outputRecord.Int
            equal "hello" outputRecord.String

        testCase "Invoking intSeq method" <| fun () ->
            let input = seq { 1 .. 10 }
            let output = invoke "intSeq" testRec ([|box input|]) true
            equal 55 (unbox<int> output)

        testCase "Invoking boolMethod" <| fun () ->
            let inputTrue = true
            let output1 = invoke "boolMethod" testRec ([|box inputTrue|]) true
            equal false (unbox<bool> output1)

            let inputFalse = false
            let output2 = invoke "boolMethod" testRec ([|box inputFalse|]) true
            equal true (unbox<bool> output2)

        testCase "making record function type works with multi args" <| fun () ->
            let funcType = typeof<string -> int -> bool -> Async<int>>
            DynamicRecord.makeRecordFuncType funcType |> ignore
            equal true true

        testCase "Invoking simpleUnionMethod" <| fun () ->
            let input = One
            let outputOne = invoke "simpleUnionMethod" testRec ([|box input|]) true
            equal Two (unbox<SimpleUnion> outputOne)

            let input = Two
            let outputTwo = invoke "simpleUnionMethod" testRec ([|box input|]) true
            equal One (unbox<SimpleUnion> outputTwo)
    ]

let serverTests =

    let implementation = TestImplementation.implementation

    let recordFunctions = DynamicRecord.createRecordFuncInfo (implementation.GetType())

    let getFunc name = Map.find name recordFunctions

    let invokeAsync (funcName: string) (input: obj[]) =
        match Map.tryFind funcName recordFunctions with
        | Some func -> DynamicRecord.invokeAsync func implementation input
        | None -> failwithf "Function %s was not found" funcName

    testList "Server Dynamic Invoke Tests" [

        testCaseAsync "Invoking when input is integer" <| async {
            let! dynamicResult = invokeAsync "echoInteger" [| box 5 |]
            equal 10 (unbox<int> dynamicResult)
        }

        testCaseAsync "Invoking when input is string" <| async {
            let! result = invokeAsync "getLength" ([|box "hello"|])
            equal 5 (unbox<int> result)
        }

        testCaseAsync "Invoking when input is option some" <| async {
            let! result = invokeAsync "echoOption" ([|box <| (Some 5)|])
            equal 10 (unbox<int> result)
        }

        testCaseAsync "Json: Invoking when input is option some" <| async {
            let input = "5"
            let func = getFunc "echoOption"
            let args = DynamicRecord.createArgsFromJson func input None
            let! result = DynamicRecord.invokeAsync func implementation args
            equal 10 (unbox<int> result)
        }

        testCaseAsync "Json array: Invoking when input is option some" <| async {
            let input = "[5]"
            let func = getFunc "echoOption"
            let args = DynamicRecord.createArgsFromJson func input None
            let! result = DynamicRecord.invokeAsync func implementation args
            equal 10 (unbox<int> result)
        }

        testCaseAsync "Invoking with input is option none" <| async {
            let input : Option<int> = None
            let! result = invokeAsync "echoOption" [| box input |]
            equal 0 (unbox<int> result)
        }

        testCaseAsync "Invoking when input is simple union: A" <| async {
            let input = A
            let! output = invokeAsync "simpleUnionInputOutput" [| box input |]
            equal B (unbox<AB> output)
        }

        testCaseAsync "Invoking when input is simple union: B" <| async {
            let input = B
            let! output = invokeAsync "simpleUnionInputOutput" [| box input |]
            equal A (unbox<AB> output)
        }

        testCaseAsync "Json: Invoking when input is simple union: B" <| async {
            let input = "\"B\""
            let func = getFunc "simpleUnionInputOutput"
            let args = DynamicRecord.createArgsFromJson func input None
            let! output =  DynamicRecord.invokeAsync func implementation args
            equal A (unbox<AB> output)
        }

        testCaseAsync "Generic union input: Maybe<int>" <| async {
            let input = Just 5
            let! output = invokeAsync "genericUnionInput" [| box input |]
            equal 5 (unbox<int> output)
        }

        testCaseAsync "JSON: Generic union input: Maybe<int>" <| async {
            let input = "{ \"Just\": 5 }"
            let func = getFunc "genericUnionInput"
            let args = DynamicRecord.createArgsFromJson func input None
            let! output =  DynamicRecord.invokeAsync func implementation args
            equal 5 (unbox<int> output)
        }

        testCaseAsync "JSON: Generic union input array Maybe<int>" <| async {
            let input = "[{ \"Just\": 5 }]"
            let func = getFunc "genericUnionInput"
            let args = DynamicRecord.createArgsFromJson func input None
            let! output =  DynamicRecord.invokeAsync func implementation args
            equal 5 (unbox<int> output)
        }

        testCaseAsync "JSON: invoking when input is an array and function has single input" <| async {
            let input = "[[1.0, 2.0, 3.0, 4.0, 5.0]]"
            let func = getFunc "floatList"
            let args = DynamicRecord.createArgsFromJson func input None
            let! output = DynamicRecord.invokeAsync func implementation args
            equal 15.0 (unbox<float> output)
        }

        testCaseAsync "JSON: invoke multiArg function" <| async {
            let input = "[[false, true, false], 5]"
            let func = getFunc "multiArg"
            let args = DynamicRecord.createArgsFromJson func input None
            let! output = DynamicRecord.invokeAsync func implementation args
            equal 6 (unbox<int> output)
        }

        testCaseAsync "JSON: invoke single record" <| async {
            let input = "{ \"name\":\"john\", \"age\": 21 }"
            let func = getFunc "simpleRec"
            let args = DynamicRecord.createArgsFromJson func input None
            let! output = DynamicRecord.invokeAsync func implementation args
            equal true (unbox<bool> output)
        }

        testCaseAsync "JSON: invoke single record as array" <| async {
            let input = "[{ \"name\":\"john\", \"age\": 21 }]"
            let func = getFunc "simpleRec"
            let args = DynamicRecord.createArgsFromJson func input None
            let! output = DynamicRecord.invokeAsync func implementation args
            equal true (unbox<bool> output)
        }

        testCaseAsync "No JSON is needed for input parameter of unit" <| async {
            let input = "" // or any thing
            let func = getFunc "unitToInts"
            let args = DynamicRecord.createArgsFromJson func input  None
            let! output = DynamicRecord.invokeAsync func implementation args
            equal 55 (unbox<int> output)
        }

        testCaseAsync "Invoking list of records" <| async {
            let input = "[[{\"Prop1\":\"\",\"Prop2\":15,\"Prop3\":null}, {\"Prop1\":\"\",\"Prop2\":10,\"Prop3\":null}]]"
            let func = getFunc "recordListToInt"
            let args = DynamicRecord.createArgsFromJson func input None
            let! output = DynamicRecord.invokeAsync func implementation args
            equal 25 (unbox<int> output)
        }

        testCaseAsync "Invoking list of integers" <| async {
            let input = "[[1,2,3,4,5]]"
            let func = getFunc "listIntegers"
            let args = DynamicRecord.createArgsFromJson func input None
            let! output = DynamicRecord.invokeAsync func implementation args
            equal 15 (unbox<int> output)
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
let allTests =
    testList "All Tests " [
        fsharpRecordTests
        serverTests
        threadSafeCell
    ]