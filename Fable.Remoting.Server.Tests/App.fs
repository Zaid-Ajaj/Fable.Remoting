module Program 

open Expecto
open Expecto.Logging
open ServerDynamicInvokeTests 

let testConfig =  { Expecto.Tests.defaultConfig with 
                        parallelWorkers = 1
                        verbosity = LogLevel.Debug }

[<EntryPoint>]
let main args = runTests testConfig allTests      
