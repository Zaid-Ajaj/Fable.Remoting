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
    ]

let testConfig =  { Expecto.Tests.defaultConfig with 
                        parallelWorkers = 1
                        verbosity = LogLevel.Debug }
                        
[<EntryPoint>]
let main argv = runTests testConfig dotnetClientTests