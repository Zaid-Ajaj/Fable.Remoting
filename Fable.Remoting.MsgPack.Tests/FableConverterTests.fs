module MsgPackConverterTests

open System
open Expecto
open Types
open Expecto.Logging
open Fable.Remoting
open System.IO
open System.Collections.Generic
open System.Numerics

let equal x y = Expect.equal true (x = y) (sprintf "%A = %A" x y)
let pass() = Expect.equal true true ""
let fail () = Expect.equal false true ""

let serializeDeserializeCompare<'a when 'a: equality> (value: 'a) =
    use ms = new MemoryStream ()
    MsgPack.Write.object value ms

    let deserialized = MsgPack.Read.Reader(ms.ToArray ()).Read typeof<'a> :?> 'a

    equal value deserialized

let serializeDeserializeCompareSequence (value: 'a) =
    use ms = new MemoryStream ()
    MsgPack.Write.object value ms

    let deserialized = MsgPack.Read.Reader(ms.ToArray ()).Read typeof<'a> :?> 'a

    Expect.sequenceEqual value deserialized "Sequences must be equal."

let serializeDeserializeCompareWithLength<'a when 'a: equality> expectedLength (value: 'a) =
    use ms = new MemoryStream ()
    MsgPack.Write.object value ms
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
        test "List of fixnums, 5 bytes" {
            [ 0; 2; 100; 10 ] |> serializeDeserializeCompareWithLength 5
        }
        test "DateTime" {
            DateTime.Now |> serializeDeserializeCompare
        }
        test "DateTimeOffset" {
            DateTimeOffset.Now |> serializeDeserializeCompare
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
        test "Map16 with map" {
            Map.ofArray [| for i in 1 .. 295 -> i, (i * i) |] |> serializeDeserializeCompare
        }
        test "Fixmap with dictionary of nothing" {
            Map.ofArray [| for i in 1 .. 2 -> i, Nothing |] |> Dictionary<_, _> |> serializeDeserializeCompareSequence
        }
        test "Map32 with dictionary" {
            Map.ofArray [| for i in 1 .. 80_000 -> i, i |] |> Dictionary<_, _> |> serializeDeserializeCompareSequence
        }
        test "Generic map" {
            Map.ofList [ "firstKey", Just 5; "secondKey", Nothing ] |> serializeDeserializeCompare
            Map.ofList [ 5000, Just 5; 1, Nothing ] |> serializeDeserializeCompare
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
        test "Array32 of long" {
            [| for _ in 1 .. 80_000 -> 5_000_000_000L |] |> serializeDeserializeCompare
        }
        test "Array32 of int32" {
            [| 1 .. 100000 |] |> serializeDeserializeCompare
        }
        test "Recursive record" {
            {
                Name = "root"
                Result = Ok 2
                Children = [
                    { Name = "Child 1"; Result = Result.Error ""; Children = [ { Name = "Grandchild"; Result = Ok -22; Children = [ ] } ] }
                    { Name = "Child 1"; Result = Ok 22; Children = [ ] }
                ]
            }
            |> serializeDeserializeCompare
        }
        test "Complex tuple" {
            ((String50.Create "as", Some ()), [ 0; 0; 25 ], { Name = ":^)"; Result = Ok 1; Children = [] }) |> serializeDeserializeCompare
        }
        test "Bigint" {
            -2I |> serializeDeserializeCompare
            12345678912345678912345678912345679123I |> serializeDeserializeCompare
        }
        test "TimeSpan" {
            TimeSpan.FromMilliseconds 0. |> serializeDeserializeCompare
            TimeSpan.FromDays 33. |> serializeDeserializeCompare
        }
        test "Enum" {
            SomeEnum.Val1 |> serializeDeserializeCompareWithLength 1
        }
        test "Guid" {
            Guid.NewGuid () |> serializeDeserializeCompareWithLength 18
        }
        test "Value option" {
            ValueSome "blah" |> serializeDeserializeCompare
            (ValueNone: int voption) |> serializeDeserializeCompare
        }
        test "Union cases with no parameters" {
            A |> serializeDeserializeCompare
            B |> serializeDeserializeCompare
        }
        test "Option of option" {
            Some (Some 5) |> serializeDeserializeCompare
            Some (None: int option) |> serializeDeserializeCompare
            (None: int option option) |> serializeDeserializeCompare
        }
        test "List of unions" {
            [ Just 4; Nothing ] |> serializeDeserializeCompare
            [ Just 4; Nothing ] |> serializeDeserializeCompare
        }
    ]