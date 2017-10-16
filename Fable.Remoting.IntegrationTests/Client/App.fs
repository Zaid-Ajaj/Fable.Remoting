module App

open Fable.Core
open Fable.Remoting.Client
open SharedTypes

let server = Proxy.createWithBuilder<IServer> routeBuilder

QUnit.registerModule "Fable.Remoting"

QUnit.test "IServer.getLegth" <| fun test ->
    let finish = test.async()
    async {
        let! result = server.getLength "hello"
        do test.equal result 5
        do finish()
    } 
    |> Async.StartImmediate

QUnit.test "ISever.echoInteger" <| fun test ->
    let finish = test.async()
    async {
        let! fstResult = server.echoInteger 20
        let! sndResult = server.echoInteger 15
        do test.equal fstResult 20
        do test.equal sndResult 15
        do finish()
    } 
    |> Async.StartImmediate


QUnit.test "IServer.echoString" <| fun test ->
    let finish = test.async()
    async {
        let! result1 = server.echoString ""
        let! result2 = server.echoString "this one"
        let! result3 = server.echoString null
        do test.equal result1 ""
        do test.equal result2 "this one"
        do test.equal true (isNull result3)
        do finish()
    } 
    |> Async.StartImmediate

QUnit.test "IServer.echoBool" <| fun test -> 
    let finish = test.async()
    async {
        let! fstTrue = server.echoBool true
        let! fstFalse = server.echoBool false
        do test.equal fstTrue true
        do test.equal fstFalse false
        do finish()
    } |> Async.StartImmediate

open System

let datesEqual (test: QUnit.Asserter) (x: DateTime) (y: DateTime) = 
    test.equal x.Year y.Year
    test.equal x.Day y.Day
    test.equal x.Month y.Month
    test.equal x.Hour y.Hour
    test.equal x.Minute y.Minute
    test.equal x.Second y.Second


QUnit.test "IServer.echoIntOption" <| fun test -> 
    let finish = test.async()
    async {
        let! fstResult = server.echoIntOption (Some 5)
        let! sndResult = server.echoIntOption None
        do test.equal true (fstResult = Some 5)
        do test.equal true (sndResult = None)
        do finish()
    } |> Async.StartImmediate

QUnit.test "IServer.echoStringOption" <| fun test -> 
    let finish = test.async()
    async {
        let! fstResult = server.echoStringOption (Some "hello")
        let! sndResult = server.echoStringOption None
        do test.equal true (fstResult = Some "hello")
        do test.equal true (sndResult = None)
        do finish()
    } |> Async.StartImmediate

QUnit.test "IServer.echoSimpleUnionType" <| fun test ->
    let finish = test.async()
    async {
        let! result1 = server.echoSimpleUnionType One
        let! result2 = server.echoSimpleUnionType Two
        do test.equal true (result1 = One)
        do test.equal true (result2 = Two)
        do finish()
    } 
    |> Async.StartImmediate

QUnit.test "IServer.echoGenericUnionInt" <| fun test -> 
    let finish = test.async()
    async {
        let! result1 = server.echoGenericUnionInt (Just 5)
        let! result2 = server.echoGenericUnionInt (Just 10)
        let! result3 = server.echoGenericUnionInt Nothing

        do test.equal true (result1 = Just 5)
        do test.equal true (result2 = Just 10)
        do test.equal true (result3 = Nothing)

        do finish()
    } 
    |> Async.StartImmediate

QUnit.test "IServer.echoGenericUnionString" <| fun test -> 
    let finish = test.async()
    async {
        let! result1 = server.echoGenericUnionString (Just "")
        let! result2 = server.echoGenericUnionString (Just null)
        let! result3 = server.echoGenericUnionString (Just "you")
        let! result4 = server.echoGenericUnionString Nothing

        do test.equal true (result1 = Just "")
        do test.equal true (result2 = Just null)
        do test.equal true (result3 = Just "you")
        do test.equal true (result4 = Nothing)

        do finish()
    } 
    |> Async.StartImmediate


QUnit.test "IServer.echoRecord" <| fun test -> 
    let finish = test.async()
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

        do finish()
    }
    |> Async.StartImmediate



QUnit.setTimeout 5000




QUnit.test "IServer.echoNestedGeneric" <| fun test ->
    let finish = test.async()

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
        do finish()
    }
    |> Async.StartImmediate


QUnit.test "IServer.echoIntList" <| fun test -> 
    let finish = test.async()
    async {
        let! output = server.echoIntList [1 .. 5]
        do test.equal true (output = [1;2;3;4;5])

        let! echoedList = server.echoIntList []
        do test.equal true (List.isEmpty echoedList)
        do finish()
    }
    |> Async.StartImmediate


QUnit.test "IServer.echoStringList" <| fun test -> 
    let finish = test.async()
    async {
        let! output = server.echoStringList ["one"; "two"; null]
        do test.equal true (output = ["one"; "two"; null])

        let! echoedList = server.echoStringList []
        do test.equal true (List.isEmpty echoedList)
        do finish()
    }
    |> Async.StartImmediate

QUnit.test "IServer.echoBoolList" <| fun test -> 
    let finish = test.async()
    async {
        let! output = server.echoBoolList [true; false; true]
        do test.equal true (output = [true; false; true])

        let! echoedList = server.echoStringList []
        do test.equal true (List.isEmpty echoedList)
        do finish()
    }
    |> Async.StartImmediate


QUnit.test "IServer.echoListOfListsOfStrings" <| fun test ->
    let finish = test.async()
    async {
        let! output = server.echoListOfListsOfStrings [["1"; "2"]; ["3"; "4";"5"]]
        do test.equal true (output =  [["1"; "2"]; ["3"; "4";"5"]])
        do finish()
    }
    |> Async.StartImmediate