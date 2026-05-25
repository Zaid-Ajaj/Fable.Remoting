module Program

open Expecto
open JsonConverterTests
open WireFormatTests

let allTests = testList "Fable.Remoting.Json tests" [
    converterTest
    wireFormatTests
]

[<EntryPoint>]
let main args = runTests defaultConfig allTests
