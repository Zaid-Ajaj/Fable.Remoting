// Learn more about F# at http://fsharp.org

open System
open SharedTypes
open Fable.Remoting.DotnetClient 
open Expecto
open Expecto.Logging
let dotnetClientTests = 
    testList "Dotnet Client tests" [
        testCase "Proxy can be created" <| fun _ ->
            let proxy = Proxy.create<ISimpleServer> (sprintf "http://localhost:8080/api/%s/%s")
            Expect.notEqual null (box proxy) "Generated server proxy is not null"

        testCaseAsync "Calling server works" <| async {
            let proxy = Proxy.create<ISimpleServer> (sprintf "http://localhost:8080/api/%s/%s")
            let! result =  proxy.CallAs<int> <@ fun server -> server.getLength "hello" @> 
            Expect.equal 5 result "Length returned is correct"
        }
    ]

let testConfig =  { Expecto.Tests.defaultConfig with 
                        parallelWorkers = 1
                        verbosity = LogLevel.Debug }
                        
[<EntryPoint>]
let main argv = runTests testConfig dotnetClientTests