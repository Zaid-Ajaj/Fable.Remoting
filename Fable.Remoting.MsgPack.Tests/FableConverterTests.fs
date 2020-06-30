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

let serializeDeserializeCompareWithLength<'a when 'a: equality> expectedLength (value: 'a) =
    use ms = new MemoryStream ()
    MsgPack.Write.writeObj value ms
    let data = ms.ToArray ()

    let deserialized = MsgPack.Read.Reader(data).Read typeof<'a> :?> 'a

    equal value deserialized
    Expect.equal data.Length expectedLength (sprintf "The expected and actual payload lengths must match.")

let converterTest =
    testList "Converter Tests" [
        test "Unit" {
            () |> serializeDeserializeCompare
        }
        test "Fixed negative number, single byte" {
            -20 |> serializeDeserializeCompareWithLength 1
        }
        test "Maybe" {
            Just 1 |> serializeDeserializeCompare 
        }
        test "Nested maybe array works" {
            Just [| Nothing; Just 1 |] |> serializeDeserializeCompare 
        }
        test "Record" {
            { Prop1 = ""; Prop2 = 2; Prop3 = Some 3 } |> serializeDeserializeCompare 
        }
        test "None" {
            None |> serializeDeserializeCompare 
        }
        test "Some string works" {
            Some "ddd" |> serializeDeserializeCompare 
        }
        test "Long serialized as fixnum" {
            20L |> serializeDeserializeCompare 
        }
        test "Long serialized as int16, 3 bytes" {
            60_000L |> serializeDeserializeCompareWithLength 3
        }
        test "Array of 3 bools, 4 bytes" {
            [| false; true; true |] |> serializeDeserializeCompareWithLength 4
        }
        test "List of fixnums" {
            [ 0; 2; 100; 10 ] |> serializeDeserializeCompare
        }
        test "DateTime" {
            DateTime.Now |> serializeDeserializeCompare
        }
        test "String16 with non-ASCII characters" {
            "δασςεφЯШзЖ888dsadčšřποιθθψζψ" |> serializeDeserializeCompare
        }
        test "Fixstr with non-ASCII characters" {
            "δ" |> serializeDeserializeCompare
        }
        test "String32 with non-ASCII characters" {
            String.init 70_000 (fun _ -> "ΰ") |> serializeDeserializeCompare
        }
        test "Decimal" {
            3.1415926535m |> serializeDeserializeCompare
        }
        test "Dict" {
            dict [ "a", 1; "b", 2 ] |> serializeDeserializeCompare
        }
        test "Binary data bin8, 5 bytes" {
            [| 55uy; 0uy; 255uy |] |> serializeDeserializeCompareWithLength 5
        }
        test "Binary data bin16, 303 bytes" {
            [| for _ in 1 .. 300 -> 55uy |] |> serializeDeserializeCompareWithLength 303
        }
        test "Binary data bin32, 80005 bytes" {
            [| for _ in 1 .. 80_000 -> 23uy |] |> serializeDeserializeCompareWithLength 80_005
        }
    ]