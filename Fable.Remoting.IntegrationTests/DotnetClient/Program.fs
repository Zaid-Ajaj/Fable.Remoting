// Learn more about F# at http://fsharp.org

open System
open SharedTypes
open Fable.Remoting.DotnetClient 
open Expecto
open Expecto.Logging
let dotnetClientTests = 
    testList "Dotnet Client tests" [
        testCase "Proxy can be created" <| fun _ ->
            let server = Proxy.createAn<ISimpleServer> "http://localhost:8080" (sprintf "/api/%s/%s")
            ()

        testCaseAsync "Calling server works" <| async {
            let server = Proxy.createAn<ISimpleServer> "http://localhost:8080" (sprintf "/api/%s/%s")
            let! (result : int) = server.getLength "hello"
            Expect.equal 5 result "Length returned is correct"
        }
    ]

let testConfig =  { Expecto.Tests.defaultConfig with 
                        parallelWorkers = 4
                        verbosity = LogLevel.Debug }
                        
[<EntryPoint>]
let main argv = runTests testConfig dotnetClientTests