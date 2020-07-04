module Serialization

open BenchmarkDotNet.Attributes
open Fable.Remoting.Json
open Newtonsoft.Json
open Fable.Remoting
open System.IO

type RecursiveRecord = {
    Name: string
    Number: int
    Children: RecursiveRecord list
}

type Maybe<'t> =
    | Just of 't
    | Nothing

let rec createRecursiveRecord childCount levels =
    if levels > 0 then
        let children = [ 1 .. childCount ] |> List.map (fun _ -> createRecursiveRecord childCount (levels - 1))
        { Name = "Test name"; Number = levels * childCount; Children = children }
    else
        { Name = "Leaf"; Number = levels * childCount; Children = [] }

let recursiveRecord = createRecursiveRecord 5 8

let intMaybeMap = [ for i in 1 .. 20_000 -> i, (if i % 2 = 0 then Nothing else Just "teeeest") ] |> Map.ofList

let int64Array = [| for i in 10_000_000L .. 10_100_000L -> i * 1000L |]

let fableConverter = FableJsonConverter ()

let jsonSerialize value = JsonConvert.SerializeObject (value, fableConverter)
let msgPackSerialize value =
    let ms = new MemoryStream ()
    MsgPack.Write.object value ms
    ms

let jsonDeserialize<'a> text = JsonConvert.DeserializeObject<'a> (text, fableConverter)
let msgPackDeserialize typ data = MsgPack.Reader(data).Read typ

[<MemoryDiagnoser>]
type RecursiveRecordSerialization () =
    [<Benchmark>]
    member _.Json () =
        jsonSerialize recursiveRecord

    [<Benchmark>]
    member _.MsgPack () =
        msgPackSerialize recursiveRecord

[<MemoryDiagnoser>]
type RecursiveRecordDeserialization () =
    let json = jsonSerialize recursiveRecord
    let binary = (msgPackSerialize recursiveRecord).ToArray ()
    let binary' = Array.copy binary

    [<Benchmark>]
    member _.Json () =
        jsonDeserialize<RecursiveRecord> json

    [<IterationSetup(Target = "MsgPack")>]
    member _.ResetMsgPackInput () =
        binary'.CopyTo (binary, 0)

    [<Benchmark>]
    member _.MsgPack () =
        binary'.CopyTo (binary, 0)
        msgPackDeserialize typeof<RecursiveRecord> binary

[<MemoryDiagnoser>]
type IntMaybeMapSerialization () =
    [<Benchmark>]
    member _.Json () =
        jsonSerialize intMaybeMap

    [<Benchmark>]
    member _.MsgPack () =
        msgPackSerialize intMaybeMap

[<MemoryDiagnoser>]
type IntMaybeMapDeserialization () =
    let json = jsonSerialize intMaybeMap
    let binary = (msgPackSerialize intMaybeMap).ToArray ()
    let binary' = Array.copy binary

    [<Benchmark>]
    member _.Json () =
        jsonDeserialize<Map<int, Maybe<string>>> json

    [<IterationSetup(Target = "MsgPack")>]
    member _.ResetMsgPackInput () =
        binary'.CopyTo (binary, 0)

    [<Benchmark>]
    member _.MsgPack () =
        binary'.CopyTo (binary, 0)
        msgPackDeserialize typeof<Map<int, Maybe<string>>> binary

[<MemoryDiagnoser>]
type Int64ArraySerialization () =
    [<Benchmark>]
    member _.Json () =
        jsonSerialize int64Array

    [<Benchmark>]
    member _.MsgPack () =
        msgPackSerialize int64Array

[<MemoryDiagnoser>]
type Int64ArrayDeserialization () =
    let json = jsonSerialize int64Array
    let binary = (msgPackSerialize int64Array).ToArray ()
    let binary' = Array.copy binary

    [<Benchmark>]
    member _.Json () =
        jsonDeserialize<int64[]> json

    [<IterationSetup(Target = "MsgPack")>]
    member _.ResetMsgPackInput () =
        binary'.CopyTo (binary, 0)

    [<Benchmark>]
    member _.MsgPack () =
        binary'.CopyTo (binary, 0)
        msgPackDeserialize typeof<int64[]> binary