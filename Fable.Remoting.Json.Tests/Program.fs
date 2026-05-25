module Program

open Expecto
open JsonConverterTests
open WireFormatTests
open StjUnionPrototypeTests
open StjWireFormatTests

let allTests = testList "Fable.Remoting.Json tests" [
    converterTest
    wireFormatTests
    unionStjPrototypeTests
    stjWireFormatTests
    stjFixesNewtonsoftNullBug
]

[<EntryPoint>]
let main args = runTests defaultConfig allTests
