module Program

open Expecto
open Expecto.Logging

open FableFalcoAdapterTests
let testConfig =  { defaultConfig with verbosity = Debug }

[<EntryPoint>]
let main _ = runTests testConfig fableFalcoAdapterTests