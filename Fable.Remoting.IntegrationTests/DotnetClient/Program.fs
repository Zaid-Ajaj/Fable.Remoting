﻿module DotnetClientTests

open System
open Fable.Remoting.DotnetClient
open Fable.Remoting.Giraffe
open Fable.Remoting.Server
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open ServerImpl
open SharedTypes
open System.Threading
open Giraffe
open Microsoft.IO
open Expecto

module ServerParts =
    let fableWebPart =
        Remoting.createApi()
        |> Remoting.fromValue server
        |> Remoting.withRouteBuilder routeBuilder
        |> Remoting.withErrorHandler (fun ex routeInfo -> Propagate (sprintf "Message: %s, request body: %A" ex.Message routeInfo.requestBodyText))
        |> Remoting.withRecyclableMemoryStreamManager (RecyclableMemoryStreamManager (RecyclableMemoryStreamManager.Options (ThrowExceptionOnToArray = true)))
        |> Remoting.buildHttpHandler

    let cts = new CancellationTokenSource()

    let configureApp (app: IApplicationBuilder) =
        app
            .UseDefaultFiles()
            .UseStaticFiles()
            .UseGiraffe fableWebPart

    let start () =
        WebHostBuilder()
            .Configure(configureApp)
            .UseKestrel()
            .UseUrls("http://localhost:9090")
            .Build()
            .RunAsync cts.Token

let _shutdownTask = ServerParts.start ()

printfn "Web server started"
printfn "Getting server ready to listen for reqeusts"

module ClientParts =
    open Fable.Remoting.DotnetClient

    let proxy = Proxy.create<IServer> (sprintf "http://localhost:9090/api/%s/%s")

    let server =
        Remoting.createApi "http://localhost:9090"
        |> Remoting.withRouteBuilder routeBuilder
        |> Remoting.withMultipartOptimization
        |> Remoting.buildProxy<IServer>

open ClientParts

