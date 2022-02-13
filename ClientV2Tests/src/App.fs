﻿module App

open System.Threading
open Fable.Core
open Fable.Core.JsInterop
open Fable.Remoting.Client
open SharedTypes
open Fable.SimpleJson
open Fable.Mocha
open System
open System.Collections.Generic

let server =
    Remoting.createApi()
    |> Remoting.withRouteBuilder routeBuilder
    |> Remoting.buildProxy<IServer>

let binaryServer =
    Remoting.createApi()
    |> Remoting.withRouteBuilder routeBuilder
    |> Remoting.withBinarySerialization
    |> Remoting.buildProxy<IBinaryServer>

type test =
    static member equal a b = Expect.equal a b "They are equal"
    static member areEqual a b = Expect.equal a b "They are equal"
    static member pass() = Expect.isTrue true "It must be true"
    static member fail() = Expect.isTrue false "It must be false"
    static member isTrue x = Expect.isTrue x "It must be true"
    static member unexpected (x: 't) = Expect.isTrue false (Json.stringify x)
    static member failwith x = failwith x
    static member passWith x = Expect.isTrue true x

let datesEqual (x: DateTime) (y: DateTime) =
    test.equal x.Year y.Year
    test.equal x.Day y.Day
    test.equal x.Month y.Month
    test.equal x.Hour y.Hour
    test.equal x.Minute y.Minute
    test.equal x.Second y.Second

let largeRecursiveRecord = createRecursiveRecord 5 7

let serverTests =
    testList "Fable.Remoting" [
        testCase "Proxy.combineWithBaseUrlWorks" <| fun _ ->
            let route = "/IMusicStore/getLength"
            ["http://api.example.com"; "http://api.example.com/"]
            |> List.map (Some >> Proxy.combineRouteWithBaseUrl route)
            |> List.distinct
            |> function
                | [ "http://api.example.com/IMusicStore/getLength" ] -> test.pass()
                | otherwise -> test.fail()

        testCaseAsync "IServer.simulateLongComputation cancellation" <|
            async {
                let tokenSource = new CancellationTokenSource(250)
                let work = async {
                    do! server.simulateLongComputation 5000
                }

                let! result =
                    Async.StartAsPromise(work, tokenSource.Token)
                    |> Async.AwaitPromise
                    |> Async.Catch

                match result with
                | Choice.Choice1Of2 _ ->
                    test.fail()
                | Choice2Of2 _ ->
                    test.pass()
            }

        testCaseAsync "IServer.getLength" <|
            async {
                let! result = server.getLength "hello"
                do test.equal result 5
            }

        testCaseAsync "IServer.echoTupleMap" <|
            async {
                let! result = server.echoTupleMap (Map.ofList [(1,1), 1])
                match Map.toList result with
                | [ (1,1), 1 ] -> test.pass()
                | otherwise -> test.failwith "Map<int * int, int> fails"
            }

        testCaseAsync "IServer.echoTupleSet" <|
            async {
                let! result = server.echoTupleSet (Set.ofList [(1,1)])
                match Set.toList result with
                | [ (1,1) ] -> test.pass()
                | otherwise -> test.failwith "Set<int * int> fails"
            }

        testCaseAsync "IServer.returnUnit" <|
            async {
                let! result = server.returnUnit()
                do! Async.Sleep 1000
                do test.pass()
            }

        testCaseAsync "IServer.intToUnit" <|
            async {
                let! result = server.intToUnit 42
                do! Async.Sleep 100
                do test.pass()
            }

        testCaseAsync "IServer.tupleToUnit" <|
            async {
                let! result = server.tupleToUnit (42, "Hello world")
                do! Async.Sleep 100
                do test.pass()
            }

        testCaseAsync "IServer.tupleToTuple" <|
            async {
                let! (text, number) = server.tupleToTuple (42, "Hello world")
                do test.areEqual text "Hello world"
                do test.areEqual number 42
            }

        testCaseAsync "IServer.echoAnonymousRecord" <|
            async {
                let! result = server.echoAnonymousRecord (Just {| name = "John" |})
                match result with
                | Just record -> test.equal "John" record.name
                | otherwise -> test.failwith "Unexpected result"
            }

        testCaseAsync "IServer.echoNestedAnonRecord" <|
            async {
                let! result = server.echoNestedAnonRecord (Just {| nested  = {| name = "John" |} |})
                match result with
                | Just record -> test.equal "John" record.nested.name
                | otherwise -> test.failwith "Unexpected result"
            }

        testCaseAsync "IServer.binaryContent" <|
            async {
                let! result = server.binaryContent()
                test.equal 3 result.Length
                test.equal true (result = [| byte 1; byte 2; byte 3|])
            }

        testCaseAsync "IServer.echoTestCommand" <|
            async {
                let firstGuid = Guid.NewGuid()
                let testCommand : TestCommand = {
                    Data = {
                        CataA = "CataA"
                        CataC = "CataC"
                        CataB = Map.ofList [
                            firstGuid, {
                                MataA = "MataA"
                                MataC = "MataC"
                                MataB = Map.ofList [
                                    firstGuid, { Text = "text"; Value = "value" }
                                ]
                            }
                        ]
                    }
                }

                let! output = server.echoTestCommand testCommand
                test.equal true (output = testCommand)
            }

        testCaseAsync "IServer.privateConstructor" <|
            async {
                let input = String50.Create "Hello"
                let! output = server.privateConstructor input
                test.equal "Hello" (output.Read())
            }

        testCaseAsync "IServer.echoRemoteWorkEntity" <|
            async {
                let entity = { RemoteWork = RequiredInput.validOrFail (ValidUserInput (RemoteWork "Fully Remote")) }
                let! echoedEntity = server.echoRemoteWorkEntity entity
                test.equal true (echoedEntity.RemoteWork = RemoteWork "Fully Remote")
            }

        testCaseAsync "IServer.binaryContent" <|
            async {
                let input = [| byte 1; byte 2; byte 3|]
                let! output = server.binaryInputOutput input
                test.equal 3 output.Length
                test.equal true (input = output)
            }

        testCaseAsync "IServer.echoToken" <|
            async {
                let! output = server.echoToken (Token "Hello there")
                test.equal output (Token "Hello there")
            }

        testCaseAsync "ISever.echoInteger" <|
            async {
                let! fstResult = server.echoInteger 20
                let! sndResult = server.echoInteger 15
                do test.equal fstResult 20
                do test.equal sndResult 15
            }

        testCaseAsync "IServer.simpleUnit" <|
            async {
                let! result = server.simpleUnit()
                do test.equal result 42
            }

        testCaseAsync "IServer.echoString" <|
            async {
                let! result1 = server.echoString ""
                let! result2 = server.echoString "this one"
                let! result3 = server.echoString null
                do test.equal result1 ""
                do test.equal result2 "this one"
                do test.equal true (isNull result3)
            }

        testCaseAsync "IServer.echoRecordWithChar" <|
            async {
                let input = { CharValue = '*' }
                let! output = server.echoRecordWithChar input
                test.equal input.CharValue output.CharValue
            }

        testCaseAsync "IServer.echoRecordWithChar using characters with accents" <|
            async {
                let input = { CharValue = 'ŕ' }
                let! output = server.echoRecordWithChar input
                test.equal input.CharValue output.CharValue
            }

        testCaseAsync "IServer.echoUnionOfOtherUnions" <|
            async {
                let! result = server.echoUnionOfOtherUnions (MyDU.CustomCase (set [SomeOtherDU.SomeOtherCase]))
                do test.equal result (MyDU.CustomCase (set [SomeOtherDU.SomeOtherCase]))
            }

        testCaseAsync "IServer.echoBool" <|
            async {
                let! fstTrue = server.echoBool true
                let! fstFalse = server.echoBool false
                do test.equal fstTrue true
                do test.equal fstFalse false
            }

        testCaseAsync "IServer.mapRecordAsKey" <|
            async {
                let! result = server.mapRecordAsKey()
                result
                |> Map.toList
                |> function
                    | [ { Key = 1; Value = "Value" }, 1 ] -> test.pass()
                    | otherwise -> test.failwith (sprintf "%A" otherwise)
            }

        testCaseAsync "IServer.mapDateTimeOffsetAsKey" <|
            async {
                let now = DateTimeOffset.Now
                let input = Map.ofList [ now, 10 ]
                let! output = server.mapDateTimeOffsetAsKey input
                test.areEqual input output
            }

        testCaseAsync "IServer.echoIntKeyMap" <|
            async {
                let input = Map.ofList [ 10, 10; 20,20 ]
                let! output = server.echoIntKeyMap input
                test.areEqual input output
            }

        testCaseAsync "IServer.echoBigIntKeyMap" <|
            async {
                let input = Map.ofList [ 10I, 10; 20I,20 ]
                let! output = server.echoBigIntKeyMap input
                test.areEqual input output
            }

        testCaseAsync "IServer.echoLongKeyMap" <|
            async {
                let input = Map.ofList [ 10L, 10; 20L,20 ]
                let! output = server.echoLongKeyMap input
                test.areEqual input output
            }

        testCaseAsync "IServer.echoDecimalKeyMap" <|
            async {
                let input = Map.ofList [ 10M, 10; 20M,20 ]
                let! output = server.echoDecimalKeyMap input
                test.areEqual input output
            }

        testCaseAsync "IServer.setRecordAsValue" <|
            async {
                let! result = server.setRecordAsValue()
                result
                |> Set.toList
                |> function
                    | [ { Key = 1; Value = "Value" } ] -> test.pass()
                    | otherwise -> test.failwith (sprintf "%A" otherwise)
            }

        testCaseAsync "IServer.echoIntOption" <|
            async {
                let! fstResult = server.echoIntOption (Some 5)
                let! sndResult = server.echoIntOption None
                do test.equal true (fstResult = Some 5)
                do test.equal true (sndResult = None)
            }

        testCaseAsync "IServer.echoStringOption" <|
            async {
                let! fstResult = server.echoStringOption (Some "hello")
                let! sndResult = server.echoStringOption None
                do test.equal true (fstResult = Some "hello")
                do test.equal true (sndResult = None)
            }

        testCaseAsync "IServer.echoPrimitiveLong" <|
            async {
                let! fstResult = server.echoPrimitiveLong (20L)
                let! sndResult = server.echoPrimitiveLong 0L
                let! thirdResult = server.echoPrimitiveLong -20L
                do test.equal true (fstResult = 20L)
                do test.equal true (sndResult = 0L)
                do test.equal true (thirdResult = -20L)
            }

        testCaseAsync "IServer.echoPrimitiveLong with large values" <|
            async {
                let! fstResult = server.echoPrimitiveLong System.Int64.MaxValue
                let! sndResult = server.echoPrimitiveLong System.Int64.MinValue
                let! thirdResult = server.echoPrimitiveLong 637588453436987750L
                do test.equal true (fstResult = System.Int64.MaxValue)
                do test.equal true (sndResult = System.Int64.MinValue)
                do test.equal true (thirdResult = 637588453436987750L)
            }

        testCaseAsync "IServer.echoComplexLong" <|
            async {
                let input = { Value = 20L; OtherValue = 10 }
                let! output = server.echoComplexLong input
                do test.equal true (input = output)
            }

        testCaseAsync "IServer.echoOptionalLong" <|
            async {
                let! fstResult = server.echoOptionalLong (Some 20L)
                let! sndResult = server.echoOptionalLong None
                do test.equal true (fstResult = (Some 20L))
                do test.equal true (sndResult = None)
            }

        testCaseAsync "IServer.echoSingleDULong" <|
            async {
                let! output = server.echoSingleDULong (SingleLongCase 20L)
                do test.equal true (output = (SingleLongCase 20L))
            }

        testCaseAsync "IServer.echoLongInGenericUnion" <|
            async {
                let! output = server.echoLongInGenericUnion (Just 20L)
                let! result = server.echoLongInGenericUnion Nothing
                do test.equal true (output = Just 20L)
                do test.equal true (result = Nothing)
            }

        testCaseAsync "IServer.echoSimpleUnionType" <|
            async {
                let! result1 = server.echoSimpleUnionType One
                let! result2 = server.echoSimpleUnionType Two
                do test.equal true (result1 = One)
                do test.equal true (result2 = Two)
            }

        testCaseAsync "IServer.echoGenericUnionInt" <|
            async {
                let! result1 = server.echoGenericUnionInt (Just 5)
                let! result2 = server.echoGenericUnionInt (Just 10)
                let! result3 = server.echoGenericUnionInt Nothing

                do test.equal true (result1 = Just 5)
                do test.equal true (result2 = Just 10)
                do test.equal true (result3 = Nothing)
            }

        testCaseAsync "IServer.echoGenericUnionString" <|
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


        testCaseAsync "IServer.echoRecord" <|
            async {
                let record1 = { Prop1 = "hello"; Prop2 = 10; Prop3 = None }
                let record2 = { Prop1 = ""; Prop2 = 20; Prop3 = Some 10 }
                let record3 = { Prop1 = null; Prop2 = 30; Prop3 = Some 20  }
                let! result1 = server.echoRecord record1
                let! result2 = server.echoRecord record2
                let! result3 = server.echoRecord record3

                do test.equal true (result1 = record1)
                do test.equal true (result2 = record2)
                do test.equal true (result3 = record3)
            }


        testCaseAsync "IServer.echoHighScores" <|
            async {
                let input = [|
                    { Name = "alfonsogarciacaro"; Score =  100 }
                    { Name = "theimowski"; Score =  28 }
                |]
                let! result = server.echoHighScores input
                do test.equal "alfonsogarciacaro" result.[0].Name
                do test.equal 100 result.[0].Score
                do test.equal "theimowski" result.[1].Name
                do test.equal 28 result.[1].Score
            }

        testCaseAsync "IServer.echoHighScores without do" <|
            async {
                let input = [|
                    { Name = "alfonsogarciacaro"; Score =  100 }
                    { Name = "theimowski"; Score =  28 }
                |]
                let! result = server.echoHighScores input
                test.equal "alfonsogarciacaro" result.[0].Name
                test.equal 100 result.[0].Score
                test.equal "theimowski" result.[1].Name
                test.equal 28 result.[1].Score
            }

        testCaseAsync "IServer.echoHighScores" <|
            async {
                let! result = server.getHighScores()
                do test.equal "alfonsogarciacaro" result.[0].Name
                do test.equal 100 result.[0].Score
                do test.equal "theimowski" result.[1].Name
                do test.equal 28 result.[1].Score
            }

        testCaseAsync "IServer.echoHighScores without do" <|
            async {
                let! result = server.getHighScores()
                test.equal "alfonsogarciacaro" result.[0].Name
                test.equal 100 result.[0].Score
                test.equal "theimowski" result.[1].Name
                test.equal 28 result.[1].Score
            }


        testCaseAsync "IServer.echoNestedGeneric" <|
            async {
                let input : GenericRecord<Maybe<int option>> = {
                    Value = Just (Some 5)
                    OtherValue = 2
                }

                let input2 : GenericRecord<Maybe<int option>> = {
                    Value = Just (None)
                    OtherValue = 2
                }
                let! result1 = server.echoNestedGeneric input
                let! result2 = server.echoNestedGeneric input2
                do test.equal true (input = result1)
                do test.equal true (input2 = result2)
            }

        testCaseAsync "IServer.echoOtherDataC" <|
            async {
                let input = {
                    Byte = 200uy
                    SByte = -10y
                    Maybes = [ Just -120y; Nothing; Just 120y; Just 5y; Just -5y ]
                }

                let! result = server.echoOtherDataC input
                do test.equal true (input = result)
            }

        testCaseAsync "IServer.echoIntList" <|
            async {
                let! output = server.echoIntList [1 .. 5]
                do test.equal true (output = [1;2;3;4;5])

                let! echoedList = server.echoIntList []
                do test.equal true (List.isEmpty echoedList)
            }

        testCaseAsync "IServer.echoSingleCase" <|
            async {
                let! output = server.echoSingleCase (SingleCase 10)
                match output with
                | SingleCase 10 -> test.pass()
                | other -> test.fail()
            }

        testCaseAsync "IServer.echoStringList" <|
            async {
                let! output = server.echoStringList ["one"; "two"; null]
                do test.equal true (output = ["one"; "two"; null])

                let! echoedList = server.echoStringList []
                do test.equal true (List.isEmpty echoedList)
            }

        testCaseAsync "IServer.echoBoolList" <|
            async {
                let! output = server.echoBoolList [true; false; true]
                do test.equal true (output = [true; false; true])

                let! echoedList = server.echoStringList []
                do test.equal true (List.isEmpty echoedList)
            }

        testCaseAsync "IServer.echoListOfListsOfStrings" <|
            async {
                let! output = server.echoListOfListsOfStrings [["1"; "2"]; ["3"; "4";"5"]]
                do test.equal true (output =  [["1"; "2"]; ["3"; "4";"5"]])
            }

        testCaseAsync "IServer.echoResult for Result<int, string>" <|
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

        testCaseAsync "IServer.echoMap" <|
            async {
                let input = ["hello", 1] |> Map.ofList
                let! output = server.echoMap input
                match input = output with
                | true -> test.pass()
                | false -> test.fail()
            }

        testCaseAsync "IServer.echoSet" <|
            async {
                let input = ["hello"] |> Set.ofList
                let! output = server.echoSet input
                match input = output with
                | true -> test.pass()
                | false -> test.fail()
            }

        testCaseAsync "IServer.echoBigInteger" <|
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

        testCaseAsync "IServer.throwError" <|
            async {
                let! result = Async.Catch (server.throwError())
                match result with
                | Choice1Of2 output -> test.fail()
                | Choice2Of2 error ->
                    match error with
                    | :? ProxyRequestException as ex ->
                        if ex.ResponseText.Contains("Generating custom server error")
                        then test.pass()
                        else test.fail()
                    | otherwise -> test.fail()
            }

        testCaseAsync "IServer.throwBinaryError" <|
            async {
                let! result = Async.Catch (server.throwBinaryError())
                match result with
                | Choice1Of2 output -> test.fail()
                | Choice2Of2 error ->
                    match error with
                    | :? ProxyRequestException as ex ->
                        if ex.ResponseText.Contains("Generating custom server error for binary response")
                        then test.pass()
                        else test.fail()
                    | otherwise -> test.fail()
            }


        testCaseAsync "IServer.mutliArgFunc" <|
            async {
                let! output = server.multiArgFunc "hello" 10 false
                test.equal 15 output

                let! sndOutput = server.multiArgFunc "byebye" 5 true
                test.equal 12 sndOutput
            }

        testCaseAsync "IServer.mutliArgFunc partially applied" <|
            async {
                let partialFunc = server.multiArgFunc "hello" 10
                let! output =  partialFunc false
                test.equal 15 output

                let otherPartialFunc = server.multiArgFunc "byebye"
                let! sndOutput = otherPartialFunc 5 true
                test.equal 12 sndOutput
            }

        testCaseAsync "IServer.pureAsync" <|
            async {
                let! output = server.pureAsync
                test.equal 42 output
            }

        testCaseAsync "IServer.asyncNestedGeneric" <|
            async {
                let! result = server.asyncNestedGeneric
                test.equal true (result = { OtherValue = 10; Value = Just (Some "value") })
            }

        testCaseAsync "IServer.multiArgComplex" <|
            async {
                let input = { OtherValue = 10; Value = Just (Some "value") }
                let! output = server.multiArgComplex false input
                test.equal true (input = output)
            }

        testCaseAsync "IServer.getSeq" <|
            async {
                let! output = server.getSeq()
                let maybes = List.ofSeq output
                match maybes with
                | [ Just 5; Nothing ] -> test.equal true true
                | _ -> test.equal false true
            }

        testCaseAsync "IServer.echoGenericMap" <|
            async {
                let input = Map.ofList [ "firstKey", Just 5; "secondKey", Nothing ]
                let! output = server.echoGenericMap input
                test.equal true (input = output)
            }

        testCaseAsync "IServer.echoRecursiveRecord" <|
            async {
                let input = {
                    Name = "root"
                    Children = [
                        { Name = "Child 1"; Children = [ { Name = "Grandchild"; Children = [ ] } ] }
                        { Name = "Child 1"; Children = [ ] }
                    ]
                }

                let! output = server.echoRecursiveRecord input
                test.equal true (output = input)
            }

        testCaseAsync "IServer.echoTree (recursive union)" <|
            async {
                let input = Branch(Branch(Leaf 10, Leaf 5), Leaf 5)
                let! output = server.echoTree input
                test.equal true (input = output)
            }

        testCaseAsync "IServer.multiArgComplex partially applied" <|
            async {
                let input = { OtherValue = 10; Value = Just (Some "value") }
                let partialF = fun x -> server.multiArgComplex false x
                let! output = partialF input
                test.equal true (input = output)
            }

        testCaseAsync "IServer.tuplesAndLists" <|
            async {
                let inputDict = Map.ofList [ "hello", 5 ]
                let inputStrings = [ "there!" ]
                let! outputDict = server.tuplesAndLists (inputDict, inputStrings)

                let expected = Map.ofList [ "hello", 5; "there!", 6 ]
                test.equal true (expected = outputDict)
            }

        testCaseAsync "IServer.datetime" <|
            async {
                let input = DateTime.Now
                let! output = server.echoDateTime input

                test.equal true (input = output)
            }

        testCaseAsync "IServer.datetimeoffset" <|
            async {
                let input = DateTimeOffset.Now
                let! output = server.echoDateTimeOffset input

                test.equal true (input = output)
            }

        testCaseAsync "IServer.largeRecursiveRecord" <|
            async {
                let input = largeRecursiveRecord
                let! output = server.echoRecursiveRecord input

                test.equal true (input = output)
            }

        testCaseAsync "IServer.echoPosition" <|
            async {
                let position = Cartesian({x = 0.0;y = 10.0; z = 6.0; w = 0.0; p = 0.0; r = -90.0}, CartesianConfig "N U T, 0, 0, 0")
                let! output = server.echoPosition position
                test.equal true (position = output)
            }

        testCaseAsync "IServer.command" <|
            async {
                let label = CommandLabel "Initializing programs"
                let identifier = IWantResponsesOn (ClientId "dDY_ftBDlUWjemgjP6leWw")
                let address = Address 2
                let position = Cartesian({x = 0.0;y = 10.0; z = 6.0; w = 0.0; p = 0.0; r = -90.0}, CartesianConfig "N U T, 0, 0, 0")
                let command = Requests.PositionSet(address, position)
                let! output = server.command(label, identifier, command)
                test.equal true (output = Some "Operation error")
            }

#if NAGAREYAMA
        testCaseAsync "IServer.echoDateOnlyMap" <|
            async {
                let input = [ (DateOnly.MinValue, DateOnly.MaxValue); (DateOnly.FromDayNumber 1000, DateOnly.FromDateTime DateTime.Now) ] |> Map.ofList
                let! output = server.echoDateOnlyMap input
                
                test.equal output input
            }

        testCaseAsync "IServer.echoTimeOnlyMap" <|
            async {
                let input = [ (TimeOnly.MinValue, TimeOnly.MaxValue); (TimeOnly (10, 20, 30, 400), TimeOnly.FromDateTime DateTime.Now) ] |> Map.ofList
                let! output = server.echoTimeOnlyMap input
                
                test.equal output input
            }
#endif
    ]

let binaryServerTests =
    testList "Fable.Remoting binary" [
        testCaseAsync "IBinaryServer.getLegth" <|
            async {
                let! result = binaryServer.getLength "hello"
                do test.equal result 5
            }

        testCaseAsync "IBinaryServer.mapDateTimeOffsetAsKey" <|
            async {
                let now = DateTimeOffset.Now
                let input = Map.ofList [ now, 10 ]
                let! output = binaryServer.mapDateTimeOffsetAsKey input
                test.areEqual input output
            }

        testCaseAsync "IBinaryServer.echoRecordWithChar" <|
            async {
                let input = { CharValue = 'ŕ' }
                let! output = binaryServer.echoRecordWithChar input
                test.equal input.CharValue output.CharValue
            }

        testCaseAsync "IBinaryServer.echoIntKeyMap" <|
            async {
                let input = Map.ofList [ 10, 10; 20,20 ]
                let! output = binaryServer.echoIntKeyMap input
                test.areEqual input output
            }

        testCaseAsync "IBinaryServer.echoBigIntKeyMap" <|
            async {
                let input = Map.ofList [ 10I, 10; 20I,20 ]
                let! output = binaryServer.echoBigIntKeyMap input
                test.areEqual input output
            }

        testCaseAsync "IBinaryServer.echoLongKeyMap" <|
            async {
                let input = Map.ofList [ 10L, 10; 20L,20 ]
                let! output = binaryServer.echoLongKeyMap input
                test.areEqual input output
            }

        testCaseAsync "IBinaryServer.echoDecimalKeyMap" <|
            async {
                let input = Map.ofList [ 10M, 10; 20M,20 ]
                let! output = binaryServer.echoDecimalKeyMap input
                test.areEqual input output
            }

        testCaseAsync "IBinaryServer.echoTupleMap" <|
            async {
                let! result = binaryServer.echoTupleMap (Map.ofList [(1,1), 1])
                match Map.toList result with
                | [ (1,1), 1 ] -> test.pass()
                | otherwise -> test.failwith "Map<int * int, int> fails"
            }

        testCaseAsync "IServer.echoTupleSet" <|
            async {
                let! result = server.echoTupleSet (Set.ofList [(1,1)])
                match Set.toList result with
                | [ (1,1) ] -> test.pass()
                | otherwise -> test.failwith "Set<int * int> fails"
            }

        testCaseAsync "IBinaryServer.returnUnit" <|
            async {
                let! result = binaryServer.returnUnit()
                do! Async.Sleep 1000
                do test.pass()
            }

        testCaseAsync "IBinaryServer.intToUnit" <|
            async {
                let! result = binaryServer.intToUnit 42
                do! Async.Sleep 100
                do test.pass()
            }

        testCaseAsync "IBinaryServer.tupleToUnit" <|
            async {
                let! result = binaryServer.tupleToUnit (42, "Hello world")
                do! Async.Sleep 100
                do test.pass()
            }

        testCaseAsync "IBinaryServer.tupleToTuple" <|
            async {
                let! (text, number) = binaryServer.tupleToTuple (42, "Hello world")
                do test.areEqual text "Hello world"
                do test.areEqual number 42
            }

        testCaseAsync "IBinaryServer.echoAnonymousRecord" <|
            async {
                let! result = binaryServer.echoAnonymousRecord (Just {| name = "John" |})
                match result with
                | Just record -> test.equal "John" record.name
                | otherwise -> test.failwith "Unexpected result"
            }

        testCaseAsync "IBinaryServer.echoNestedAnonRecord" <|
            async {
                let! result = binaryServer.echoNestedAnonRecord (Just {| nested  = {| name = "John" |} |})
                match result with
                | Just record -> test.equal "John" record.nested.name
                | otherwise -> test.failwith "Unexpected result"
            }

        testCaseAsync "IBinaryServer.binaryContent" <|
            async {
                let! result = binaryServer.binaryContent()
                test.equal 3 result.Length
                test.equal true (result = [| byte 1; byte 2; byte 3|])
            }

        testCaseAsync "IBinaryServer.privateConstructor" <|
            async {
                let input = String50.Create "Hello"
                let! output = binaryServer.privateConstructor input
                test.equal "Hello" (output.Read())
            }

        testCaseAsync "IBinaryServer.echoRemoteWorkEntity" <|
            async {
                let entity = { RemoteWork = RequiredInput.validOrFail (ValidUserInput (RemoteWork "Fully Remote")) }
                let! echoedEntity = binaryServer.echoRemoteWorkEntity entity
                test.equal true (echoedEntity.RemoteWork = RemoteWork "Fully Remote")
            }

        testCaseAsync "IBinaryServer.binaryContentInOut" <|
            async {
                let input = [| byte 1; byte 2; byte 3|]
                let! output = binaryServer.binaryInputOutput input
                test.equal 3 output.Length
                test.equal true (input = output)
            }

        testCaseAsync "IBinaryServer.echoInteger" <|
            async {
                let! fstResult = binaryServer.echoInteger 20
                let! sndResult = binaryServer.echoInteger 15
                do test.equal fstResult 20
                do test.equal sndResult 15
            }

        testCaseAsync "IBinaryServer.simpleUnit" <|
            async {
                let! result = binaryServer.simpleUnit()
                do test.equal result 42
            }

        testCaseAsync "IBinaryServer.echoString" <|
            async {
                let! result1 = binaryServer.echoString ""
                let! result2 = binaryServer.echoString "this one"
                let! result3 = binaryServer.echoString null
                do test.equal result1 ""
                do test.equal result2 "this one"
                do test.equal true (isNull result3)
            }

        testCaseAsync "IBinaryServer.echoBool" <|
            async {
                let! fstTrue = binaryServer.echoBool true
                let! fstFalse = binaryServer.echoBool false
                do test.equal fstTrue true
                do test.equal fstFalse false
            }

        testCaseAsync "IBinaryServer.mapRecordAsKey" <|
            async {
                let! result = binaryServer.mapRecordAsKey()
                result
                |> Map.toList
                |> function
                    | [ { Key = 1; Value = "Value" }, 1 ] -> test.pass()
                    | otherwise -> test.failwith (sprintf "%A" otherwise)
            }

        testCaseAsync "IServer.setRecordAsValue" <|
            async {
                let! result = server.setRecordAsValue()
                result
                |> Set.toList
                |> function
                    | [ { Key = 1; Value = "Value" } ] -> test.pass()
                    | otherwise -> test.failwith (sprintf "%A" otherwise)
            }

        testCaseAsync "IBinaryServer.echoIntOption" <|
            async {
                let! fstResult = binaryServer.echoIntOption (Some 5)
                let! sndResult = binaryServer.echoIntOption None
                do test.equal true (fstResult = Some 5)
                do test.equal true (sndResult = None)
            }

        testCaseAsync "IBinaryServer.echoStringOption" <|
            async {
                let! fstResult = binaryServer.echoStringOption (Some "hello")
                let! sndResult = binaryServer.echoStringOption None
                do test.equal true (fstResult = Some "hello")
                do test.equal true (sndResult = None)
            }

        testCaseAsync "IBinaryServer.echoPrimitiveLong" <|
            async {
                let! fstResult = binaryServer.echoPrimitiveLong (20L)
                let! sndResult = binaryServer.echoPrimitiveLong 0L
                let! thirdResult = binaryServer.echoPrimitiveLong -20L
                do test.equal true (fstResult = 20L)
                do test.equal true (sndResult = 0L)
                do test.equal true (thirdResult = -20L)
            }

        testCaseAsync "IBinaryServer.echoPrimitiveLong with large values" <|
            async {
                let! fstResult = binaryServer.echoPrimitiveLong System.Int64.MaxValue
                let! sndResult = binaryServer.echoPrimitiveLong System.Int64.MinValue
                do test.equal true (fstResult = System.Int64.MaxValue)
                do test.equal true (sndResult = System.Int64.MinValue)
            }

        testCaseAsync "IBinaryServer.echoComplexLong" <|
            async {
                let input = { Value = 20L; OtherValue = 10 }
                let! output = binaryServer.echoComplexLong input
                do test.equal true (input = output)
            }

        testCaseAsync "IBinaryServer.echoOptionalLong" <|
            async {
                let! fstResult = binaryServer.echoOptionalLong (Some 20L)
                let! sndResult = binaryServer.echoOptionalLong None
                do test.equal true (fstResult = (Some 20L))
                do test.equal true (sndResult = None)
            }

        testCaseAsync "IBinaryServer.echoSingleDULong" <|
            async {
                let! output = binaryServer.echoSingleDULong (SingleLongCase 20L)
                do test.equal true (output = (SingleLongCase 20L))
            }

        testCaseAsync "IBinaryServer.echoLongInGenericUnion" <|
            async {
                let! output = binaryServer.echoLongInGenericUnion (Just 20L)
                let! result = binaryServer.echoLongInGenericUnion Nothing
                do test.equal true (output = Just 20L)
                do test.equal true (result = Nothing)
            }

        testCaseAsync "IBinaryServer.echoSimpleUnionType" <|
            async {
                let! result1 = binaryServer.echoSimpleUnionType One
                let! result2 = binaryServer.echoSimpleUnionType Two
                do test.equal true (result1 = One)
                do test.equal true (result2 = Two)
            }

        testCaseAsync "IBinaryServer.echoGenericUnionInt" <|
            async {
                let! result1 = binaryServer.echoGenericUnionInt (Just 5)
                let! result2 = binaryServer.echoGenericUnionInt (Just 10)
                let! result3 = binaryServer.echoGenericUnionInt Nothing

                do test.equal true (result1 = Just 5)
                do test.equal true (result2 = Just 10)
                do test.equal true (result3 = Nothing)
            }

        testCaseAsync "IBinaryServer.echoGenericUnionString" <|
            async {
                let! result1 = binaryServer.echoGenericUnionString (Just "")
                let! result2 = binaryServer.echoGenericUnionString (Just null)
                let! result3 = binaryServer.echoGenericUnionString (Just "you")
                let! result4 = binaryServer.echoGenericUnionString Nothing

                do test.equal true (result1 = Just "")
                do test.equal true (result2 = Just null)
                do test.equal true (result3 = Just "you")
                do test.equal true (result4 = Nothing)
            }


        testCaseAsync "IBinaryServer.echoRecord" <|
            async {
                let record1 = { Prop1 = "hello"; Prop2 = 10; Prop3 = None }
                let record2 = { Prop1 = ""; Prop2 = 20; Prop3 = Some 10 }
                let record3 = { Prop1 = null; Prop2 = 30; Prop3 = Some 20  }
                let! result1 = binaryServer.echoRecord record1
                let! result2 = binaryServer.echoRecord record2
                let! result3 = binaryServer.echoRecord record3

                do test.equal true (result1 = record1)
                do test.equal true (result2 = record2)
                do test.equal true (result3 = record3)
            }


        testCaseAsync "IBinaryServer.echoHighScores" <|
            async {
                let input = [|
                    { Name = "alfonsogarciacaro"; Score =  100 }
                    { Name = "theimowski"; Score =  28 }
                |]
                let! result = binaryServer.echoHighScores input
                do test.equal "alfonsogarciacaro" result.[0].Name
                do test.equal 100 result.[0].Score
                do test.equal "theimowski" result.[1].Name
                do test.equal 28 result.[1].Score
            }

        testCaseAsync "IBinaryServer.echoHighScores without do" <|
            async {
                let input = [|
                    { Name = "alfonsogarciacaro"; Score =  100 }
                    { Name = "theimowski"; Score =  28 }
                |]
                let! result = binaryServer.echoHighScores input
                test.equal "alfonsogarciacaro" result.[0].Name
                test.equal 100 result.[0].Score
                test.equal "theimowski" result.[1].Name
                test.equal 28 result.[1].Score
            }

        testCaseAsync "IBinaryServer.echoHighScores" <|
            async {
                let! result = binaryServer.getHighScores()
                do test.equal "alfonsogarciacaro" result.[0].Name
                do test.equal 100 result.[0].Score
                do test.equal "theimowski" result.[1].Name
                do test.equal 28 result.[1].Score
            }

        testCaseAsync "IBinaryServer.echoHighScores without do" <|
            async {
                let! result = binaryServer.getHighScores()
                test.equal "alfonsogarciacaro" result.[0].Name
                test.equal 100 result.[0].Score
                test.equal "theimowski" result.[1].Name
                test.equal 28 result.[1].Score
            }


        testCaseAsync "IBinaryServer.echoNestedGeneric" <|
            async {
                let input : GenericRecord<Maybe<int option>> = {
                    Value = Just (Some 5)
                    OtherValue = 2
                }

                let input2 : GenericRecord<Maybe<int option>> = {
                    Value = Just (None)
                    OtherValue = 2
                }
                let! result1 = binaryServer.echoNestedGeneric input
                let! result2 = binaryServer.echoNestedGeneric input2
                do test.equal true (input = result1)
                do test.equal true (input2 = result2)
            }

        testCaseAsync "IBinaryServer.echoOtherDataC" <|
            async {
                let input = {
                    Byte = 200uy
                    SByte = -10y
                    Maybes = [ Just -120y; Nothing; Just 120y; Just 5y; Just -5y ]
                }

                let! result = binaryServer.echoOtherDataC input
                do test.equal true (input = result)
            }

        testCaseAsync "IBinaryServer.echoIntList" <|
            async {
                let! output = binaryServer.echoIntList [1 .. 5]
                do test.equal true (output = [1;2;3;4;5])

                let! echoedList = binaryServer.echoIntList []
                do test.equal true (List.isEmpty echoedList)
            }

        testCaseAsync "IBinaryServer.echoSingleCase" <|
            async {
                let! output = binaryServer.echoSingleCase (SingleCase 10)
                match output with
                | SingleCase 10 -> test.pass()
                | other -> test.fail()
            }

        testCaseAsync "IBinaryServer.echoStringList" <|
            async {
                let! output = binaryServer.echoStringList ["one"; "two"; null]
                do test.equal true (output = ["one"; "two"; null])

                let! echoedList = binaryServer.echoStringList []
                do test.equal true (List.isEmpty echoedList)
            }

        testCaseAsync "IBinaryServer.echoBoolList" <|
            async {
                let! output = binaryServer.echoBoolList [true; false; true]
                do test.equal true (output = [true; false; true])

                let! echoedList = binaryServer.echoStringList []
                do test.equal true (List.isEmpty echoedList)
            }

        testCaseAsync "IBinaryServer.echoListOfListsOfStrings" <|
            async {
                let! output = binaryServer.echoListOfListsOfStrings [["1"; "2"]; ["3"; "4";"5"]]
                do test.equal true (output =  [["1"; "2"]; ["3"; "4";"5"]])
            }

        testCaseAsync "IBinaryServer.echoResult for Result<int, string>" <|
            async {
                let! outputOk = binaryServer.echoResult (Ok 15)
                match outputOk with
                | Ok 15 -> test.pass()
                | otherwise -> test.fail()

                let! outputError = binaryServer.echoResult (Error "hello")
                match outputError with
                | Error "hello" -> test.pass()
                | otherwise -> test.fail()
            }

        testCaseAsync "IBinaryServer.echoMap" <|
            async {
                let input = ["hello", 1] |> Map.ofList
                let! output = binaryServer.echoMap input
                match input = output with
                | true -> test.pass()
                | false -> test.fail()
            }

        testCaseAsync "IServer.echoSet" <|
            async {
                let input = ["hello"] |> Set.ofList
                let! output = server.echoSet input
                match input = output with
                | true -> test.pass()
                | false -> test.fail()
            }

        testCaseAsync "IBinaryServer.echoBigInteger" <|
            async {
                let n = 1I
                let! output = binaryServer.echoBigInteger n
                test.equal true (output = n)

                let n = 2I
                let! output = binaryServer.echoBigInteger n
                test.equal true (output = n)

                let n = -1I
                let! output = binaryServer.echoBigInteger n
                test.equal true (output = n)

                let n = -2I
                let! output = binaryServer.echoBigInteger n
                test.equal true (output = n)

                let n = 100I
                let! output = binaryServer.echoBigInteger n
                test.equal true (output = n)
            }

        testCaseAsync "IBinaryServer.throwError" <|
            async {
                let! result = Async.Catch (binaryServer.throwError())
                match result with
                | Choice1Of2 output -> test.fail()
                | Choice2Of2 error ->
                    match error with
                    | :? ProxyRequestException as ex ->
                        if ex.ResponseText.Contains("Generating custom server error")
                        then test.pass()
                        else test.fail()
                    | otherwise -> test.fail()
            }

        testCaseAsync "IBinaryServer.throwBinaryError" <|
            async {
                let! result = Async.Catch (binaryServer.throwBinaryError())
                match result with
                | Choice1Of2 output -> test.fail()
                | Choice2Of2 error ->
                    match error with
                    | :? ProxyRequestException as ex ->
                        if ex.ResponseText.Contains("Generating custom server error for binary response")
                        then test.pass()
                        else test.fail()
                    | otherwise -> test.fail()
            }


        testCaseAsync "IBinaryServer.mutliArgFunc" <|
            async {
                let! output = binaryServer.multiArgFunc "hello" 10 false
                test.equal 15 output

                let! sndOutput = binaryServer.multiArgFunc "byebye" 5 true
                test.equal 12 sndOutput
            }

        testCaseAsync "IBinaryServer.mutliArgFunc partially applied" <|
            async {
                let partialFunc = binaryServer.multiArgFunc "hello" 10
                let! output =  partialFunc false
                test.equal 15 output

                let otherPartialFunc = binaryServer.multiArgFunc "byebye"
                let! sndOutput = otherPartialFunc 5 true
                test.equal 12 sndOutput
            }

        testCaseAsync "IBinaryServer.pureAsync" <|
            async {
                let! output = binaryServer.pureAsync
                test.equal 42 output
            }

        testCaseAsync "IBinaryServer.asyncNestedGeneric" <|
            async {
                let! result = binaryServer.asyncNestedGeneric
                test.equal true (result = { OtherValue = 10; Value = Just (Some "value") })
            }

        testCaseAsync "IBinaryServer.multiArgComplex" <|
            async {
                let input = { OtherValue = 10; Value = Just (Some "value") }
                let! output = binaryServer.multiArgComplex false input
                test.equal true (input = output)
            }

        testCaseAsync "IBinaryServer.echoGenericMap" <|
            async {
                let input = Map.ofList [ "firstKey", Just 5; "secondKey", Nothing ]
                let! output = binaryServer.echoGenericMap input
                test.equal true (input = output)
            }

        testCaseAsync "IBinaryServer.genericDictionary" <|
            async {
                let expected = Map.ofList [ "firstKey", Just 5; "secondKey", Nothing ] |> Dictionary<_, _>
                let! actual = binaryServer.genericDictionary ()

                test.equal expected.Count actual.Count

                for k, v in expected |> Seq.map (|KeyValue|) do
                    test.equal true (actual.[k] = v)
            }

        testCaseAsync "IBinaryServer.echoRecursiveRecord" <|
            async {
                let input = {
                    Name = "root"
                    Children = [
                        { Name = "Child 1"; Children = [ { Name = "Grandchild"; Children = [ ] } ] }
                        { Name = "Child 1"; Children = [ ] }
                    ]
                }

                let! output = binaryServer.echoRecursiveRecord input
                test.equal true (output = input)
            }

        testCaseAsync "IBinaryServer.echoTree (recursive union)" <|
            async {
                let input = Branch(Branch(Leaf 10, Leaf 5), Leaf 5)
                let! output = binaryServer.echoTree input
                test.equal true (input = output)
            }

        testCaseAsync "IBinaryServer.multiArgComplex partially applied" <|
            async {
                let input = { OtherValue = 10; Value = Just (Some "value") }
                let partialF = fun x -> binaryServer.multiArgComplex false x
                let! output = partialF input
                test.equal true (input = output)
            }

        testCaseAsync "IBinaryServer.tuplesAndLists" <|
            async {
                let inputDict = Map.ofList [ "hello", 5 ]
                let inputStrings = [ "there!" ]
                let! outputDict = binaryServer.tuplesAndLists (inputDict, inputStrings)

                let expected = Map.ofList [ "hello", 5; "there!", 6 ]
                test.equal true (expected = outputDict)
            }

        testCaseAsync "IBinaryServer.timespans" <|
            async {
                let input = TimeSpan.FromTicks 0L
                let! output = binaryServer.echoTimeSpan input
                test.equal true (input = output)

                let input = TimeSpan.FromDays -0.3
                let! output = binaryServer.echoTimeSpan input
                test.equal true (input = output)

                let input = TimeSpan.FromMilliseconds 999.
                let! output = binaryServer.echoTimeSpan input
                test.equal true (input = output)
            }
        testCaseAsync "IBinaryServer.unitOfMeasure" <|
            async {
                let input = 200s<SomeUnit>
                let! output = binaryServer.echoInt16WithMeasure input
                test.equal true (input = output)

                let input = 200<SomeUnit>
                let! output = binaryServer.echoIntWithMeasure input
                test.equal true (input = output)

                let input = 200L<SomeUnit>
                let! output = binaryServer.echoInt64WithMeasure input
                test.equal true (input = output)

                let input = 32313213121.1415926535m<SomeUnit>
                let! output = binaryServer.echoDecimalWithMeasure input
                test.equal true (input = output)

                let input = 3.14<SomeUnit>
                let! output = binaryServer.echoFloatWithMeasure input
                test.equal true (input = output)
            }
        testCaseAsync "IBinaryServer.enum" <|
            async {
                let input = SomeEnum.Val2
                let! output = binaryServer.echoEnum input
                test.equal true (input = output)
            }
        testCaseAsync "IBinaryServer.stringEnum" <|
            async {
                let input = SecondString
                let! output = binaryServer.echoStringEnum input
                test.equal true (input = output)
            }

        testCaseAsync "IBinaryServer.datetime" <|
            async {
                let input = DateTime.Now
                let! output = binaryServer.echoDateTime input

                test.equal true (input = output)
            }

        testCaseAsync "IBinaryServer.datetimeoffset" <|
            async {
                let input = DateTimeOffset.Now
                let! output = binaryServer.echoDateTimeOffset input

                test.equal true (input = output)
            }

        testCaseAsync "IBinaryServer.guid" <|
            async {
                let input = Guid.NewGuid ()
                let! output = binaryServer.echoGuid input

                test.equal true (input = output)
            }

        testCaseAsync "IBinaryServer.largeRecursiveRecord" <|
            async {
                let input = largeRecursiveRecord
                let! output = binaryServer.echoRecursiveRecord input

                test.equal true (input = output)
            }

        testCaseAsync "IBinaryServer.echoIntOptionOption" <|
            async {
                let input = Some (Some 55)
                let! output = binaryServer.echoIntOptionOption input

                test.equal true (input = output)
            }

        testCaseAsync "IBinaryServer.echoMaybeBoolList" <|
            async {
                let input = [ Just true; Nothing ]
                let! output = binaryServer.echoMaybeBoolList input

                test.equal true (input = output)
            }

        testCaseAsync "IBinaryServer.array3tuples" <|
            async {
                let input = [| (1L, ":)", DateTime.Now); (4L, ":<", DateTime.Now) |]
                let! output = binaryServer.echoArray3tuples input

                test.equal true (input = output)
            }

#if NAGAREYAMA
        testCaseAsync "IBinaryServer.echoDateOnlyMap" <|
            async {
                let input = [ (DateOnly.MinValue, DateOnly.MaxValue); (DateOnly.FromDayNumber 1000, DateOnly.FromDateTime DateTime.Now) ] |> Map.ofList
                let! output = binaryServer.echoDateOnlyMap input

                test.equal output input
            }

        testCaseAsync "IBinaryServer.echoTimeOnlyMap" <|
            async {
                let input = [ (TimeOnly.MinValue, TimeOnly.MaxValue); (TimeOnly (10, 20, 30, 400), TimeOnly.FromDateTime DateTime.Now) ] |> Map.ofList
                let! output = binaryServer.echoTimeOnlyMap input

                test.equal output input
            }
#endif

        testCaseAsync "IBinaryServer.pureTask" <|
            async {
                let! output = binaryServer.pureTask
                test.equal 42 output
            }

        testCaseAsync "IBinaryServer.echoMapTask" <|
            async {
                let input = ["hello", 1] |> Map.ofList
                let! output = binaryServer.echoMapTask input
                match input = output with
                | true -> test.pass()
                | false -> test.fail()
            }
    ]

let cookieServer =
    Remoting.createApi()
    |> Remoting.withRouteBuilder routeBuilder
    |> Remoting.buildProxy<ICookieServer>

[<Emit("document.cookie")>]
let currentDocumentCookie : string = jsNative

let cookieServerTests =
  testList "Cookie Server" [
    testCaseAsync "ICookieServer.checkCookie" <|
        async {
            // Cookie not set yet
            let! firstCall = cookieServer.checkCookie ()
            Expect.equal false firstCall "First call should return false"

            // Cookie should now be set and sent back to server
            let! secondCall = cookieServer.checkCookie ()
            Expect.equal true secondCall "Second call should return true"

            // Cookie should not be visible to javascript (HttpOnly)
            let notInJs = not (currentDocumentCookie.Contains("httpOnly-test-cookie"))
            Expect.equal true notInJs "Cookie not visible to the document"
        }
  ]

let resolveAccessToken n =
    async {
        let request = Http.get (sprintf "/IAuthServer/token/%d" n)
        let! response = Http.send request
        return response.ResponseBody
    }

let createSecureApi (accessToken: string) =
    Remoting.createApi()
    |> Remoting.withRouteBuilder routeBuilder
    |> Remoting.withAuthorizationHeader accessToken
    |> Remoting.buildProxy<IAuthServer>

let authorizedServer n f =
    async {
        let! accessToken = resolveAccessToken n
        let authServer = createSecureApi accessToken
        return! f authServer
    }

let secureApiTests =
    testList "Secure API Tests" [
        testCaseAsync "IAuthServer can be used by resolving access tokens" <|
            async {
                let! firstResponse = authorizedServer 1 (fun api -> api.getSecureValue())
                let! secondResponse = authorizedServer 2 (fun api -> api.getSecureValue())
                test.equal firstResponse 1
                test.equal secondResponse 2
            }
    ]


let inline serializeDeserializeCompare typ (value: 'a) =
    let ra = FSharp.Collections.ResizeArray<byte> ()
    Fable.Remoting.MsgPack.Write.Fable.writeObject value typ ra
    let deserialized = Fable.Remoting.MsgPack.Read.Reader(ra.ToArray ()).Read typ :?> 'a
    Expect.equal value deserialized "Values are equal after roundtrip"

let inline serializeDeserialize typ (value: 'a) =
    let ra = FSharp.Collections.ResizeArray<byte> ()
    Fable.Remoting.MsgPack.Write.Fable.writeObject value typ ra
    Fable.Remoting.MsgPack.Read.Reader(ra.ToArray ()).Read typ :?> 'a

let inline serializeDeserializeCompareDictionary typ (value: System.Collections.Generic.IDictionary<'a, 'b>) =
    let ra = FSharp.Collections.ResizeArray<byte> ()
    Fable.Remoting.MsgPack.Write.Fable.writeObject value typ ra

    let deserialized = Fable.Remoting.MsgPack.Read.Reader(ra.ToArray ()).Read typ :?> System.Collections.Generic.IDictionary<'a, 'b>

    for key in value.Keys do
        test.equal value.[key] deserialized.[key]

let inline serializeDeserializeCompareWithLength expectedLength typ (value: 'a) =
    let ra = FSharp.Collections.ResizeArray<byte> ()
    Fable.Remoting.MsgPack.Write.Fable.writeObject value typ ra

    let deserialized = Fable.Remoting.MsgPack.Read.Reader(ra.ToArray ()).Read typ :?> 'a

    Expect.equal value deserialized "Values are equal after roundtrip"
    Expect.equal ra.Count expectedLength "Written and read bytes are the same"

let msgPackTests =
    testList "Message Pack serialization tests" [
        testCase "Dictionary with type as key works" <| fun _ ->
            let cache = Dictionary<Type, int>()
            cache.Add(typeof<int>, 1)
            cache.Add(typeof<string>, 2)
            cache.Add(typeof<int64>, 3)

            Expect.equal 3 cache.Count "There are three elements in the dictionary"
            Expect.equal cache.[typeof<int>] 1 "Indexing int types works with dictionary"
            Expect.equal cache.[typeof<string>] 2 "Indexing string types works with dictionary"
            Expect.equal cache.[typeof<int64>] 3 "Indexing int64 types works with dictionary"

        testCase "Unit" <| fun () ->
            () |> serializeDeserializeCompare typeof<unit>

        testCase "Fixed negative number, single byte" <| fun () ->
            -20 |> serializeDeserializeCompareWithLength 1 typeof<int>
        testCase "Maybe" <| fun () ->
            Just 1 |> serializeDeserializeCompare typeof<Maybe<int>>
        testCase "Nested maybe array works" <| fun () ->
            Just [| Nothing; Just 1 |] |> serializeDeserializeCompare typeof<Maybe<Maybe<int>[]>>
        testCase "Record" <| fun () ->
            { Prop1 = ""; Prop2 = 2; Prop3 = Some 3 } |> serializeDeserializeCompare typeof<Record>
        testCase "None" <| fun () ->
            None |> serializeDeserializeCompare typeof<obj option>
        testCase "Some string works" <| fun () ->
            Some "ddd" |> serializeDeserializeCompare typeof<string option>
        testCase "Long serialized as fixnum" <| fun () ->
            20L |> serializeDeserializeCompare typeof<int64>

        testCase "Long serialized as int16, 3 bytes" <| fun () ->
            60_000L |> serializeDeserializeCompareWithLength 3 typeof<int64>
        testCase "uint64, 9 bytes" <| fun () ->
            637588453436987750UL |> serializeDeserializeCompareWithLength 9 typeof<uint64>
        testCase "int64, 9 bytes" <| fun () ->
            -137588453400987759L |> serializeDeserializeCompareWithLength 9 typeof<int64>

        testCase "Array of 3 bools, 4 bytes" <| fun () ->
            [| false; true; true |] |> serializeDeserializeCompareWithLength 4 typeof<bool[]>

        testCase "List of fixnums, 5 bytes" <| fun () ->
            [ 0; 2; 100; 10 ] |> serializeDeserializeCompareWithLength 5 typeof<int list>

        testCase "DateTime" <| fun () ->
            DateTime.Now |> serializeDeserializeCompare typeof<DateTime>

        testCase "DateTime conversions preverses Kind" <| fun () ->
            let nowTicks = DateTime.Now.Ticks
            let localNow = DateTime(nowTicks, DateTimeKind.Local)
            let utcNow = DateTime(nowTicks, DateTimeKind.Utc)
            let unspecifiedNow = DateTime(nowTicks, DateTimeKind.Unspecified)

            let localNowDeserialized = serializeDeserialize typeof<DateTime> localNow
            let utcNowDeserialized = serializeDeserialize typeof<DateTime> utcNow
            let unspecifiedNowDeserialized = serializeDeserialize typeof<DateTime> unspecifiedNow

            Expect.equal DateTimeKind.Local localNowDeserialized.Kind "Local is preserved"
            Expect.equal DateTimeKind.Utc utcNowDeserialized.Kind "Utc is preserved"
            Expect.equal DateTimeKind.Unspecified unspecifiedNowDeserialized.Kind "Unspecified is preserved"

            Expect.equal localNow localNowDeserialized "Now(Local) can be converted"
            Expect.equal utcNow utcNowDeserialized "Now(Utc) can be converted"
            Expect.equal unspecifiedNow unspecifiedNowDeserialized "Now(Unspecified) can be converted"

        testCase "DateTimeOffset" <| fun () ->
            DateTimeOffset.Now |> serializeDeserializeCompare typeof<DateTimeOffset>

        testCase "String16 with non-ASCII characters" <| fun () ->
            "δασςεφЯШзЖ888dsadčšřποιθθψζψ" |> serializeDeserializeCompare typeof<string>

        testCase "Fixstr with non-ASCII characters" <| fun () ->
            "δ" |> serializeDeserializeCompare typeof<string>

        testCase "String32 with non-ASCII characters" <| fun () ->
            String.init 70_000 (fun _ -> "ΰ") |> serializeDeserializeCompare typeof<string>

        testCase "Negative long" <| fun () ->
            -5889845622625456789L |> serializeDeserializeCompare typeof<int64>

        testCase "Decimal" <| fun () ->
            32313213121.1415926535m |> serializeDeserializeCompare typeof<decimal>

        testCase "Map16 with map" <| fun () ->
            Map.ofArray [| for i in 1 .. 295 -> i, (i * i) |] |> serializeDeserializeCompare typeof<Map<int, int>>

        testCase "Fixmap with dictionary of nothing" <| fun () ->
            Map.ofArray [| for i in 1 .. 2 -> i, Nothing |] |> Dictionary<_, _> |> serializeDeserializeCompareDictionary typeof<Dictionary<int, Maybe<obj>>>

        testCase "Map32 with dictionary" <| fun () ->
            Map.ofArray [| for i in 1 .. 80_000 -> i, i |] |> Dictionary<_, _> |> serializeDeserializeCompareDictionary typeof<Dictionary<int, int>>

        testCase "Generic map" <| fun () ->
            Map.ofList [ "firstKey", Just 5; "secondKey", Nothing ] |> serializeDeserializeCompare typeof<Map<string, Maybe<int>>>
            Map.ofList [ 5000, Just 5; 1, Nothing ] |> serializeDeserializeCompare typeof<Map<int, Maybe<int>>>

        testCase "Set16" <| fun () ->
            Set.ofArray [| for i in 1 .. 295 -> i |] |> serializeDeserializeCompare typeof<Set<int>>

        testCase "Generating large sets works" <| fun _ ->
            let largeSet = Set.ofArray [| for i in 1 .. 80_000 -> i |]
            Expect.equal largeSet.Count 80_000 "Checking set count works"

        testCase "Comparing large sets works" <| fun _ ->
            let largeSetA = Set.ofArray [| for i in 1 .. 80_000 -> i |]
            let largeSetB = Set.ofArray [| for i in 1 .. 80_000 -> i |]

            Expect.equal largeSetA largeSetB "The sets are equal"

        testCase "Set32" <| fun () ->
            Set.ofArray [| for i in 1 .. 80_000 -> i |] |> serializeDeserializeCompare typeof<Set<int>>

        testCase "Recursive set" <| fun () ->
            Set.ofList [
                { Name = "root"; Children = [ { Name = "Grandchild"; Children = [ { Name = "root"; Children = [ { Name = "Grandchild2"; Children = [ ] } ] } ] } ] }
                { Name = "root"; Children = [ { Name = "Grandchild2"; Children = [ ] } ] }
            ] |> serializeDeserializeCompare typeof<Set<RecursiveRecord>>
        testCase "Binary data bin8, 5 bytes" <| fun () ->
            [| 55uy; 0uy; 255uy |] |> serializeDeserializeCompareWithLength 5 typeof<byte[]>
        testCase "Binary data bin16, 303 bytes" <| fun () ->
            [| for _ in 1 .. 300 -> 55uy |] |> serializeDeserializeCompareWithLength 303 typeof<byte[]>
        testCase "Binary data bin32, 80005 bytes" <| fun () ->
            [| for _ in 1 .. 80_000 -> 23uy |] |> serializeDeserializeCompareWithLength 80_005 typeof<byte[]>
        testCase "Array32 of long" <| fun () ->
            [| for _ in 1 .. 80_000 -> 5_000_000_000L |] |> serializeDeserializeCompare typeof<int64[]>
        testCase "Array32 of int32" <| fun () ->
            [| -100000 .. 100000 |] |> serializeDeserializeCompare typeof<int[]>
        testCase "Array32 of uint32" <| fun () ->
            [| 0u .. 200000u |] |> serializeDeserializeCompare typeof<uint32[]>
        testCase "Array of single" <| fun () ->
            [| Single.Epsilon; Single.MaxValue; Single.MinValue; Single.PositiveInfinity; Single.NegativeInfinity |] |> serializeDeserializeCompare typeof<float32[]>
            [| -3f .. 0.5f .. 3f |] |> serializeDeserializeCompare typeof<float32[]>
        testCase "Array of double" <| fun () ->
            [| Double.Epsilon; Double.MaxValue; Double.MinValue; Double.PositiveInfinity; Double.NegativeInfinity |] |> serializeDeserializeCompare typeof<float[]>
            [| -3_000. .. 0.1 .. 3_000. |] |> serializeDeserializeCompare typeof<float[]>
        testCase "Recursive record" <| fun () ->
            {
                Name = "root"
                Children = [
                    { Name = "Child 1"; Children = [ { Name = "Grandchild"; Children = [ ] } ] }
                    { Name = "Child 1"; Children = [ ] }
                ]
            }
            |> serializeDeserializeCompare typeof<RecursiveRecord>
        testCase "Complex tuple" <| fun () ->
            ((String50.Create "as", Some ()), [ 0; 0; 25 ], { Name = ":^)"; Children = [] }) |> serializeDeserializeCompare typeof<(String50 * unit option) * int list * RecursiveRecord>
        testCase "Bigint" <| fun () ->
            -2I |> serializeDeserializeCompare typeof<System.Numerics.BigInteger>
            12345678912345678912345678912345679123I |> serializeDeserializeCompare typeof<System.Numerics.BigInteger>
        testCase "TimeSpan" <| fun () ->
            TimeSpan.FromMilliseconds 0. |> serializeDeserializeCompare typeof<TimeSpan>
            TimeSpan.FromDays 33. |> serializeDeserializeCompare typeof<TimeSpan>
        testCase "Enum" <| fun () ->
            SomeEnum.Val1 |> serializeDeserializeCompareWithLength 1 typeof<SomeEnum>
        testCase "Guid" <| fun () ->
            Guid.NewGuid () |> serializeDeserializeCompareWithLength 18 typeof<Guid>
        testCase "Results" <| fun () ->
            Ok 15 |> serializeDeserializeCompare typeof<Result<int, string>>
            Error "yup" |> serializeDeserializeCompare typeof<Result<int, string>>
        testCase "Units of measure" <| fun () ->
            85<SomeUnit> |> serializeDeserializeCompareWithLength 1 typeof<int<SomeUnit>>
            85L<SomeUnit> |> serializeDeserializeCompareWithLength 1 typeof<int64<SomeUnit>>
            -85L<SomeUnit> |> serializeDeserializeCompareWithLength 9 typeof<int64<SomeUnit>>
            32313213121.1415926535m<SomeUnit> |> serializeDeserializeCompareWithLength 18 typeof<decimal<SomeUnit>>
            80000005.44f<SomeUnit> |> serializeDeserializeCompareWithLength 5 typeof<float32<SomeUnit>>
            80000000000005.445454<SomeUnit> |> serializeDeserializeCompareWithLength 9 typeof<float<SomeUnit>>
        testCase "Value option" <| fun () ->
            ValueSome "blah" |> serializeDeserializeCompare typeof<string voption>
            ValueNone |> serializeDeserializeCompare typeof<string voption>
        testCase "Union cases with no parameters" <| fun () ->
            One |> serializeDeserializeCompare typeof<UnionType>
            Two |> serializeDeserializeCompare typeof<UnionType>
        testCase "Option of option" <| fun () ->
            Some (Some 5) |> serializeDeserializeCompare typeof<int option option>
            Some None |> serializeDeserializeCompare typeof<int option option>
            None |> serializeDeserializeCompare typeof<int option option>
        testCase "List of unions" <| fun () ->
            [ Just 4; Nothing ] |> serializeDeserializeCompare typeof<Maybe<int> list>
            [ Just 4; Nothing ] |> serializeDeserializeCompare typeof<Maybe<int> list>
        testCase "Array of 3-tuples" <| fun () ->
            [| (1L, ":)", DateTime.Now); (4L, ":<", DateTime.Now) |] |> serializeDeserializeCompare typeof<(int64 * string * DateTime)[]>
        testCase "Chars" <| fun () ->
            'q' |> serializeDeserializeCompare typeof<char>
            'ψ' |> serializeDeserializeCompare typeof<char>
            '☃' |> serializeDeserializeCompare typeof<char>
            "☠️".[0] |> serializeDeserializeCompare typeof<char>
        testCase "Bytes" <| fun () ->
            0uy |> serializeDeserializeCompare typeof<byte>
            0y |> serializeDeserializeCompare typeof<sbyte>
            255uy |> serializeDeserializeCompare typeof<byte>
            100y |> serializeDeserializeCompare typeof<sbyte>
            -100y |> serializeDeserializeCompare typeof<sbyte>
            -5y |> serializeDeserializeCompare typeof<sbyte>
            [| 0uy; 255uy; 100uy; 5uy |] |> serializeDeserializeCompare typeof<byte[]>
            [| 0y; 100y; -100y; -5y |] |> serializeDeserializeCompare typeof<sbyte[]>

#if NAGAREYAMA
        testCase "DateOnlyMap" <| fun () ->
            [ (DateOnly.MinValue, DateOnly.MaxValue); (DateOnly.FromDayNumber 1000, DateOnly.FromDateTime DateTime.Now) ]
            |> Map.ofList
            |> serializeDeserializeCompareDictionary typeof<Map<DateOnly, DateOnly>>
        testCase "TimeOnlyMap" <| fun () ->
            [ (TimeOnly.MinValue, TimeOnly.MaxValue); (TimeOnly (10, 20, 30, 400), TimeOnly.FromDateTime DateTime.Now) ]
            |> Map.ofList
            |> serializeDeserializeCompareDictionary typeof<Map<TimeOnly, TimeOnly>>
#endif
    ]

let alltests =
    testList "All Tests" [
        serverTests
        binaryServerTests
        cookieServerTests
        secureApiTests
        msgPackTests
    ]

Mocha.runTests alltests
|> ignore