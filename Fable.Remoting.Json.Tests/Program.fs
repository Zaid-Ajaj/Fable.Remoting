module Program

open Expecto
open JsonConverterTests
open WireFormatTests
open StjUnionPrototypeTests
open StjWireFormatTests
open StjFableClientWireTests

let allTests = testList "Fable.Remoting.Json tests" [
    converterTest
    wireFormatTests
    unionStjPrototypeTests
    stjWireFormatTests
    stjFixesNewtonsoftNullBug
    stjFableClientWireTests
]

[<EntryPoint>]
let main args = runTests defaultConfig allTests