let dotnetClientTests =
    testList "Dotnet Client tests" [

        testCaseAsync "IServer.getLength" <| async {
            let! result =  proxy.call <@ fun server -> server.getLength "hello" @>
            Expect.equal 5 result "Length returned is correct"
        }

        testCaseAsync "IServer.getLength with proxy" <| async {
            let! result =  server.getLength "hello"
            Expect.equal 5 result "Length returned is correct"
        }

        testCaseAsync "IServer.getLength expression from outside" <| async {
            let value = "value from outside"
            let! result =  proxy.call <@ fun server -> server.getLength value @>
            Expect.equal 18 result "Length returned is correct"
        }

        testCaseAsync "IServer.echoInteger" <| async {
            let! firstResult = proxy.call <@ fun server -> server.echoInteger 20 @>
            let! secondResult = proxy.call <@ fun server -> server.echoInteger 0 @>
            Expect.equal 20 firstResult "result is echoed correctly"
            Expect.equal 0 secondResult "result is echoed correctly"
        }

        testCaseAsync "IServer.echoInteger with proxy" <| async {
            let! firstResult = server.echoInteger 20
            let! secondResult = server.echoInteger 0
            Expect.equal 20 firstResult "result is echoed correctly"
            Expect.equal 0 secondResult "result is echoed correctly"
        }

        testCaseAsync "IServer.simpleUnit" <| async {
            let! result =  proxy.call <@ fun server -> server.simpleUnit () @>
            Expect.equal 42 result "result is correct"
        }

        testCaseAsync "IServer.simpleUnit with proxy" <| async {
            let! result = server.simpleUnit()
            Expect.equal 42 result "result is correct"
        }

        testCaseAsync "IServer.echoBool" <| async {
            let! one = proxy.call <@ fun server -> server.echoBool true @>
            let! two = proxy.call <@ fun server -> server.echoBool false  @>
            Expect.equal one true "Bool result is correct"
            Expect.equal two false "Bool result is correct"
        }

        testCaseAsync "IServer.echoBool with proxy" <| async {
            let! one = server.echoBool true
            let! two = server.echoBool false
            Expect.equal one true "Bool result is correct"
            Expect.equal two false "Bool result is correct"
        }

        testCaseAsync "IServer.echoIntOption" <| async {
            let! one =  proxy.call <@ fun server -> server.echoIntOption (Some 20) @>
            let! two =  proxy.call <@ fun server -> server.echoIntOption None @>

            Expect.equal one (Some 20) "Option<int> returned is correct"
            Expect.equal two None "Option<int> returned is correct"
        }

        testCaseAsync "IServer.echoIntOption with proxy" <| async {
            let! one =  server.echoIntOption (Some 20)
            let! two =  server.echoIntOption None

            Expect.equal one (Some 20) "Option<int> returned is correct"
            Expect.equal two None "Option<int> returned is correct"
        }

        testCaseAsync "IServer.echoIntOption from outside" <| async {
            let first = Some 20
            let second : Option<int> = None
            let! one =  proxy.call <@ fun server -> server.echoIntOption first @>
            let! two =  proxy.call <@ fun server -> server.echoIntOption second @>

            Expect.equal one (Some 20) "Option<int> returned is correct"
            Expect.equal two None "Option<int> returned is correct"
        }

        testCaseAsync "IServer.echoStringOption" <| async {
            let! one = proxy.call <@ fun server -> server.echoStringOption (Some "value") @>
            let! two = proxy.call <@ fun server -> server.echoStringOption None @>
            Expect.equal one (Some "value") "Option<string> returned is correct"
            Expect.equal two None "Option<string> returned is correct"
        }

        testCaseAsync "IServer.echoStringOption with proxy" <| async {
            let! one = server.echoStringOption (Some "value")
            let! two = server.echoStringOption None
            Expect.equal one (Some "value") "Option<string> returned is correct"
            Expect.equal two None "Option<string> returned is correct"
        }

        testCaseAsync "IServer.echoStringOption from outside" <| async {
            let first = Some "value"
            let second : Option<string> = None
            let! one = proxy.call <@ fun server -> server.echoStringOption first @>
            let! two = proxy.call <@ fun server -> server.echoStringOption second @>
            Expect.equal one (Some "value") "Option<string> returned is correct"
            Expect.equal two None "Option<string> returned is correct"
        }

        testCaseAsync "IServer.echoSimpleUnionType" <| async {
            let! result1 = proxy.call <@ fun server -> server.echoSimpleUnionType One @>
            let! result2 = proxy.call <@ fun server -> server.echoSimpleUnionType Two @>
            Expect.equal true (result1 = One) "SimpleUnion returned is correct"
            Expect.equal true (result2 = Two) "SimpleUnion returned is correct"
        }

        testCaseAsync "IServer.echoSimpleUnionType with proxy" <| async {
            let! result1 = server.echoSimpleUnionType One
            let! result2 = server.echoSimpleUnionType Two
            Expect.equal true (result1 = One) "SimpleUnion returned is correct"
            Expect.equal true (result2 = Two) "SimpleUnion returned is correct"
        }

        testCaseAsync "IServer.echoSimpleUnionType from outside" <| async {
            let first = One
            let second = Two
            let! result1 = proxy.call <@ fun server -> server.echoSimpleUnionType first @>
            let! result2 = proxy.call <@ fun server -> server.echoSimpleUnionType second @>
            Expect.equal true (result1 = One) "SimpleUnion returned is correct"
            Expect.equal true (result2 = Two) "SimpleUnion returned is correct"
        }

        testCaseAsync "IServer.echoGenericUnionInt" <| async {
            let! result1 = proxy.call <@ fun server -> server.echoGenericUnionInt (Just 5) @>
            let! result2 = proxy.call <@ fun server -> server.echoGenericUnionInt (Just 10) @>
            let! result3 = proxy.call <@ fun server -> server.echoGenericUnionInt Nothing @>

            Expect.equal true (result1 = Just 5) "GenericUnionInt returned is correct"
            Expect.equal true (result2 = Just 10) "GenericUnionInt returned is correct"
            Expect.equal true (result3 = Nothing) "GenericUnionInt returned is correct"
        }

        testCaseAsync "IServer.echoGenericUnionInt with proxy" <| async {
            let! result1 = server.echoGenericUnionInt (Just 5)
            let! result2 = server.echoGenericUnionInt (Just 10)
            let! result3 = server.echoGenericUnionInt Nothing

            Expect.equal true (result1 = Just 5) "GenericUnionInt returned is correct"
            Expect.equal true (result2 = Just 10) "GenericUnionInt returned is correct"
            Expect.equal true (result3 = Nothing) "GenericUnionInt returned is correct"
        }

        testCaseAsync "IServer.echoGenericUnionString" <| async {
            let! result1 = proxy.call <@ fun server -> server.echoGenericUnionString (Just "") @>
            let! result2 = proxy.call <@ fun server -> server.echoGenericUnionString (Just null) @>
            let! result3 = proxy.call <@ fun server -> server.echoGenericUnionString Nothing @>

            Expect.equal true (result1 = Just "") "GenericUnionString returned is correct"
            Expect.equal true (result2 = Just null) "GenericUnionString returned is correct"
            Expect.equal true (result3 = Nothing) "GenericUnionString returned is correct"
        }

        testCaseAsync "IServer.echoGenericUnionString with proxy" <| async {
            let! result1 = server.echoGenericUnionString (Just "")
            let! result2 = server.echoGenericUnionString (Just null)
            let! result3 = server.echoGenericUnionString Nothing

            Expect.equal true (result1 = Just "") "GenericUnionString returned is correct"
            Expect.equal true (result2 = Just null) "GenericUnionString returned is correct"
            Expect.equal true (result3 = Nothing) "GenericUnionString returned is correct"
        }

        testCaseAsync "IServer.echoRecord" <| async {
            let record1 = { Prop1 = "hello"; Prop2 = 10; Prop3 = None }
            let record2 = { Prop1 = ""; Prop2 = 20; Prop3 = Some 10 }
            let record3 = { Prop1 = null; Prop2 = 30; Prop3 = Some 20  }
            let! result1 = proxy.call <@ fun server -> server.echoRecord record1 @>
            let! result2 = proxy.call <@ fun server -> server.echoRecord record2 @>
            let! result3 = proxy.call <@ fun server -> server.echoRecord record3 @>

            Expect.equal true (result1 = record1) "Record returned is correct"
            Expect.equal true (result2 = record2) "Record returned is correct"
            Expect.equal true (result3 = record3) "Record returned is correct"
        }

        testCaseAsync "IServer.echoRecord with proxy" <| async {
            let record1 = { Prop1 = "hello"; Prop2 = 10; Prop3 = None }
            let record2 = { Prop1 = ""; Prop2 = 20; Prop3 = Some 10 }
            let record3 = { Prop1 = null; Prop2 = 30; Prop3 = Some 20  }
            let! result1 = server.echoRecord record1
            let! result2 = server.echoRecord record2
            let! result3 = server.echoRecord record3

            Expect.equal true (result1 = record1) "Record returned is correct"
            Expect.equal true (result2 = record2) "Record returned is correct"
            Expect.equal true (result3 = record3) "Record returned is correct"
        }

        testCaseAsync "IServer.echoNestedGeneric from outside" <| async {
            let input : GenericRecord<Maybe<int option>> = {
                Value = Just (Some 5)
                OtherValue = 2
            }

            let input2 : GenericRecord<Maybe<int option>> = {
                Value = Just (None)
                OtherValue = 2
            }

            let! result1 = proxy.call <@ fun server -> server.echoNestedGeneric input @>
            let! result2 = proxy.call <@ fun server -> server.echoNestedGeneric input2 @>
            Expect.equal true (input = result1) "Nested generic record is correct"
            Expect.equal true (input2 = result2) "Nested generic record is correct"
        }

        // Inline values cannot always be compiled, so define first and reference from inside the quotation expression
        testCaseAsync "IServer.echoNestedGeneric inline in expression" <| async {
            let! result1 = proxy.call <@ fun server -> server.echoNestedGeneric { Value = Just (Some 5); OtherValue = 2 }  @>
            let! result2 = proxy.call <@ fun server -> server.echoNestedGeneric { Value = Just (None); OtherValue = 2 } @>
            Expect.equal true ({ Value = Just (Some 5); OtherValue = 2 } = result1) "Nested generic record is correct"
            Expect.equal true ({ Value = Just (None); OtherValue = 2 } = result2) "Nested generic record is correct"
        }

        testCaseAsync "IServer.echoNestedGeneric inline in expression with expression" <| async {
            let! result1 = server.echoNestedGeneric { Value = Just (Some 5); OtherValue = 2 }
            let! result2 = server.echoNestedGeneric { Value = Just (None); OtherValue = 2 }
            Expect.equal true ({ Value = Just (Some 5); OtherValue = 2 } = result1) "Nested generic record is correct"
            Expect.equal true ({ Value = Just (None); OtherValue = 2 } = result2) "Nested generic record is correct"
        }

        testCaseAsync "IServer.echoOtherDataC" <| async {
            let input = {
                Byte = 200uy
                SByte = -10y
                Maybes = [ Just -120y; Nothing; Just 120y; Just 5y; Just -5y ]
            }

            let! result = server.echoOtherDataC input
            Expect.equal true (input = result) "OtherDataC is correct"
        }

        testCaseAsync "IServer.echoIntList" <| async {
            let inputList = [1 .. 5]
            let! output = proxy.call <@ fun server -> server.echoIntList inputList @>
            Expect.equal output [1;2;3;4;5] "The echoed list is correct"
            let emptyList : int list = [ ]
            let! echoedList = proxy.call <@ fun server -> server.echoIntList emptyList @>
            Expect.equal true (List.isEmpty echoedList) "The echoed list is correct"
        }

        testCaseAsync "IServer.echoIntList with proxy" <| async {
            let inputList = [1 .. 5]
            let! output =  server.echoIntList inputList
            Expect.equal output [1;2;3;4;5] "The echoed list is correct"
            let emptyList : int list = [ ]
            let! echoedList = server.echoIntList emptyList
            Expect.equal true (List.isEmpty echoedList) "The echoed list is correct"
        }

        testCaseAsync "IServer.echoSingleCase" <| async {
            let! output = proxy.call <@ fun server -> server.echoSingleCase (SingleCase 10) @>
            Expect.equal output (SingleCase 10) "Single case union roundtrip works"
        }

        testCaseAsync "IServer.echoSingleCase with proxy" <| async {
            let! output = server.echoSingleCase (SingleCase 10)
            Expect.equal output (SingleCase 10) "Single case union roundtrip works"
        }

        testCaseAsync "IServer.echoStringList" <| async {
            let input = ["one"; "two"; null]
            let! output = proxy.call <@ fun server -> server.echoStringList input @>
            Expect.equal input output "Echoed list is correct"
            let emptyList : string list = []
            let! echoedList = proxy.call <@ fun server -> server.echoStringList emptyList @>
            Expect.equal true (List.isEmpty echoedList) "Echoed list is empty"
        }

        testCaseAsync "IServer.echoStringList with proxy" <| async {
            let input = ["one"; "two"; null]
            let! output = server.echoStringList input
            Expect.equal input output "Echoed list is correct"
            let emptyList : string list = []
            let! echoedList = server.echoStringList emptyList
            Expect.equal true (List.isEmpty echoedList) "Echoed list is empty"
        }

        testCaseAsync "IServer.echoBoolList" <| async {
            let input = [true; false; true]
            let! output = proxy.call <@ fun server -> server.echoBoolList input @>
            Expect.equal output input "Echoed list is correct"
            let emptyList : bool list = []
            let! echoedList = proxy.call <@ fun server -> server.echoBoolList emptyList @>
            Expect.equal true (List.isEmpty echoedList) "Echoed list is empty"
        }

        testCaseAsync "IServer.echoBoolList with proxy" <| async {
            let input = [true; false; true]
            let! output = server.echoBoolList input
            Expect.equal output input "Echoed list is correct"
            let emptyList : bool list = []
            let! echoedList = server.echoBoolList emptyList
            Expect.equal true (List.isEmpty echoedList) "Echoed list is empty"
        }

        testCaseAsync "IServer.echoListOfListsOfStrings" <| async {
            let input = [["1"; "2"]; ["3"; "4";"5"]]
            let! output = proxy.call <@ fun server -> server.echoListOfListsOfStrings input @>
            Expect.equal input output "Echoed list is correct"
        }

        testCaseAsync "IServer.echoListOfListsOfStrings with proxy" <| async {
            let input = [["1"; "2"]; ["3"; "4";"5"]]
            let! output = server.echoListOfListsOfStrings input
            Expect.equal input output "Echoed list is correct"
        }

        testCaseAsync "IServer.echoResult for Result<int, string>" <| async {
            let! output = proxy.call <@ fun server -> server.echoResult (Ok 15) @>
            Expect.equal output (Ok 15) "Result is correct"

            let! output = proxy.call <@ fun server -> server.echoResult (Result.Error "somewhere here") @>
            Expect.equal output (Result.Error "somewhere here")  "Result is correct"
        }

        testCaseAsync "IServer.echoResult for Result<int, string> with proxy" <| async {
            let! output = server.echoResult (Ok 15)
            Expect.equal output (Ok 15) "Result is correct"

            let! output = server.echoResult (Result.Error "somewhere here")
            Expect.equal output (Result.Error "somewhere here") "Result is correct"
        }

        testCaseAsync "IServer.echoMap" <| async {
            let input = ["hello", 1] |> Map.ofList
            let! output = proxy.call <@ fun server -> server.echoMap input @>
            Expect.equal input output "Map is echoed correctly"
        }

        testCaseAsync "IServer.echoMap with proxy" <| async {
            let input = ["hello", 1] |> Map.ofList
            let! output = server.echoMap input
            Expect.equal input output "Map is echoed correctly"
        }

        testCaseAsync "IServer.echoSet" <| async {
            let input = ["hello"] |> Set.ofList
            let! output = proxy.call <@ fun server -> server.echoSet input @>
            Expect.equal input output "Set is echoed correctly"
        }

        testCaseAsync "IServer.echoSet with proxy" <| async {
            let input = ["hello"] |> Set.ofList
            let! output = server.echoSet input
            Expect.equal input output "Set is echoed correctly"
        }

        testCaseAsync "IServer.echoRecordWithStringOption with proxy" <| async {
            let input = { StringValue = Some "value" }
            let! output = server.echoRecordWithStringOption input
            Expect.equal input output "Set is echoed correctly"
        }

        testCaseAsync "IServer.throwError using callSafely" <| async {
            let! result = proxy.callSafely <@ fun server -> server.throwError() @>
            match result with
            | Ok value -> failwithf "Got value %A where an error was expected" value
            | Result.Error ex ->
                match ex with
                | :? Http.ProxyRequestException -> Expect.isTrue true "Works"
                | other -> Expect.isTrue false "Should not happen"
        }

        testCaseAsync "IServer.mutliArgFunc" <| async {
            let! result = proxy.call <@ fun server -> server.multiArgFunc "hello" 10 false @>
            Expect.equal 15 result "Result is correct"

            let! sndResult = proxy.call <@ fun server -> server.multiArgFunc "byebye" 5 true @>
            Expect.equal 12 sndResult "Result is correct"
        }

        testCaseAsync "IServer.mutliArgFunc with proxy" <| async {
            let! result = server.multiArgFunc "hello" 10 false
            Expect.equal 15 result "Result is correct"

            let! sndResult = server.multiArgFunc "byebye" 5 true
            Expect.equal 12 sndResult "Result is correct"
        }

        testCaseAsync "IServer.pureAsync" <| async {
            let! result = proxy.call <@ fun server -> server.pureAsync @>
            Expect.equal 42 result "Pure async without parameters works"
        }

        testCaseAsync "IServer.pureAsync with proxy" <| async {
            let! result = server.pureAsync
            Expect.equal 42 result "Pure async without parameters works"
        }

        testCaseAsync "IServer.asyncNestedGeneric" <| async {
            let! result = proxy.call <@ fun server -> server.asyncNestedGeneric @>
            Expect.equal { OtherValue = 10; Value = Just (Some "value") } result "Returned value is correct"
        }

        testCaseAsync "IServer.asyncNestedGeneric with proxy" <| async {
            let! result = server.asyncNestedGeneric
            Expect.equal { OtherValue = 10; Value = Just (Some "value") } result "Returned value is correct"
        }

        testCaseAsync "IServer.echoBigInteger" <| async {
            let input = 1I
            let! output = proxy.call <@ fun server -> server.echoBigInteger input @>
            Expect.equal input output "Big int is equal"
        }

        testCaseAsync "IServer.echoBigInteger with proxy" <| async {
            let input = 1I
            let! output = server.echoBigInteger input
            Expect.equal input output "Big int is equal"
        }

        testCaseAsync "IServer.tuplesAndLists" <| async {
            let inputDict = Map.ofList [ "hello", 5 ]
            let inputStrings = [ "there!" ]
            let! output = proxy.call <@ fun server -> server.tuplesAndLists (inputDict, inputStrings) @>
            let expected = Map.ofList [ "hello", 5; "there!", 6 ]
            Expect.equal output expected "Echoed map is correct"
        }

        testCaseAsync "IServer.tuplesAndLists with proxy" <| async {
            let inputDict = Map.ofList [ "hello", 5 ]
            let inputStrings = [ "there!" ]
            let! output =  server.tuplesAndLists (inputDict, inputStrings)
            let expected = Map.ofList [ "hello", 5; "there!", 6 ]
            Expect.equal output expected "Echoed map is correct"
        }

        testCaseAsync "IServer.command" <| async {
            let label = CommandLabel "Initializing programs"
            let identifier = IWantResponsesOn (ClientId "dDY_ftBDlUWjemgjP6leWw")
            let address = Address 2
            let position = Cartesian({x = 0.0;y = 10.0; z = 6.0; w = 0.0; p = 0.0; r = -90.0}, CartesianConfig "N U T, 0, 0, 0")
            let command = Requests.PositionSet(address, position)
            let! output = proxy.call (fun server -> server.command(label, identifier, command))
            Expect.equal output (Some "Operation error") "Output has gone through"
        }

        testCaseAsync "IServer.command with proxy" <| async {
            let label = CommandLabel "Initializing programs"
            let identifier = IWantResponsesOn (ClientId "dDY_ftBDlUWjemgjP6leWw")
            let address = Address 2
            let position = Cartesian({x = 0.0;y = 10.0; z = 6.0; w = 0.0; p = 0.0; r = -90.0}, CartesianConfig "N U T, 0, 0, 0")
            let command = Requests.PositionSet(address, position)
            let! output = server.command(label, identifier, command)
            Expect.equal output (Some "Operation error") "Output has gone through"
        }

        testCaseAsync "IServer.echoPosition" <| async {
            let position = Cartesian({x = 0.0;y = 10.0; z = 6.0; w = 0.0; p = 0.0; r = -90.0}, CartesianConfig "N U T, 0, 0, 0")
            let! output = proxy.call (fun server -> server.echoPosition(position))
            Expect.equal output position "Output has gone through"
        }

        testCaseAsync "IServer.echoPosition with proxy" <| async {
            let position = Cartesian({x = 0.0;y = 10.0; z = 6.0; w = 0.0; p = 0.0; r = -90.0}, CartesianConfig "N U T, 0, 0, 0")
            let! output = server.echoPosition(position)
            Expect.equal output position "Output has gone through"
        }

        testCaseAsync "IServer.echoDateOnlyMap" <| async {
            let input = [ (DateOnly.MinValue, DateOnly.MaxValue); (DateOnly.FromDayNumber 1000, DateOnly.FromDateTime DateTime.Now) ] |> Map.ofList
            let! output = server.echoDateOnlyMap input
            Expect.equal output input "Output has gone through"
        }

        testCaseAsync "IServer.echoTimeOnlyMap" <| async {
            let input = [ (TimeOnly.MinValue, TimeOnly.MaxValue); (TimeOnly (10, 20, 30, 400), TimeOnly.FromDateTime DateTime.Now) ] |> Map.ofList
            let! output = server.echoTimeOnlyMap input
            Expect.equal output input "Output has gone through"
        }

        testCaseAsync "IServer.pureTask" <| async {
            let! output = server.pureTask |> Async.AwaitTask
            Expect.equal output 42 "Pure task without parameters works"
        }

        testCaseAsync "IServer.echoMapTask" <| async {
            let expected = Map.ofList [ "yup", 6 ]
            let! output = server.echoMapTask expected |> Async.AwaitTask
            Expect.equal output expected "Echoed map is correct"
        }

        testCaseAsync "IServer.getPostTimestamp" <| async {
            let! output = server.getPostTimestamp()
            Expect.equal output staticTimestampText "Timestamp is correct"
        }
        
        testCaseAsync "IServer.getPostTimestamp_Result" <| async {
            let! output = server.getPostTimestamp_Result()
            Expect.equal output (Ok staticTimestampText) "Timestamp is correct"
        }

        testCaseAsync "IServer.multipart" <|
            async {
                let r = System.Random ()

                let score = { Name = "test"; Score = r.Next 100 }
                let bytes1 = Array.init (r.Next 10_000) byte
                let bytes2 = Array.init (r.Next 50_000) byte
                let num = r.Next 666_666 |> int64

                let! output = server.multipart score bytes1 num bytes2
                let expected = int64 score.Score + num + (bytes1 |> Array.sumBy int64) + (bytes2 |> Array.sumBy int64)

                Expect.equal output expected "Result is correct"
            }
    ]

let testConfig =  { Expecto.Tests.defaultConfig with
                        parallelWorkers = 1
                        verbosity = Logging.LogLevel.Debug }

[<EntryPoint>]
let main argv =
    let testResult = runTests testConfig dotnetClientTests
    // quit server
    ServerParts.cts.Cancel()
    testResult