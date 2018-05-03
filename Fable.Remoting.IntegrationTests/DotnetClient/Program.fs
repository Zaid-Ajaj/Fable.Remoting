// Learn more about F# at http://fsharp.org

open System
open SharedTypes
open Fable.Remoting.DotnetClient 
open Expecto
open Expecto.Logging

let proxy = Proxy.create<IServer> (sprintf "http://localhost:8080/api/%s/%s")
let dotnetClientTests = 
    testList "Dotnet Client tests" [

        testCaseAsync "IServer.getLength" <| async {
            let! result =  proxy.call <@ fun server -> server.getLength "hello" @> 
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

        testCaseAsync "IServer.simpleUnit" <| async {
            let! result =  proxy.call <@ fun server -> server.simpleUnit () @> 
            Expect.equal 42 result "result is correct"
        }

        testCaseAsync "IServer.echoBool" <| async {
            let! one = proxy.call <@ fun server -> server.echoBool true @>
            let! two = proxy.call <@ fun server -> server.echoBool false  @> 
            Expect.equal one true "Bool result is correct"
            Expect.equal two false "Bool result is correct"
        }

        testCaseAsync "IServer.echoIntOption" <| async {
            let! one =  proxy.call <@ fun server -> server.echoIntOption (Some 20) @> 
            let! two =  proxy.call <@ fun server -> server.echoIntOption None @> 
            
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

        testCaseAsync "IServer.echoGenericUnionString" <| async { 
            let! result1 = proxy.call <@ fun server -> server.echoGenericUnionString (Just "") @>
            let! result2 = proxy.call <@ fun server -> server.echoGenericUnionString (Just null) @>
            let! result3 = proxy.call <@ fun server -> server.echoGenericUnionString Nothing @>

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
        //testCaseAsync "IServer.echoNestedGeneric inline in expression" <| async { 
        //    let! result1 = proxy.call <@ fun server -> server.echoNestedGeneric { Value = Just (Some 5); OtherValue = 2 }  @>
        //    let! result2 = proxy.call <@ fun server -> server.echoNestedGeneric { Value = Just (None); OtherValue = 2 } @>
        //    Expect.equal true ({ Value = Just (Some 5); OtherValue = 2 } = result1) "Nested generic record is correct"
        //    Expect.equal true ({ Value = Just (None); OtherValue = 2 } = result2) "Nested generic record is correct"
        //}

        testCaseAsync "IServer.echoIntList" <| async {
            let inputList = [1 .. 5]
            let! output = proxy.call <@ fun server -> server.echoIntList inputList @>
            Expect.equal output [1;2;3;4;5] "The echoed list is correct"
            let emptyList : int list = [ ] 
            let! echoedList = proxy.call <@ fun server -> server.echoIntList emptyList @>
            Expect.equal true (List.isEmpty echoedList) "The echoed list is correct"          
        }

        testCaseAsync "IServer.echoSingleCase" <| async {
            let! output = proxy.call <@ fun server -> server.echoSingleCase (SingleCase 10) @>
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

        testCaseAsync "IServer.echoBoolList" <| async {
            let input = [true; false; true]
            let! output = proxy.call <@ fun server -> server.echoBoolList input @>
            Expect.equal output input "Echoed list is correct"
            let emptyList : bool list = [] 
            let! echoedList = proxy.call <@ fun server -> server.echoBoolList emptyList @>
            Expect.equal true (List.isEmpty echoedList) "Echoed list is empty"
        }        
        
    ]

let testConfig =  { Expecto.Tests.defaultConfig with 
                        parallelWorkers = 1
                        verbosity = LogLevel.Debug }
                        
[<EntryPoint>]
let main argv = runTests testConfig dotnetClientTests