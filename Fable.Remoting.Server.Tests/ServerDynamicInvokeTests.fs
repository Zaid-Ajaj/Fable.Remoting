module ServerDynamicInvokeTests 

open Expecto
open Fable.Remoting.Server
open Fable.Remoting.Reflection
open Types

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
}

let fsharpRecordTests = 

    let invoke (methodName: string) (record: obj) (input: obj[]) (_: bool) =
        FSharpRecord.Invoke(methodName, record, input) 

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
    }

    testList "FSharpRecord tests" [

        testCase "Invoking listsMethod" <| fun () ->
            let input = [1 .. 10]
            let output = invoke "listsMethod" testRec ([|box input|]) true
            equal 55 (unbox<int> output)

        testCase "Invoking simpleMethod" <| fun () ->
            let input = "hello"
            let output = invoke "simpleMethod" testRec ([|box input|]) true
            equal 5 (unbox<int> output)

        // failing because of this blocker: https://github.com/dotnet/corefx/issues/23387
        testCase "Invkoing genericUnion on record dynamically works" <| fun () ->
            let input = Just 5
            let output = invoke "genericUnion" testRec ([|box input|]) true
            equal 5 (unbox<int> output)

        testCase "Invoking arrayMethod" <| fun () -> 
            let input = [| 1 .. 10 |]
            let output = invoke "arrayMethod" testRec ([|box input|]) true
            equal 55 (unbox<int> output)
        
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
    
    testList "Server Dynamic Invoke Tests" [

        testCaseAsync "Invoking when input is integer" <| async {
            let! dynamicResult = ServerSide.dynamicallyInvoke "echoInteger" implementation ([|box 5|]) 
            equal 10 (unbox<int> dynamicResult)
        }

        testCaseAsync "Invoking when input is string" <| async {
            let! result = ServerSide.dynamicallyInvoke "getLength" implementation ([|box "hello"|]) 
            equal 5 (unbox<int> result)
        }

        testCaseAsync "Invoking when input is option some" <| async {
            let! result = ServerSide.dynamicallyInvoke "echoOption" implementation ([|box <| (Some 5)|]) 
            equal 10 (unbox<int> result)
        }

        testCaseAsync "Invoking with input is option none" <| async {
            let input : Option<int> = None
            let! result = ServerSide.dynamicallyInvoke "echoOption" implementation ([|box input|]) 
            equal 0 (unbox<int> result)
        }

        testCaseAsync "Invoking when input is simple union: A" <| async {
            let input = A
            let! output = ServerSide.dynamicallyInvoke "simpleUnionInputOutput" implementation ([|box input|]) 
            equal B (unbox<AB> output)
        }

        testCaseAsync "Invoking when input is simple union: B" <| async {
            let input = B
            let! output = ServerSide.dynamicallyInvoke "simpleUnionInputOutput" implementation ([|box input|]) 
            equal A (unbox<AB> output)
        }
        // failing because of this blocker: https://github.com/dotnet/corefx/issues/23387
        testCaseAsync "Generic union input: Maybe<int>" <| async {
            let input = Just 5
            let! output = ServerSide.dynamicallyInvoke "genericUnionInput" implementation ([|box input|]) 
            equal 5 (unbox<int> output)
        }
    ]

let allTests = 
    testList "All Tests " [
        fsharpRecordTests
        serverTests
    ]