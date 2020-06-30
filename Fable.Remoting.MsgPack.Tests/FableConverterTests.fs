module MsgPackConverterTests

open System
open Expecto
open Types
open Expecto.Logging
open Fable.Remoting
open System.IO

let equal x y = Expect.equal true (x = y) (sprintf "%A = %A" x y)
let pass() = Expect.equal true true ""
let fail () = Expect.equal false true ""

let serializeDeserializeCompare<'a when 'a: equality> (value: 'a) =
    use ms = new MemoryStream ()
    MsgPack.Write.writeObj value ms

    let deserialized = MsgPack.Read.Reader(ms.ToArray ()).Read typeof<'a> :?> 'a

    equal value deserialized 

let converterTest =
    testList "Converter Tests" [
        test "Fixed negative number works and is a single byte" {
            let actual = -20
            use ms = new MemoryStream ()
            MsgPack.Write.writeObj actual ms
            let data = ms.ToArray ()

            let deserialized = MsgPack.Read.Reader(data).Read typeof<int> :?> int

            equal actual deserialized
            Expect.equal data.Length 1 "Negative number more than -32 has to be serialized in a single byte."
        }
        test "Maybe works" {
            Just 1 |> serializeDeserializeCompare 
        }
        test "Nested maybe array works" {
            Just [| Nothing; Just 1 |] |> serializeDeserializeCompare 
        }
        test "Record works" {
            { Prop1 = ""; Prop2 = 2; Prop3 = Some 3 } |> serializeDeserializeCompare 
        }
        test "None works" {
            None |> serializeDeserializeCompare 
        }
        test "Some string works" {
            Some "ddd" |> serializeDeserializeCompare 
        }
    ]