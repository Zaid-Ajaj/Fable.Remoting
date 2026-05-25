module Program

open Expecto
open Expecto.Logging

open FableSuaveAdapterTests
open StjHttpIntegrationTests

let testConfig =  { Expecto.Tests.defaultConfig with
                        verbosity = LogLevel.Debug
                        parallelWorkers = 1 }


let allTests = testList "All Suave tests" [
    fableSuaveAdapterTests
    stjSuaveIntegrationTests
]

[<EntryPoint>]
let main args = runTests testConfig allTests
