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

type SimpleRecord = {
    A: string
    B: int }

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

let simpleRecordArray = [| for i in 1 .. 100_000 -> { A = "blaah"; B = 20000 } |]

let arrayOfFloatArray = [| for i in 1 .. 500 -> [| for j = 250 downto -250 do float j / float i |] |]

let fableConverter = FableJsonConverter ()

let jsonSerialize value = JsonConvert.SerializeObject (value, fableConverter)
let msgPackSerializeToArray value =
    use ms = new MemoryStream ()
    MsgPack.Write.serializeObj value ms
    ms.ToArray ()

// a large buffer to be reused for msgpack, because I am not interested in allocations caused by expanding the memory stream
// this puts json serialization at a very slight disadvantage
let ms = new MemoryStream (20_000_000)

let jsonDeserialize<'a> text = JsonConvert.DeserializeObject<'a> (text, fableConverter)
let msgPackDeserialize typ data = MsgPack.Read.Reader(data).Read typ

[<MemoryDiagnoser>]
type SimpleRecordArraySerialization () =
    let msgPackSerializeManual (out: Stream) (xs: SimpleRecord[]) =
        MsgPack.Write.writeArrayHeader xs.Length out
    
        for i in 0 .. xs.Length - 1 do
            let x = xs.[i]
            out.WriteByte (MsgPack.Format.fixarr 2uy)
            MsgPack.Write.writeString x.A out
            MsgPack.Write.writeInt64 (int64 x.B) out

    [<Benchmark>]
    member _.Json () =
        jsonSerialize simpleRecordArray

    [<Benchmark>]
    member _.MsgPack () =
        ms.Position <- 0L
        MsgPack.Write.serializeObj simpleRecordArray ms

    [<Benchmark>]
    member _.Manual () =
        ms.Position <- 0L
        msgPackSerializeManual ms simpleRecordArray

[<MemoryDiagnoser>]
type ArrayOfFloatArraySerialization () =
    let msgPackSerialize = MsgPack.Write.makeSerializerObj typeof<float[][]>

    let msgPackSerializeManual (out: Stream) (xs: float[][]) =
        MsgPack.Write.writeArrayHeader xs.Length out
        for i in 0 .. xs.Length - 1 do
            let x = xs.[i]
            
            MsgPack.Write.writeArrayHeader x.Length out
            for j in 0 .. x.Length - 1 do
                MsgPack.Write.writeDouble x.[j] out

    [<Benchmark>]
    member _.Json () =
        jsonSerialize arrayOfFloatArray

    [<Benchmark>]
    member _.MsgPack () =
        ms.Position <- 0L
        msgPackSerialize.Invoke (arrayOfFloatArray, ms)

    [<Benchmark>]
    member _.Manual () =
        ms.Position <- 0L
        msgPackSerializeManual ms arrayOfFloatArray

[<MemoryDiagnoser>]
type RecursiveRecordSerialization () =
    let rec msgPackSerializeManual (out: Stream) (x: RecursiveRecord) =
        out.WriteByte (MsgPack.Format.fixarr 3uy)
        MsgPack.Write.writeString x.Name out
        MsgPack.Write.writeInt64 (int64 x.Number) out
        MsgPack.Write.writeArrayHeader x.Children.Length out
    
        for x in x.Children do
            msgPackSerializeManual out x

    [<Benchmark>]
    member _.Json () =
        jsonSerialize recursiveRecord

    [<Benchmark>]
    member _.MsgPack () =
        ms.Position <- 0L
        MsgPack.Write.serializeObj recursiveRecord ms

    [<Benchmark>]
    member _.Manual () =
        ms.Position <- 0L
        msgPackSerializeManual ms recursiveRecord

[<MemoryDiagnoser>]
type RecursiveRecordDeserialization () =
    let json = jsonSerialize recursiveRecord
    let binary = msgPackSerializeToArray recursiveRecord

    [<Benchmark>]
    member _.Json () =
        jsonDeserialize<RecursiveRecord> json

    [<Benchmark>]
    member _.MsgPack () =
        msgPackDeserialize typeof<RecursiveRecord> binary

[<MemoryDiagnoser>]
type IntMaybeMapSerialization () =
    let msgPackSerializeManual (out: Stream) (map: Map<int, Maybe<string>>) =
        MsgPack.Write.writeMapHeader map.Count out

        map |> Map.iter (fun k v ->
            MsgPack.Write.writeInt64 (int64 k) out

            match v with
            | Nothing ->
                out.WriteByte (MsgPack.Format.fixarr 2uy)
                out.WriteByte (MsgPack.Format.fixposnum 1uy)
                out.WriteByte (MsgPack.Format.fixarr 0uy)
            | Just x ->
                out.WriteByte (MsgPack.Format.fixarr 2uy)
                out.WriteByte (MsgPack.Format.fixposnum 0uy)
                MsgPack.Write.writeString x out)

    [<Benchmark>]
    member _.Json () =
        jsonSerialize intMaybeMap

    [<Benchmark>]
    member _.MsgPack () =
        ms.Position <- 0L
        MsgPack.Write.serializeObj intMaybeMap ms

    [<Benchmark>]
    member _.Manual () =
        ms.Position <- 0L
        msgPackSerializeManual ms intMaybeMap

[<MemoryDiagnoser>]
type IntMaybeMapDeserialization () =
    let json = jsonSerialize intMaybeMap
    let binary = msgPackSerializeToArray intMaybeMap

    [<Benchmark>]
    member _.Json () =
        jsonDeserialize<Map<int, Maybe<string>>> json

    [<Benchmark>]
    member _.MsgPack () =
        msgPackDeserialize typeof<Map<int, Maybe<string>>> binary

[<MemoryDiagnoser>]
type Int64ArraySerialization () =
    let msgPackSerializeManual (out: Stream) (xs: int64[]) =
        MsgPack.Write.writeArrayHeader xs.Length out
        for x in xs do
            MsgPack.Write.writeInt64 x out

    [<Benchmark>]
    member _.Json () =
        jsonSerialize int64Array

    [<Benchmark>]
    member _.MsgPack () =
        ms.Position <- 0L
        MsgPack.Write.serializeObj int64Array ms

    [<Benchmark>]
    member _.Manual () =
        ms.Position <- 0L
        msgPackSerializeManual ms int64Array

[<MemoryDiagnoser>]
type Int64ArrayDeserialization () =
    let json = jsonSerialize int64Array
    let binary = msgPackSerializeToArray int64Array

    [<Benchmark>]
    member _.Json () =
        jsonDeserialize<int64[]> json

    [<Benchmark>]
    member _.MsgPack () =
        msgPackDeserialize typeof<int64[]> binary