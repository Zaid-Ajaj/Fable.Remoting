module Program 

open Expecto
open Expecto.Logging

open FableGiraffeAdapterTests 

let testConfig =  { Expecto.Tests.defaultConfig with 
                        parallelWorkers = 1
                        verbosity = LogLevel.Debug }

[<EntryPoint>]
let main _ = runTests testConfig fableGiraffeAdapterTests
