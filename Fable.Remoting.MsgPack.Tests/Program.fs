module Program 

open Expecto
open MsgPackConverterTests 

[<EntryPoint>]
let main args = runTests defaultConfig converterTest      
