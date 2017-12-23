module Program 

open Expecto
open Expecto.Logging

open FableSuaveAdapterTests 

let testConfig =  { Expecto.Tests.defaultConfig with 
                        verbosity = LogLevel.Debug }

[<EntryPoint>]
let main args = runTests testConfig fableSuaveAdapterTests      
