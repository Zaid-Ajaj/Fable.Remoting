module Program 

open Expecto
open ServerDynamicInvokeTests 

let config = Expecto.Tests.defaultConfig

[<EntryPoint>]
let main args = runTests config serverTests      
