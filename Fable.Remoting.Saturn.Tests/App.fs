module Program 

open Expecto
open Expecto.Logging

open FableSaturnAdapterTests 

let testConfig =  { Expecto.Tests.defaultConfig with 
                        verbosity = LogLevel.Debug }

[<EntryPoint>]
let main _ = runTests testConfig FableSaturnAdapterTests
