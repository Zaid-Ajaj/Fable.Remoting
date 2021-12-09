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
    MsgPack.Write.serializeObj value ms

    let deserialized = MsgPack.Read.Reader(ms.ToArray ()).Read typeof<'a> :?> 'a

    equal value deserialized

let serializeDeserialize<'a> (value: 'a) =
    use ms = new MemoryStream ()
    MsgPack.Write.serializeObj value ms

    MsgPack.Read.Reader(ms.ToArray ()).Read typeof<'a> :?> 'a

let serializeDeserializeCompareSequence (value: 'a) =
    use ms = new MemoryStream ()
    MsgPack.Write.serializeObj value ms
    let data = ms.ToArray ()
    let inputCopy = Array.copy data

    let deserialized = MsgPack.Read.Reader(data).Read typeof<'a> :?> 'a

    Expect.sequenceEqual value deserialized "Sequences must be equal."
    Expect.sequenceEqual data inputCopy "The input data has been changed."

let serializeDeserializeCompareWithLength<'a when 'a: equality> expectedLength (value: 'a) =
    use ms = new MemoryStream ()
    MsgPack.Write.serializeObj value ms
    let data = ms.ToArray ()
    let inputCopy = Array.copy data

    let deserialized = MsgPack.Read.Reader(data).Read typeof<'a> :?> 'a

    equal value deserialized
    Expect.equal data.Length expectedLength "The expected and actual payload lengths must match."
    Expect.sequenceEqual data inputCopy "The input data has been changed."

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
            { Prop1 = ""; Prop2 = 2; Prop3 = None } |> serializeDeserializeCompare
        }
        test "None" {
            (None: string option) |> serializeDeserializeCompare
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
        test "uint64, 9 bytes" {
            637588453436987750UL |> serializeDeserializeCompareWithLength 9
        }
        test "int64, 9 bytes" {
            -137588453400987759L |> serializeDeserializeCompareWithLength 9
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

        test "DateTime conversions preverses Kind" {
            let nowTicks = DateTime.Now.Ticks
            let localNow = DateTime(nowTicks, DateTimeKind.Local)
            let utcNow = DateTime(nowTicks, DateTimeKind.Utc)
            let unspecifiedNow = DateTime(nowTicks, DateTimeKind.Unspecified)

            let localNowDeserialized = serializeDeserialize localNow
            let utcNowDeserialized = serializeDeserialize utcNow
            let unspecifiedNowDeserialized = serializeDeserialize unspecifiedNow

            Expect.equal DateTimeKind.Local localNowDeserialized.Kind "Local is preserved"
            Expect.equal DateTimeKind.Utc utcNowDeserialized.Kind "Utc is preserved"
            Expect.equal DateTimeKind.Unspecified unspecifiedNowDeserialized.Kind "Unspecified is preserved"

            Expect.equal localNow localNowDeserialized "Now(Local) can be converted"
            Expect.equal utcNow utcNowDeserialized "Now(Utc) can be converted"
            Expect.equal unspecifiedNow unspecifiedNowDeserialized "Now(Unspecified) can be converted"
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
            32313213121.1415926535m |> serializeDeserializeCompare
        }
        test "Map16 with map" {
            Map.ofArray [| for i in 1 .. 295 -> i, (i * i) |] |> serializeDeserializeCompare
        }
        test "Fixmap with dictionary of nothing" {
            Map.ofArray [| for i in 1 .. 2 -> i, Nothing |] |> Dictionary<_, Maybe<bool>> |> serializeDeserializeCompareSequence
        }
        test "Map32 with dictionary" {
            Map.ofArray [| for i in 1 .. 80_000 -> i, i |] |> Dictionary<_, _> |> serializeDeserializeCompareSequence
        }
        test "Generic map" {
            Map.ofList [ "firstKey", Just 5; "secondKey", Nothing ] |> serializeDeserializeCompare
            Map.ofList [ 5000, Just 5; 1, Nothing ] |> serializeDeserializeCompare
        }
        test "Set16" {
            Set.ofArray [| for i in 1 .. 295 -> i |] |> serializeDeserializeCompare
        }
        test "Set32" {
            Set.ofArray [| for i in 1 .. 80_000 -> i |] |> serializeDeserializeCompareSequence
        }
        test "Generic set" {
            Set.ofList [ {| something = 588854245464513.2465; somethnigElse = 58.24f |} ] |> serializeDeserializeCompare
            Set.ofList [ {| something = 5; somethnigElse = 10 |}; {| something = 5; somethnigElse = 11 |} ] |> serializeDeserializeCompare
            Set.ofList [ {| something = 6; somethnigElse = 10 |}; {| something = 5; somethnigElse = 10 |} ] |> serializeDeserializeCompare
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
            [| for i in 1L .. 80_000L -> 5_000_000_000L * (if i % 2L = 0L then -1L else 1L) |] |> serializeDeserializeCompare
        }
        test "Array32 of int32" {
            [| -100000 .. 100000 |] |> serializeDeserializeCompare
        }
        test "Array32 of uint32" {
            [| 0u .. 200000u |] |> serializeDeserializeCompare
        }
        test "Array of single" {
            [| Single.Epsilon; Single.MaxValue; Single.MinValue; Single.PositiveInfinity; Single.NegativeInfinity |] |> serializeDeserializeCompare
            [| for i in -30_000 .. 30_000 -> float32 i * 10.356f |] |> serializeDeserializeCompare
        }
        test "Array of double" {
            [| Double.Epsilon; Double.MaxValue; Double.MinValue; Double.PositiveInfinity; Double.NegativeInfinity |] |> serializeDeserializeCompare
            [| for i in -30_000 .. 30_000 -> float i * 100.300056 |] |> serializeDeserializeCompare
        }
        test "Recursive record" {
            {
                Name = "root"
                Result = Ok 2
                Children = [
                    { Name = "Child 1"; Result = Result.Error null; Children = [ { Name = "Grandchild"; Result = Ok -50; Children = [ ] } ] }
                    { Name = "Child 1"; Result = Result.Error "ss"; Children = [ ] }
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
        test "null string" {
            (null: string) |> serializeDeserializeCompare
        }
        test "Array of 3-tuples" {
            [| (1L, ":)", DateTime.Now); (4L, ":<", DateTime.Now) |] |> serializeDeserializeCompare
        }
        test "datatable" {
            let t = new System.Data.DataTable()
            t.TableName <- "myname"
            t.Columns.Add("a", typeof<int>) |> ignore
            t.Columns.Add("b", typeof<string>) |> ignore
            t.Rows.Add(1, "11111")  |> ignore
            t.Rows.Add(2, "222222") |> ignore
            use ms = new MemoryStream ()
            MsgPack.Write.serializeObj t ms

            let deserialized = MsgPack.Read.Reader(ms.ToArray ()).Read typeof<System.Data.DataTable> :?> System.Data.DataTable
            Expect.equal deserialized.Columns.Count   t.Columns.Count  "column count"
            Expect.equal deserialized.Rows.Count      t.Rows.Count     "row count"
            Expect.equal deserialized.TableName       t.TableName      "table name"
            Expect.equal deserialized.Rows.[0].["a"]  t.Rows.[0].["a"] "table.[0,'a']"
            Expect.equal deserialized.Rows.[0].["b"]  t.Rows.[0].["b"] "table.[0,'b']"
            Expect.equal deserialized.Rows.[1].["a"]  t.Rows.[1].["a"] "table.[1,'a']"
            Expect.equal deserialized.Rows.[1].["b"]  t.Rows.[1].["b"] "table.[1,'b']"
        }
        test "dataset" {
            let t = new System.Data.DataTable()
            t.TableName <- "myname"
            t.Columns.Add("a", typeof<int>) |> ignore
            t.Columns.Add("b", typeof<string>) |> ignore
            t.Rows.Add(1, "11111")  |> ignore
            t.Rows.Add(2, "222222") |> ignore
            let ds = new System.Data.DataSet()
            ds.Tables.Add t
            use ms = new MemoryStream ()
            MsgPack.Write.serializeObj ds ms

            let deserialized = MsgPack.Read.Reader(ms.ToArray ()).Read typeof<System.Data.DataSet> :?> System.Data.DataSet
            Expect.equal deserialized.Tables.["myname"].Columns.Count   t.Columns.Count  "column count"
            Expect.equal deserialized.Tables.["myname"].Rows.Count      t.Rows.Count     "row count"
            Expect.equal deserialized.Tables.["myname"].TableName       t.TableName      "table name"
            Expect.equal deserialized.Tables.["myname"].Rows.[0].["a"]  t.Rows.[0].["a"] "table.[0,'a']"
            Expect.equal deserialized.Tables.["myname"].Rows.[0].["b"]  t.Rows.[0].["b"] "table.[0,'b']"
            Expect.equal deserialized.Tables.["myname"].Rows.[1].["a"]  t.Rows.[1].["a"] "table.[1,'a']"
            Expect.equal deserialized.Tables.["myname"].Rows.[1].["b"]  t.Rows.[1].["b"] "table.[1,'b']"
        }
        test "Chars" {
            'q' |> serializeDeserializeCompare
            'ψ' |> serializeDeserializeCompare
            '☃' |> serializeDeserializeCompare
            "☠️".[0] |> serializeDeserializeCompare
        }
        test "Bytes" {
            0uy |> serializeDeserializeCompare
            0y |> serializeDeserializeCompare
            255uy |> serializeDeserializeCompare
            100y |> serializeDeserializeCompare
            -100y |> serializeDeserializeCompare
            -5y |> serializeDeserializeCompare
            [| 0uy; 255uy; 100uy; 5uy |] |> serializeDeserializeCompare
            [| 0y; 100y; -100y; -5y |] |> serializeDeserializeCompare
        }
        test "Units of measure" {
            85<SomeUnit> |> serializeDeserializeCompareWithLength 1
            85L<SomeUnit> |> serializeDeserializeCompareWithLength 1
            -85L<SomeUnit> |> serializeDeserializeCompareWithLength 9
            32313213121.1415926535m<SomeUnit> |> serializeDeserializeCompareWithLength 18
            80005.44f<SomeUnit> |> serializeDeserializeCompareWithLength 5
            80000000000005.445454<SomeUnit> |> serializeDeserializeCompareWithLength 9
        }
    ]