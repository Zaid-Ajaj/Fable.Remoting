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

let converterTest =
    testList "Converter Tests" [
        test "Maybe works" {
            let actual = Just 1
            use ms = new MemoryStream ()
            MsgPack.Write.writeObj actual ms

            let deserialized = MsgPack.Read.Reader(ms.ToArray ()).Read typeof<Maybe<int>> :?> Maybe<int>

            equal actual deserialized
        }
        test "Record works" {
            let actual = Just [| Nothing; Just 1 |]
            use ms = new MemoryStream ()
            MsgPack.Write.writeObj actual ms

            let deserialized = MsgPack.Read.Reader(ms.ToArray ()).Read typeof<Maybe<Maybe<int>[]>> :?> Maybe<Maybe<int>[]>

            equal actual deserialized
        }
        test "Nested maybe array works" {
            let actual = { Prop1 = ""; Prop2 = 2; Prop3 = Some 3 }
            use ms = new MemoryStream ()
            MsgPack.Write.writeObj actual ms

            let deserialized = MsgPack.Read.Reader(ms.ToArray ()).Read typeof<Record> :?> Record

            equal actual deserialized
        }
    ]