module Program

open Expecto
open Expecto.Logging

open FableFalcoAdapterTests
open StjHttpIntegrationTests
open LegacyNewtonsoftIntegrationTests

let testConfig =  { defaultConfig with verbosity = Debug }

let allTests = testList "All Tests" [
    fableFalcoAdapterTests
    stjFalcoIntegrationTests
    legacyNewtonsoftFalcoTests
]

[<EntryPoint>]
let main _ = runTests testConfig allTests