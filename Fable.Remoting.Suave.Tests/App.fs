module Program 

open Expecto
open Expecto.Logging

open FableSuaveAdapterTests 

let testConfig =  { Expecto.Tests.defaultConfig with 
                        verbosity = LogLevel.Debug
                        parallelWorkers = 1 }
                         

[<EntryPoint>]
let main args = runTests testConfig fableSuaveAdapterTests      
