module Program 

open Expecto
open JsonConverterTests 

[<EntryPoint>]
let main args = runTests defaultConfig converterTest      
