module Serialization

open BenchmarkDotNet.Attributes
open Fable.Remoting.Json
open Newtonsoft.Json
open Fable.Remoting
open System.IO
open BenchmarkDotNet.Configs

type RecursiveRecord = {
    Name: string
    Number: int
    Children: RecursiveRecord list
}

let rec createRecursiveRecord childCount levels =
    if levels > 0 then
        let children = [ 1 .. childCount ] |> List.map (fun _ -> createRecursiveRecord childCount (levels - 1))
        { Name = "Test name"; Number = levels * childCount; Children = children }
    else
        { Name = "Leaf"; Number = levels * childCount; Children = [] }

let recursiveRecord = createRecursiveRecord 3 8

let fableConverter = FableJsonConverter ()

let jsonSerialize value = JsonConvert.SerializeObject (value, fableConverter)
let msgPackSerialize value =
    let ms = new MemoryStream ()
    MsgPack.Write.write value ms
    ms

let jsonDeserialize<'a> text = JsonConvert.DeserializeObject<'a> (text, fableConverter)
let msgPackDeserialize typ data = MsgPack.Reader(data).Read typ

let jsonText = jsonSerialize recursiveRecord
let msgPackBinary = (msgPackSerialize recursiveRecord).ToArray ()

[<GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)>]
[<CategoriesColumn>]
[<MemoryDiagnoser>]
type Test () =
    [<BenchmarkCategory("Recursive record serialize"); Benchmark(Baseline = true)>]
    member _.RecursiveRecordSerializeJson () =
        jsonSerialize recursiveRecord

    [<BenchmarkCategory("Recursive record serialize"); Benchmark>]
    member _.RecursiveRecordSerializeMsgPack () =
        msgPackSerialize recursiveRecord

    [<BenchmarkCategory("Recursive record deserialize"); Benchmark(Baseline = true)>]
    member _.RecursiveRecordDeserializeJson () =
        jsonDeserialize<RecursiveRecord> jsonText

    [<BenchmarkCategory("Recursive record deserialize"); Benchmark>]
    member _.RecursiveRecordDeserializeMsgPack () =
        msgPackDeserialize typeof<RecursiveRecord> msgPackBinary