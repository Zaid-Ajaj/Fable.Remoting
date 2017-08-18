module ServerDynamicInvokeTests 

open Expecto
open Fable.Remoting.Server
open Fable.Remoting.Reflection
open Types

let equal x y = Expect.equal true (x = y) (sprintf "%A = %A" x y)
let pass () = Expect.equal true true ""   
let fail () = Expect.equal false true ""

type TestRec = { 
    simpleMethod : string -> int
    listsMethod : int list -> int
    genericUnion: Maybe<int> -> int
}

let fsharpRecordTests = 
    let invoke (methodName: string) (record: obj) (input: obj) (hasArg: bool) =
        FSharpRecord.Invoke(methodName, record, input, hasArg) 

    let testRec = { 
        genericUnion = function Just x -> x | _ -> 0 
        simpleMethod = fun input -> input.Length
        listsMethod = fun xs -> Seq.sum xs
    }

    testList "FSharpRecord tests" [
        testCase "Invoking listsMethod" <| fun () ->
            let input = [1 .. 10]
            let output = invoke "listsMethod" testRec (box input) true
            equal 55 (unbox<int> output)

        testCase "Invoking simpleMethod" <| fun () ->
            let input = "hello"
            let output = invoke "simpleMethod" testRec (box input) true
            equal 5 (unbox<int> output)

        testCase "Invkoing genericUnion on record dynamically works" <| fun () ->
            let input = Just 5
            let output = invoke "genericUnion" testRec (box input) true
            equal 5 (unbox<int> output)
    ]

let serverTests = 

    let implementation = TestImplementation.implementation
    
    testList "Server Dynamic Invoke Tests" [

        testCaseAsync "Invoking when input is integer" <| async {
            let! dynamicResult = ServerSide.dynamicallyInvoke "echoInteger" implementation (box 5) true
            equal 10 (unbox<int> dynamicResult)
        }

        testCaseAsync "Invoking when input is string" <| async {
            let! result = ServerSide.dynamicallyInvoke "getLength" implementation (box "hello") true
            equal 5 (unbox<int> result)
        }

        testCaseAsync "Invoking when input is option some" <| async {
            let! result = ServerSide.dynamicallyInvoke "echoOption" implementation (box (Some 5)) true
            equal 10 (unbox<int> result)
        }

        testCaseAsync "Invoking with input is option none" <| async {
            let input : Option<int> = None
            let! result = ServerSide.dynamicallyInvoke "echoOption" implementation (box input) true
            equal 0 (unbox<int> result)
        }

        testCaseAsync "Invoking when input is simple union: A" <| async {
            let input = A
            let! output = ServerSide.dynamicallyInvoke "simpleUnionInputOutput" implementation (box input) true
            equal B (unbox<AB> output)
        }

        testCaseAsync "Invoking when input is simple union: B" <| async {
            let input = B
            let! output = ServerSide.dynamicallyInvoke "simpleUnionInputOutput" implementation (box input) true
            equal A (unbox<AB> output)
        }

        testCaseAsync "Generic union input: Maybe<int>" <| async {
            let input = Just 5
            let! output = ServerSide.dynamicallyInvoke "genericUnionOutput" implementation (box input) true
            equal 5 (unbox<int> output)
        }
    ]

let allTests = 
    testList "All Tests " [
        fsharpRecordTests
        serverTests
    ]