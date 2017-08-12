namespace Fable.Remoting.Server.Tests

open NUnit.Framework
open Fable.Remoting.Server

[<TestFixture>]
module FSharpRecordTests = 

    let implementation = TestImplementation.implementation

    [<Test>]
    let ``Invoking with input is integer``() = 
        let result = ServerSide.dynamicallyInvoke "echoInteger" implementation (box 5) true
        async {
            let! dynamicResult = result
            do Assert.AreEqual(10, unbox<int> dynamicResult)
        } 
        |> Async.RunSynchronously

    [<Test>]
    let ``Invoking with input is string``() = 
        let result = ServerSide.dynamicallyInvoke "getLength" implementation (box "hello") true
        async {
            let! dynamicResult = result
            do Assert.AreEqual(5, unbox<int> dynamicResult)
        } 
        |> Async.RunSynchronously

    [<Test>]
    let ``Invoking with input is option some``() = 
        let result = ServerSide.dynamicallyInvoke "echoOption" implementation (box (Some 5)) true
        async {
            let! dynamicResult = result
            do Assert.AreEqual(10, unbox<int> dynamicResult)
        } 
        |> Async.RunSynchronously


    [<Test>]
    let ``Invoking with input is option none``() = 
        let input : Option<int> = None
        let result = ServerSide.dynamicallyInvoke "echoOption" implementation (box input) true
        async {
            let! dynamicResult = result
            do Assert.AreEqual(0, unbox<int> dynamicResult)
        } 
        |> Async.RunSynchronously