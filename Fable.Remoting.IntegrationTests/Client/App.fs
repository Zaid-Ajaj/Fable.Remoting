module App

open Fable.Remoting.Client
open SharedTypes


Proxy.onError <| fun errorInfo ->
    printfn "Handling server error in the client"
    printfn "Recieved %A" errorInfo.error

let server = Proxy.createWithBuilder<IServer> routeBuilder

QUnit.registerModule "Fable.Remoting"

QUnit.testCaseAsync "IServer.getLegth" <| fun test ->
    async {
        let! result = server.getLength "hello"
        do test.equal result 5
    } 

QUnit.testCaseAsync "ISever.echoInteger" <| fun test ->
    async {
        let! fstResult = server.echoInteger 20
        let! sndResult = server.echoInteger 15
        do test.equal fstResult 20
        do test.equal sndResult 15
    } 


QUnit.testCaseAsync "IServer.echoString" <| fun test ->
    async {
        let! result1 = server.echoString ""
        let! result2 = server.echoString "this one"
        let! result3 = server.echoString null
        do test.equal result1 ""
        do test.equal result2 "this one"
        do test.equal true (isNull result3)
    } 

QUnit.testCaseAsync "IServer.echoBool" <| fun test -> 
    async {
        let! fstTrue = server.echoBool true
        let! fstFalse = server.echoBool false
        do test.equal fstTrue true
        do test.equal fstFalse false
    }

open System

let datesEqual (test: QUnit.Asserter) (x: DateTime) (y: DateTime) = 
    test.equal x.Year y.Year
    test.equal x.Day y.Day
    test.equal x.Month y.Month
    test.equal x.Hour y.Hour
    test.equal x.Minute y.Minute
    test.equal x.Second y.Second


QUnit.testCaseAsync "IServer.echoIntOption" <| fun test -> 
    async {
        let! fstResult = server.echoIntOption (Some 5)
        let! sndResult = server.echoIntOption None
        do test.equal true (fstResult = Some 5)
        do test.equal true (sndResult = None)
    }

QUnit.testCaseAsync "IServer.echoStringOption" <| fun test -> 
    async {
        let! fstResult = server.echoStringOption (Some "hello")
        let! sndResult = server.echoStringOption None
        do test.equal true (fstResult = Some "hello")
        do test.equal true (sndResult = None)
    } 

QUnit.testCaseAsync "IServer.echoSimpleUnionType" <| fun test ->
    async {
        let! result1 = server.echoSimpleUnionType One
        let! result2 = server.echoSimpleUnionType Two
        do test.equal true (result1 = One)
        do test.equal true (result2 = Two)
    } 

QUnit.testCaseAsync "IServer.echoGenericUnionInt" <| fun test -> 
    async {
        let! result1 = server.echoGenericUnionInt (Just 5)
        let! result2 = server.echoGenericUnionInt (Just 10)
        let! result3 = server.echoGenericUnionInt Nothing

        do test.equal true (result1 = Just 5)
        do test.equal true (result2 = Just 10)
        do test.equal true (result3 = Nothing)
    } 

QUnit.testCaseAsync "IServer.echoGenericUnionString" <| fun test -> 
    async {
        let! result1 = server.echoGenericUnionString (Just "")
        let! result2 = server.echoGenericUnionString (Just null)
        let! result3 = server.echoGenericUnionString (Just "you")
        let! result4 = server.echoGenericUnionString Nothing

        do test.equal true (result1 = Just "")
        do test.equal true (result2 = Just null)
        do test.equal true (result3 = Just "you")
        do test.equal true (result4 = Nothing)
    } 


