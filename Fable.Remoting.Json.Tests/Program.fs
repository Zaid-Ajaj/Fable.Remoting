module Program

open Expecto
open JsonConverterTests
open WireFormatTests
open StjUnionPrototypeTests

let allTests = testList "Fable.Remoting.Json tests" [
    converterTest
    wireFormatTests
    unionStjPrototypeTests
]

[<EntryPoint>]
let main args = runTests defaultConfig allTests
