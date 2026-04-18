module FableFalcoAdapterTests

open System
open System.IO
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.TestHost
open Microsoft.AspNetCore.Http


open Expecto
open Types
open System.Net
open Microsoft.IO

let builder = sprintf "/api/%s/%s"

module ServerParts =

    open Falco
    open Microsoft.Extensions.DependencyInjection
    open Fable.Remoting.Server
    open Fable.Remoting.Falco
    let webApp =
        Remoting.createApi()
        |> Remoting.withRouteBuilder builder
        |> Remoting.withErrorHandler (fun ex routeInfo -> Propagate (sprintf "Message: %s, request body: %A" ex.Message routeInfo.requestBodyText))
        |> Remoting.fromValue server
        |> Remoting.buildHttpEndpoints
        

    let webAppBinary =
        Remoting.createApi()
        |> Remoting.withRouteBuilder builder
        |> Remoting.withErrorHandler (fun ex routeInfo -> Propagate (sprintf "Message: %s, request body: %A" ex.Message routeInfo.requestBodyText))
        |> Remoting.withBinarySerialization
        |> Remoting.withRecyclableMemoryStreamManager (RecyclableMemoryStreamManager (RecyclableMemoryStreamManager.Options (ThrowExceptionOnToArray = true)))
        |> Remoting.fromValue binaryServer
        |> Remoting.buildHttpEndpoints

    let otherWebApp =
        Remoting.createApi()
        |> Remoting.withRouteBuilder builder
        |> Remoting.withErrorHandler (fun ex routeInfo -> Propagate (sprintf "Message: %s, request body: %A" ex.Message routeInfo.requestBodyText))
        |> Remoting.fromContext (fun ctx -> implementation)
        |> Remoting.buildHttpEndpoints

    let configureServices (services : IServiceCollection) =
        services.AddRouting() |> ignore
    let configureApp (app : IApplicationBuilder) =
        let endpoints = Seq.collect id [webApp; webAppBinary; otherWebApp]
        app.UseRouting().UseFalco endpoints |> ignore

    let createHost() =
        WebHostBuilder()
            .UseContentRoot(Directory.GetCurrentDirectory())
            .ConfigureServices(Action<IServiceCollection> configureServices)
            .Configure(Action<IApplicationBuilder> configureApp)
            
open ServerParts

let testServer = new TestServer(createHost())
let client = testServer.CreateClient()

module ClientParts =
    open Fable.Remoting.DotnetClient

    // proxies to different API's
    let proxy = Proxy.custom<IServer> builder client false
    let binaryProxy = Proxy.custom<IBinaryServer> builder client true
    let protocolProxy = Proxy.custom<IProtocol> builder client false

open ClientParts

