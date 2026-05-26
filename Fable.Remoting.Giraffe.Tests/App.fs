module Program

open Expecto
open Expecto.Logging

open FableGiraffeAdapterTests
open MiddlewareTests
open StjHttpIntegrationTests
open LegacyNewtonsoftIntegrationTests
let testConfig =  { defaultConfig with verbosity = Debug }

let allTests = testList "All Tests" [ fableGiraffeAdapterTests; middlewareTests; stjHttpIntegrationTests; legacyNewtonsoftGiraffeTests ]

[<EntryPoint>]
let main _ = runTests testConfig allTests