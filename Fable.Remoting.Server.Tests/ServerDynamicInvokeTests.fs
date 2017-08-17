module ServerDynamicInvokeTests 

open Expecto
open Fable.Remoting.Server
open Types

let equal x y = Expect.equal true (x = y) (sprintf "%A = %A" x y)
let pass () = Expect.equal true true ""   
let fail () = Expect.equal false true ""

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
    ]