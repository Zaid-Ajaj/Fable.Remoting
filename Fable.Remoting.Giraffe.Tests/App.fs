module Program

open Expecto
open Expecto.Logging

open FableGiraffeAdapterTests
open MiddlewareTests
open StjHttpIntegrationTests
let testConfig =  { defaultConfig with verbosity = Debug }

let allTests = testList "All Tests" [ fableGiraffeAdapterTests; middlewareTests; stjHttpIntegrationTests ]

[<EntryPoint>]
let main _ = runTests testConfig allTests