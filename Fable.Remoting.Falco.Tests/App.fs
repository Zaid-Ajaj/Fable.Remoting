module Program

open Expecto
open Expecto.Logging

open FableFalcoAdapterTests
open StjHttpIntegrationTests

let testConfig =  { defaultConfig with verbosity = Debug }

let allTests = testList "All Tests" [
    fableFalcoAdapterTests
    stjFalcoIntegrationTests
]

[<EntryPoint>]
let main _ = runTests testConfig allTests