module StjWireFormatTests

// Phase 4 — runs the full Phase 2 byte-compat gallery through the System.Text.Json
// converter set. Every assertion in WireFormatTests.fs must hold byte-equally
// against the STJ serializer.

open System.Text.Json
open Fable.Remoting.Json.SystemTextJson
open Expecto

let private stjSerializer : WireFormatTests.ISerializer =
    let options = FableConverters.create ()
    { new WireFormatTests.ISerializer with
        member _.Serialize<'a>(value: 'a) = JsonSerializer.Serialize<'a>(value, options) }

let stjWireFormatTests =
    WireFormatTests.buildWireFormatTests "Phase 4 — wire format byte-compat (STJ)" stjSerializer
