module Program

open Expecto
open Expecto.Logging
open Fable.Remoting.AzureFunctions.Worker.Tests.Client

let testConfig =  { defaultConfig with verbosity = Debug }

let allTests = testList "All Tests" [ AdapterTests.fableAzureFunctionsAdapter; MiddlewareTests.middlewareTests ]

[<EntryPoint>]
let main _ = runTests testConfig allTests