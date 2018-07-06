module Program 

open Expecto
open Expecto.Logging

open FableGiraffeAdapterTests 
open MiddlewareTests 
let testConfig =  { Expecto.Tests.defaultConfig with 
                        verbosity = LogLevel.Debug }

let allTests = testList "All Tests" [ fableGiraffeAdapterTests; middlewareTests ]

[<EntryPoint>]
let main _ = runTests testConfig allTests