QUnit.testCaseAsync "IServer.echoRecord" <| fun test -> 
    let record1 = { Prop1 = "hello"; Prop2 = 10; Prop3 = None }
    let record2 = { Prop1 = ""; Prop2 = 20; Prop3 = Some 10 }
    let record3 = { Prop1 = null; Prop2 = 30; Prop3 = Some 20  }
    async {
        let! result1 = server.echoRecord record1
        let! result2 = server.echoRecord record2
        let! result3 = server.echoRecord record3

        do test.equal true (result1 = record1)
        do test.equal true (result2 = record2)
        do test.equal true (result3 = record3)
    }



QUnit.setTimeout 5000




QUnit.testCaseAsync "IServer.echoNestedGeneric" <| fun test ->

    let input : GenericRecord<Maybe<int option>> = {
        Value = Just (Some 5)
        OtherValue = 2
    }

    let input2 : GenericRecord<Maybe<int option>> = {
        Value = Just (None)
        OtherValue = 2
    }

    async {
        let! result1 = server.echoNestedGeneric input
        let! result2 = server.echoNestedGeneric input2
        do test.equal true (input = result1)
        do test.equal true (input2 = result2)
    }


QUnit.testCaseAsync "IServer.echoIntList" <| fun test -> 
    async {
        let! output = server.echoIntList [1 .. 5]
        do test.equal true (output = [1;2;3;4;5])

        let! echoedList = server.echoIntList []
        do test.equal true (List.isEmpty echoedList)
    }


QUnit.testCaseAsync "IServer.echoStringList" <| fun test -> 
    async {
        let! output = server.echoStringList ["one"; "two"; null]
        do test.equal true (output = ["one"; "two"; null])

        let! echoedList = server.echoStringList []
        do test.equal true (List.isEmpty echoedList)
    }

QUnit.testCaseAsync "IServer.echoBoolList" <| fun test -> 
    async {
        let! output = server.echoBoolList [true; false; true]
        do test.equal true (output = [true; false; true])

        let! echoedList = server.echoStringList []
        do test.equal true (List.isEmpty echoedList)
    }


QUnit.testCaseAsync "IServer.echoListOfListsOfStrings" <| fun test ->
    async {
        let! output = server.echoListOfListsOfStrings [["1"; "2"]; ["3"; "4";"5"]]
        do test.equal true (output =  [["1"; "2"]; ["3"; "4";"5"]])
    }

QUnit.testCaseAsync "IServer.echoResult for Result<int, string>" <| fun test ->
    async {
        let! outputOk = server.echoResult (Ok 15)
        match outputOk with
        | Ok 15 -> test.pass()
        | otherwise -> test.fail()

        let! outputError = server.echoResult (Error "hello")
        match outputError with
        | Error "hello" -> test.pass()
        | otherwise -> test.fail()
    } 

QUnit.testCaseAsync "IServer.echoMap" <| fun test ->
    async {
        let input = ["hello", 1] |> Map.ofList
        let! output = server.echoMap input
        match input = output with
        | true -> test.pass()
        | false -> test.fail()
    }

QUnit.testCaseAsync "IServer.echoBigInteger" <| 
    fun test ->
        async {
            let n = 1I
            let! output = server.echoBigInteger n
            test.equal true (output = n)

            let n = 2I
            let! output = server.echoBigInteger n
            test.equal true (output = n)

            let n = -1I
            let! output = server.echoBigInteger n
            test.equal true (output = n)

            let n = -2I
            let! output = server.echoBigInteger n
            test.equal true (output = n)

            let n = 100I
            let! output = server.echoBigInteger n
            test.equal true (output = n)
        }

QUnit.testCaseAsync "IServer.throwError" <| fun test ->
    async {
        try
          test.expect 0
          let! output = server.throwError()
          printfn "%s" output
        with
         | ex -> 
            printfn "Qunit.testCase error handler %s" ex.Message
    }

QUnit.testCaseAsync "IServer.mutliArgFunc" <| fun test ->
    async { 
        let! output = server.multiArgFunc "hello" 10 false
        test.equal 15 output 

        let! sndOutput = server.multiArgFunc "byebye" 5 true
        test.equal 12 sndOutput
    }