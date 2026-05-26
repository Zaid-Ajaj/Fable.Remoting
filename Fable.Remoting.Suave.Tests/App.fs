module Program

open Expecto
open Expecto.Logging

open FableSuaveAdapterTests
open StjHttpIntegrationTests
open LegacyNewtonsoftIntegrationTests

let testConfig =  { Expecto.Tests.defaultConfig with
                        verbosity = LogLevel.Debug
                        parallelWorkers = 1 }


let allTests = testList "All Suave tests" [
    fableSuaveAdapterTests
    stjSuaveIntegrationTests
    legacyNewtonsoftSuaveTests
]

[<EntryPoint>]
let main args = runTests testConfig allTests