let fableFalcoAdapterTests =
    testList "FableFalcoAdapter tests" [
        
        testCaseAsync "IProtocol.echoGenericUnionInt" <| async {
            let! result = protocolProxy.call(fun server -> server.echoGenericUnionInt (Just 5))
            Expect.equal (Just 5) result "it works"
        }

        testCaseAsync "IProtocol.binaryContent" <| async {
            let! result = protocolProxy.call(fun server -> server.binaryContent())
            Expect.equal [| 1uy; 2uy; 3uy |] result "it works"
        }

        testCaseAsync "IServer.getLength" <| async {
            let! result =  proxy.call(fun server -> server.getLength "hello")
            Expect.equal 5 result "Length returned is correct"
        }

        testCaseAsync "IProtocol.echoIntList" <| async {
            let input =  [1 .. 5]
            let! result = protocolProxy.call(fun server -> server.echoIntList input)
            Expect.equal [1 .. 5] result "it works"
        }

        testCaseAsync "IServer.getLength expression from outside" <| async {
            let value = "value from outside"
            let! result =  proxy.call(fun server -> server.getLength value)
            Expect.equal 18 result "Length returned is correct"
        }

        testCaseAsync "IServer.echoInteger" <| async {
            let! firstResult = proxy.call (fun server -> server.echoInteger 20)
            let! secondResult = proxy.call (fun server -> server.echoInteger 0)
            Expect.equal 20 firstResult "result is echoed correctly"
            Expect.equal 0 secondResult "result is echoed correctly"
        }

        testCaseAsync "IServer.echoInteger with explicit quotes" <| async {
            let! firstResult = proxy.call <@ fun (server: IServer) -> server.echoInteger 20 @>
            let! secondResult = proxy.call <@ fun (server: IServer) -> server.echoInteger 0 @>
            Expect.equal 20 firstResult "result is echoed correctly"
            Expect.equal 0 secondResult "result is echoed correctly"
        }

        testCaseAsync "IServer.simpleUnit" <| async {
            let! result =  proxy.call (fun server -> server.simpleUnit ())
            Expect.equal 42 result "result is correct"
        }

        testCaseAsync "IServer.echoBool" <| async {
            let! one = proxy.call (fun server -> server.echoBool true)
            let! two = proxy.call (fun server -> server.echoBool false)
            Expect.equal one true "Bool result is correct"
            Expect.equal two false "Bool result is correct"
        }

        testCaseAsync "IServer.echoIntOption" <| async {
            let! one =  proxy.call (fun server -> server.echoIntOption (Some 20))
            let! two =  proxy.call (fun server -> server.echoIntOption None)

            Expect.equal one (Some 20) "Option<int> returned is correct"
            Expect.equal two None "Option<int> returned is correct"
        }

        testCaseAsync "IServer.echoIntOption from outside" <| async {
            let first = Some 20
            let second : Option<int> = None
            let! one =  proxy.call (fun server -> server.echoIntOption first)
            let! two =  proxy.call (fun server -> server.echoIntOption second)

            Expect.equal one (Some 20) "Option<int> returned is correct"
            Expect.equal two None "Option<int> returned is correct"
        }

        testCaseAsync "IServer.echoStringOption" <| async {
            let! one = proxy.call (fun server -> server.echoStringOption (Some "value"))
            let! two = proxy.call (fun server -> server.echoStringOption None)
            Expect.equal one (Some "value") "Option<string> returned is correct"
            Expect.equal two None "Option<string> returned is correct"
        }

        testCaseAsync "IServer.echoStringOption from outside" <| async {
            let first = Some "value"
            let second : Option<string> = None
            let! one = proxy.call (fun server -> server.echoStringOption first)
            let! two = proxy.call (fun server -> server.echoStringOption second)
            Expect.equal one (Some "value") "Option<string> returned is correct"
            Expect.equal two None "Option<string> returned is correct"
        }

        testCaseAsync "IServer.echoSimpleUnionType" <| async {
            let! result1 = proxy.call (fun server -> server.echoSimpleUnionType One)
            let! result2 = proxy.call (fun server -> server.echoSimpleUnionType Two)
            Expect.equal true (result1 = One) "SimpleUnion returned is correct"
            Expect.equal true (result2 = Two) "SimpleUnion returned is correct"
        }

        testCaseAsync "IServer.echoSimpleUnionType from outside" <| async {
            let first = One
            let second = Two
            let! result1 = proxy.call (fun server -> server.echoSimpleUnionType first)
            let! result2 = proxy.call (fun server -> server.echoSimpleUnionType second)
            Expect.equal true (result1 = One) "SimpleUnion returned is correct"
            Expect.equal true (result2 = Two) "SimpleUnion returned is correct"
        }

        testCaseAsync "IServer.echoGenericUnionInt" <| async {
            let! result1 = proxy.call (fun server -> server.echoGenericUnionInt (Just 5))
            let! result2 = proxy.call (fun server -> server.echoGenericUnionInt (Just 10))
            let! result3 = proxy.call (fun server -> server.echoGenericUnionInt Nothing)

            Expect.equal true (result1 = Just 5) "GenericUnionInt returned is correct"
            Expect.equal true (result2 = Just 10) "GenericUnionInt returned is correct"
            Expect.equal true (result3 = Nothing) "GenericUnionInt returned is correct"
        }

        testCaseAsync "IServer.echoGenericUnionString" <| async {
            let! result1 = proxy.call (fun server -> server.echoGenericUnionString (Just ""))
            let! result2 = proxy.call (fun server -> server.echoGenericUnionString (Just null))
            let! result3 = proxy.call (fun server -> server.echoGenericUnionString Nothing)

            Expect.equal true (result1 = Just "") "GenericUnionString returned is correct"
            Expect.equal true (result2 = Just null) "GenericUnionString returned is correct"
            Expect.equal true (result3 = Nothing) "GenericUnionString returned is correct"
        }

        testCaseAsync "IServer.echoRecord" <| async {
            let record1 = { Prop1 = "hello"; Prop2 = 10; Prop3 = None }
            let record2 = { Prop1 = ""; Prop2 = 20; Prop3 = Some 10 }
            let record3 = { Prop1 = null; Prop2 = 30; Prop3 = Some 20  }
            let! result1 = proxy.call ( fun server -> server.echoRecord record1 )
            let! result2 = proxy.call ( fun server -> server.echoRecord record2 )
            let! result3 = proxy.call ( fun server -> server.echoRecord record3 )

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

            let! result1 = proxy.call ( fun server -> server.echoNestedGeneric input )
            let! result2 = proxy.call ( fun server -> server.echoNestedGeneric input2 )
            Expect.equal true (input = result1) "Nested generic record is correct"
            Expect.equal true (input2 = result2) "Nested generic record is correct"
        }

        // Inline values cannot always be compiled, so define first and reference from inside the quotation expression
        testCaseAsync "IServer.echoNestedGeneric inline in expression" <| async {
            let! result1 = proxy.call ( fun server -> server.echoNestedGeneric { Value = Just (Some 5); OtherValue = 2 }  )
            let! result2 = proxy.call ( fun server -> server.echoNestedGeneric { Value = Just (None); OtherValue = 2 } )
            Expect.equal true ({ Value = Just (Some 5); OtherValue = 2 } = result1) "Nested generic record is correct"
            Expect.equal true ({ Value = Just (None); OtherValue = 2 } = result2) "Nested generic record is correct"
        }

        testCaseAsync "IServer.echoIntList" <| async {
            let inputList = [1 .. 5]
            let! output = proxy.call ( fun server -> server.echoIntList inputList )
            Expect.equal output [1;2;3;4;5] "The echoed list is correct"
            let emptyList : int list = [ ]
            let! echoedList = proxy.call ( fun server -> server.echoIntList emptyList )
            Expect.equal true (List.isEmpty echoedList) "The echoed list is correct"
        }

        testCaseAsync "IServer.echoSingleCase" <| async {
            let! output = proxy.call ( fun server -> server.echoSingleCase (SingleCase 10) )
            Expect.equal output (SingleCase 10) "Single case union roundtrip works"
        }

        testCaseAsync "IServer.echoStringList" <| async {
            let input = ["one"; "two"; null]
            let! output = proxy.call ( fun server -> server.echoStringList input )
            Expect.equal input output "Echoed list is correct"
            let emptyList : string list = []
            let! echoedList = proxy.call ( fun server -> server.echoStringList emptyList )
            Expect.equal true (List.isEmpty echoedList) "Echoed list is empty"
        }

        testCaseAsync "IServer.echoBoolList" <| async {
            let input = [true; false; true]
            let! output = proxy.call ( fun server -> server.echoBoolList input )
            Expect.equal output input "Echoed list is correct"
            let emptyList : bool list = []
            let! echoedList = proxy.call ( fun server -> server.echoBoolList emptyList )
            Expect.equal true (List.isEmpty echoedList) "Echoed list is empty"
        }

        testCaseAsync "IServer.echoListOfListsOfStrings" <| async {
            let input = [["1"; "2"]; ["3"; "4";"5"]]
            let! output = proxy.call ( fun server -> server.echoListOfListsOfStrings input )
            Expect.equal input output "Echoed list is correct"
        }

        testCaseAsync "IServer.echoResult for Result<int, string>" <| async {
            let! output = proxy.call ( fun server -> server.echoResult (Ok 15) )
            Expect.equal output (Ok 15) "Result is correct"

            let! output = proxy.call ( fun server -> server.echoResult (Result.Error "somewhere here") )
            Expect.equal output (Error "somewhere here")  "Result is correct"
        }

        testCaseAsync "IServer.echoMap" <| async {
            let input = ["hello", 1] |> Map.ofList
            let! output = proxy.call ( fun server -> server.echoMap input )
            Expect.equal input output "Map is echoed correctly"
        }

        (*
            calling the function:
            throwError = fun () -> async {
                return! failwith "Generating custom server error"
            }

            with error handler: (fun ex routeInfo -> Propagate ex.Message)
        *)
        testCaseAsync "IServer.throwError using callSafely" <| async {
            let! result = proxy.callSafely (fun server -> server.throwError())
            match result with
            | Ok value -> failwithf "Got value %A where an error was expected" value
            | Result.Error ex ->
                match ex with
                | :? Fable.Remoting.DotnetClient.Http.ProxyRequestException as reqEx ->
                    Expect.isTrue (reqEx.ResponseText.Contains("Generating custom server error")) "Works"
                | other -> Expect.isTrue false "Should not happen"
        }

        testCaseAsync "IServer.throwError using callSafelyTask" <| async {
            let! result = proxy.callSafelyTask (fun server -> server.throwError()) |> Async.AwaitTask
            match result with
            | Ok value -> failwithf "Got value %A where an error was expected" value
            | Result.Error ex ->
                match ex with
                | :? Fable.Remoting.DotnetClient.Http.ProxyRequestException as reqEx ->
                    Expect.isTrue (reqEx.ResponseText.Contains("Generating custom server error")) "Works"
                | other -> Expect.isTrue false "Should not happen"
        }

        testCaseAsync "IServer.mutliArgFunc" <| async {
            let! result = proxy.call (fun server -> server.multiArgFunc "hello" 10 false)
            Expect.equal 15 result "Result is correct"

            let! sndResult = proxy.call (fun server -> server.multiArgFunc "byebye" 5 true)
            Expect.equal 12 sndResult "Result is correct"
        }

        testCaseAsync "IServer.pureAsync" <| async {
            let! result = proxy.call (fun server -> server.pureAsync)
            Expect.equal 42 result "Pure async without parameters works"
        }

        testCaseAsync "IServer.pureAsync as task" <| async {
            let! result = proxy.callTask (fun server -> server.pureAsync) |> Async.AwaitTask
            Expect.equal 42 result "Pure async without parameters works"
        }

        testCaseAsync "IServer.asyncNestedGeneric" <| async {
            let! result = proxy.call (fun server -> server.asyncNestedGeneric)
            Expect.equal { OtherValue = 10; Value = Just (Some "value") } result "Returned value is correct"
        }

        testCaseAsync "IServer.echoBigInteger" <| async {
            let input = 1I
            let! output = proxy.call (fun server -> server.echoBigInteger input)
            Expect.equal input output "Big int is equal"
        }

        testCaseAsync "IServer.tuplesAndLists" <| async {
            let inputDict = Map.ofList [ "hello", 5 ]
            let inputStrings = [ "there!" ]
            let! output = proxy.call (fun server -> server.tuplesAndLists (inputDict, inputStrings))
            let expected = Map.ofList [ "hello", 5; "there!", 6 ]
            Expect.equal output expected "Echoed map is correct"
        }

        let testDataTableWithProxyCall proxyCall = async {
            let t = new System.Data.DataTable()
            t.TableName <- "myname"
            t.Columns.Add("a", typeof<int64>) |> ignore
            t.Columns.Add("b", typeof<string>) |> ignore
            t.Rows.Add(1L, "11111")  |> ignore
            t.Rows.Add(2L, "222222") |> ignore

            let! (deserialized: System.Data.DataTable) = proxyCall t

            Expect.equal deserialized.TableName      t.TableName      "table name"
            Expect.equal deserialized.Columns.Count  t.Columns.Count  "column count"
            Expect.equal deserialized.Rows.Count     t.Rows.Count     "row count"
            Expect.equal deserialized.Rows.[0].["a"] t.Rows.[0].["a"] "table.[0,'a']"
            Expect.equal deserialized.Rows.[0].["b"] t.Rows.[0].["b"] "table.[0,'b']"
            Expect.equal deserialized.Rows.[1].["a"] t.Rows.[1].["a"] "table.[1,'a']"
            Expect.equal deserialized.Rows.[1].["b"] t.Rows.[1].["b"] "table.[1,'b']"
        }

        let testDataSetWithProxyCall proxyCall = async {
            let t = new System.Data.DataTable()
            t.TableName <- "myname"
            t.Columns.Add("a", typeof<int64>) |> ignore
            t.Columns.Add("b", typeof<string>) |> ignore
            t.Rows.Add(1L, "11111")  |> ignore
            t.Rows.Add(2L, "222222") |> ignore

            let t2 = new System.Data.DataTable()
            t2.TableName <- "myname2"
            t2.Columns.Add("t.a", typeof<int64>) |> ignore
            t2.Columns.Add("b", typeof<string>) |> ignore
            t2.Constraints.Add(System.Data.ForeignKeyConstraint(t.Columns.["a"], t2.Columns.["t.a"], ConstraintName = "t2fk"))
            t2.Rows.Add(1L, "t.11111")  |> ignore
            t2.Rows.Add(2L, "t.222222") |> ignore

            let ds = new System.Data.DataSet()
            ds.Tables.Add t
            ds.Tables.Add t2

            let! (deserialized: System.Data.DataSet) = proxyCall ds

            Expect.equal deserialized.Tables.Count                               ds.Tables.Count  "tables count"
            Expect.equal deserialized.Tables.[0].TableName                       t.TableName      "table name"
            Expect.equal deserialized.Tables.[0].Columns.Count                   t.Columns.Count  "column count"
            Expect.equal deserialized.Tables.[0].Rows.Count                      t.Rows.Count     "row count"
            Expect.equal deserialized.Tables.[0].Rows.[0].["a"]                  t.Rows.[0].["a"] "table.[0,'a']"
            Expect.equal deserialized.Tables.[0].Rows.[0].["b"]                  t.Rows.[0].["b"] "table.[0,'b']"
            Expect.equal deserialized.Tables.[0].Rows.[1].["a"]                  t.Rows.[1].["a"] "table.[1,'a']"
            Expect.equal deserialized.Tables.[0].Rows.[1].["b"]                  t.Rows.[1].["b"] "table.[1,'b']"
            Expect.equal deserialized.Tables.[1].Constraints.[0].ConstraintName "t2fk"            "constraint name"
        }

        testCaseAsync "IServer.echoDataTable" <| async {
            let proxyCall = fun dt -> proxy.call (fun server -> server.echoDataTable dt)
            do! testDataTableWithProxyCall proxyCall
        }

        testCaseAsync "IServer.echoDataSet" <| async {
            let proxyCall = fun ds -> proxy.call (fun server -> server.echoDataSet ds)
            do! testDataSetWithProxyCall proxyCall
        }

        testCaseAsync "IBinaryServer.getLength" <| async {
            let! result =  binaryProxy.call(fun server -> server.getLength "hello")
            Expect.equal 5 result "Length returned is correct"
        }

        testCaseAsync "IBinaryServer.getLength expression from outside" <| async {
            let value = "value from outside"
            let! result =  binaryProxy.call(fun server -> server.getLength value)
            Expect.equal 18 result "Length returned is correct"
        }

        testCaseAsync "IBinaryServer.echoInteger" <| async {
            let! firstResult = binaryProxy.call (fun server -> server.echoInteger 20)
            let! secondResult = binaryProxy.call (fun server -> server.echoInteger 0)
            Expect.equal 20 firstResult "result is echoed correctly"
            Expect.equal 0 secondResult "result is echoed correctly"
        }

        testCaseAsync "IBinaryServer.echoInteger with explicit quotes" <| async {
            let! firstResult = binaryProxy.call <@ fun server -> server.echoInteger 20 @>
            let! secondResult = binaryProxy.call <@ fun server -> server.echoInteger 0 @>
            Expect.equal 20 firstResult "result is echoed correctly"
            Expect.equal 0 secondResult "result is echoed correctly"
        }

        testCaseAsync "IBinaryServer.simpleUnit" <| async {
            let! result =  binaryProxy.call (fun server -> server.simpleUnit ())
            Expect.equal 42 result "result is correct"
        }

        testCaseAsync "IBinaryServer.echoBool" <| async {
            let! one = binaryProxy.call (fun server -> server.echoBool true)
            let! two = binaryProxy.call (fun server -> server.echoBool false)
            Expect.equal one true "Bool result is correct"
            Expect.equal two false "Bool result is correct"
        }

        testCaseAsync "IBinaryServer.echoIntOption" <| async {
            let! one =  binaryProxy.call (fun server -> server.echoIntOption (Some 20))
            let! two =  binaryProxy.call (fun server -> server.echoIntOption None)

            Expect.equal one (Some 20) "Option<int> returned is correct"
            Expect.equal two None "Option<int> returned is correct"
        }

        testCaseAsync "IBinaryServer.echoIntOption from outside" <| async {
            let first = Some 20
            let second : Option<int> = None
            let! one =  binaryProxy.call (fun server -> server.echoIntOption first)
            let! two =  binaryProxy.call (fun server -> server.echoIntOption second)

            Expect.equal one (Some 20) "Option<int> returned is correct"
            Expect.equal two None "Option<int> returned is correct"
        }

        testCaseAsync "IBinaryServer.echoStringOption" <| async {
            let! one = binaryProxy.call (fun server -> server.echoStringOption (Some "value"))
            let! two = binaryProxy.call (fun server -> server.echoStringOption None)
            Expect.equal one (Some "value") "Option<string> returned is correct"
            Expect.equal two None "Option<string> returned is correct"
        }

        testCaseAsync "IBinaryServer.echoStringOption from outside" <| async {
            let first = Some "value"
            let second : Option<string> = None
            let! one = binaryProxy.call (fun server -> server.echoStringOption first)
            let! two = binaryProxy.call (fun server -> server.echoStringOption second)
            Expect.equal one (Some "value") "Option<string> returned is correct"
            Expect.equal two None "Option<string> returned is correct"
        }

        testCaseAsync "IBinaryServer.echoSimpleUnionType" <| async {
            let! result1 = binaryProxy.call (fun server -> server.echoSimpleUnionType One)
            let! result2 = binaryProxy.call (fun server -> server.echoSimpleUnionType Two)
            Expect.equal true (result1 = One) "SimpleUnion returned is correct"
            Expect.equal true (result2 = Two) "SimpleUnion returned is correct"
        }

        testCaseAsync "IBinaryServer.echoSimpleUnionType from outside" <| async {
            let first = One
            let second = Two
            let! result1 = binaryProxy.call (fun server -> server.echoSimpleUnionType first)
            let! result2 = binaryProxy.call (fun server -> server.echoSimpleUnionType second)
            Expect.equal true (result1 = One) "SimpleUnion returned is correct"
            Expect.equal true (result2 = Two) "SimpleUnion returned is correct"
        }

        testCaseAsync "IBinaryServer.echoGenericUnionInt" <| async {
            let! result1 = binaryProxy.call (fun server -> server.echoGenericUnionInt (Just 5))
            let! result2 = binaryProxy.call (fun server -> server.echoGenericUnionInt (Just 10))
            let! result3 = binaryProxy.call (fun server -> server.echoGenericUnionInt Nothing)

            Expect.equal true (result1 = Just 5) "GenericUnionInt returned is correct"
            Expect.equal true (result2 = Just 10) "GenericUnionInt returned is correct"
            Expect.equal true (result3 = Nothing) "GenericUnionInt returned is correct"
        }

        testCaseAsync "IBinaryServer.echoGenericUnionString" <| async {
            let! result1 = binaryProxy.call (fun server -> server.echoGenericUnionString (Just ""))
            let! result2 = binaryProxy.call (fun server -> server.echoGenericUnionString (Just null))
            let! result3 = binaryProxy.call (fun server -> server.echoGenericUnionString Nothing)

            Expect.equal true (result1 = Just "") "GenericUnionString returned is correct"
            Expect.equal true (result2 = Just null) "GenericUnionString returned is correct"
            Expect.equal true (result3 = Nothing) "GenericUnionString returned is correct"
        }

        testCaseAsync "IBinaryServer.echoRecord" <| async {
            let record1 = { Prop1 = "hello"; Prop2 = 10; Prop3 = None }
            let record2 = { Prop1 = ""; Prop2 = 20; Prop3 = Some 10 }
            let record3 = { Prop1 = null; Prop2 = 30; Prop3 = Some 20  }
            let! result1 = binaryProxy.call ( fun server -> server.echoRecord record1 )
            let! result2 = binaryProxy.call ( fun server -> server.echoRecord record2 )
            let! result3 = binaryProxy.call ( fun server -> server.echoRecord record3 )

            Expect.equal true (result1 = record1) "Record returned is correct"
            Expect.equal true (result2 = record2) "Record returned is correct"
            Expect.equal true (result3 = record3) "Record returned is correct"
        }

        testCaseAsync "IBinaryServer.echoNestedGeneric from outside" <| async {
            let input : GenericRecord<Maybe<int option>> = {
                Value = Just (Some 5)
                OtherValue = 2
            }

            let input2 : GenericRecord<Maybe<int option>> = {
                Value = Just (None)
                OtherValue = 2
            }

            let! result1 = binaryProxy.call ( fun server -> server.echoNestedGeneric input )
            let! result2 = binaryProxy.call ( fun server -> server.echoNestedGeneric input2 )
            Expect.equal true (input = result1) "Nested generic record is correct"
            Expect.equal true (input2 = result2) "Nested generic record is correct"
        }

        // Inline values cannot always be compiled, so define first and reference from inside the quotation expression
        testCaseAsync "IBinaryServer.echoNestedGeneric inline in expression" <| async {
            let! result1 = binaryProxy.call ( fun server -> server.echoNestedGeneric { Value = Just (Some 5); OtherValue = 2 }  )
            let! result2 = binaryProxy.call ( fun server -> server.echoNestedGeneric { Value = Just (None); OtherValue = 2 } )
            Expect.equal true ({ Value = Just (Some 5); OtherValue = 2 } = result1) "Nested generic record is correct"
            Expect.equal true ({ Value = Just (None); OtherValue = 2 } = result2) "Nested generic record is correct"
        }

        testCaseAsync "IBinaryServer.echoIntList" <| async {
            let inputList = [1 .. 5]
            let! output = binaryProxy.call ( fun server -> server.echoIntList inputList )
            Expect.equal output [1;2;3;4;5] "The echoed list is correct"
            let emptyList : int list = [ ]
            let! echoedList = binaryProxy.call ( fun server -> server.echoIntList emptyList )
            Expect.equal true (List.isEmpty echoedList) "The echoed list is correct"
        }

        testCaseAsync "IBinaryServer.echoSingleCase" <| async {
            let! output = binaryProxy.call ( fun server -> server.echoSingleCase (SingleCase 10) )
            Expect.equal output (SingleCase 10) "Single case union roundtrip works"
        }

        testCaseAsync "IBinaryServer.echoStringList" <| async {
            let input = ["one"; "two"; null]
            let! output = binaryProxy.call ( fun server -> server.echoStringList input )
            Expect.equal input output "Echoed list is correct"
            let emptyList : string list = []
            let! echoedList = binaryProxy.call ( fun server -> server.echoStringList emptyList )
            Expect.equal true (List.isEmpty echoedList) "Echoed list is empty"
        }

        testCaseAsync "IBinaryServer.echoBoolList" <| async {
            let input = [true; false; true]
            let! output = binaryProxy.call ( fun server -> server.echoBoolList input )
            Expect.equal output input "Echoed list is correct"
            let emptyList : bool list = []
            let! echoedList = binaryProxy.call ( fun server -> server.echoBoolList emptyList )
            Expect.equal true (List.isEmpty echoedList) "Echoed list is empty"
        }

        testCaseAsync "IBinaryServer.echoListOfListsOfStrings" <| async {
            let input = [["1"; "2"]; ["3"; "4";"5"]]
            let! output = binaryProxy.call ( fun server -> server.echoListOfListsOfStrings input )
            Expect.equal input output "Echoed list is correct"
        }

        testCaseAsync "IBinaryServer.echoResult for Result<int, string>" <| async {
            let! output = binaryProxy.call ( fun server -> server.echoResult (Ok 15) )
            Expect.equal output (Ok 15) "Result is correct"

            let! output = binaryProxy.call ( fun server -> server.echoResult (Result.Error "somewhere here") )
            Expect.equal output (Error "somewhere here")  "Result is correct"
        }

        testCaseAsync "IBinaryServer.echoMap" <| async {
            let input = ["hello", 1] |> Map.ofList
            let! output = binaryProxy.call ( fun server -> server.echoMap input )
            Expect.equal input output "Map is echoed correctly"
        }

        testCaseAsync "IBinaryServer.mutliArgFunc" <| async {
            let! result = binaryProxy.call (fun server -> server.multiArgFunc "hello" 10 false)
            Expect.equal 15 result "Result is correct"

            let! sndResult = binaryProxy.call (fun server -> server.multiArgFunc "byebye" 5 true)
            Expect.equal 12 sndResult "Result is correct"
        }

        testCaseAsync "IBinaryServer.pureAsync" <| async {
            let! result = binaryProxy.call (fun server -> server.pureAsync)
            Expect.equal 42 result "Pure async without parameters works"
        }

        testCaseAsync "IBinaryServer.asyncNestedGeneric" <| async {
            let! result = binaryProxy.call (fun server -> server.asyncNestedGeneric)
            Expect.equal { OtherValue = 10; Value = Just (Some "value") } result "Returned value is correct"
        }

        testCaseAsync "IBinaryServer.echoBigInteger" <| async {
            let input = 1I
            let! output = binaryProxy.call (fun server -> server.echoBigInteger input)
            Expect.equal input output "Big int is equal"
        }

        testCaseAsync "IBinaryServer.echoBigInteger as task" <| async {
            let input = 1I
            let! output = binaryProxy.callTask (fun server -> server.echoBigInteger input) |> Async.AwaitTask
            Expect.equal input output "Big int is equal"
        }

        testCaseAsync "IBinaryServer.tuplesAndLists" <| async {
            let inputDict = Map.ofList [ "hello", 5 ]
            let inputStrings = [ "there!" ]
            let! output = binaryProxy.call (fun server -> server.tuplesAndLists (inputDict, inputStrings))
            let expected = Map.ofList [ "hello", 5; "there!", 6 ]
            Expect.equal output expected "Echoed map is correct"
        }

        testCaseAsync "IBinaryServer.echoDataTable" <| async {
            let proxyCall = fun dt -> binaryProxy.call (fun server -> server.echoDataTable dt)
            do! testDataTableWithProxyCall proxyCall
        }

        testCaseAsync "IBinaryServer.echoDataSet" <| async {
            let proxyCall = fun ds -> binaryProxy.call (fun server -> server.echoDataSet ds)
            do! testDataSetWithProxyCall proxyCall
        }

        testCaseAsync "IBinaryServer.pureTask as async" <| async {
            let! output = binaryProxy.call (fun server -> server.pureTask)
            Expect.equal output 42 "Pure task without parameters works"
        }

        testCaseAsync "IBinaryServer.pureTask as task" <| async {
            let! output = binaryProxy.callTask (fun server -> server.pureTask) |> Async.AwaitTask
            Expect.equal output 42 "Pure task without parameters works"
        }

        testCaseAsync "IBinaryServer.echoMapTask as async" <| async {
            let expected = Map.ofList [ "yup", 6 ]
            let! output = binaryProxy.call (fun server -> server.echoMapTask expected)
            Expect.equal output expected "Echoed map is correct"
        }

        testCaseAsync "IBinaryServer.echoMapTask as task" <| async {
            let expected = Map.ofList [ "yup", 6 ]
            let! output = binaryProxy.callTask (fun server -> server.echoMapTask expected) |> Async.AwaitTask
            Expect.equal output expected "Echoed map is correct"
        }

        testCaseAsync "IBinaryServer.pureTask as async with explicit quotes" <| async {
            let! output = binaryProxy.call <@ fun server -> server.pureTask @>
            Expect.equal output 42 "Pure task without parameters works"
        }

        testCaseAsync "IBinaryServer.pureTask as task with explicit quotes" <| async {
            let! output = binaryProxy.callTask <@ fun server -> server.pureTask @> |> Async.AwaitTask
            Expect.equal output 42 "Pure task without parameters works"
        }

        testCaseAsync "IBinaryServer.echoMapTask as async with explicit quotes" <| async {
            let expected = Map.ofList [ "yup", 6 ]
            let! output = binaryProxy.call <@ fun server -> server.echoMapTask expected @>
            Expect.equal output expected "Echoed map is correct"
        }

        testCaseAsync "IBinaryServer.echoMapTask as task with explicit quotes" <| async {
            let expected = Map.ofList [ "yup", 6 ]
            let! output = binaryProxy.callTask <@ fun server -> server.echoMapTask expected @> |> Async.AwaitTask
            Expect.equal output expected "Echoed map is correct"
        }
    ]
