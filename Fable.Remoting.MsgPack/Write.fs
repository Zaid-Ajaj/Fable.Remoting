module Fable.Remoting.MsgPack.Write

open System.Collections.Concurrent
open System.IO
open System
open System.Collections.Generic
open System.Text
open FSharp.Reflection
open System.Numerics

#if FABLE_COMPILER
let packerCache = ConcurrentDictionary<Type, obj -> Stream -> unit> ()
let unionCaseFieldReaderCache = ConcurrentDictionary<Type, obj -> obj[]> ()

let inline write32bitNumber b1 b2 b3 b4 (out: Stream) writeFormat =
    if b2 > 0uy || b1 > 0uy then
        if writeFormat then out.WriteByte Format.Uint32
        out.WriteByte b1
        out.WriteByte b2
        out.WriteByte b3
        out.WriteByte b4
    elif (b3 > 0uy) then
        if writeFormat then out.WriteByte Format.Uint16
        out.WriteByte b3
        out.WriteByte b4
    else
        if writeFormat then out.WriteByte Format.Uint8
        out.WriteByte b4

let write64bitNumber b1 b2 b3 b4 b5 b6 b7 b8 (out: Stream) =
    if b4 > 0uy || b3 > 0uy || b2 > 0uy || b1 > 0uy then
        out.WriteByte Format.Uint64
        out.WriteByte b1
        out.WriteByte b2
        out.WriteByte b3
        out.WriteByte b4
        write32bitNumber b5 b6 b7 b8 out false
    else
        write32bitNumber b5 b6 b7 b8 out true

let inline writeUnsigned32bitNumber (n: UInt32) (out: Stream) =
    write32bitNumber (n >>> 24 |> byte) (n >>> 16 |> byte) (n >>> 8 |> byte) (byte n) out

let inline writeUnsigned64bitNumber (n: UInt64) (out: Stream) =
    write64bitNumber (n >>> 56 |> byte) (n >>> 48 |> byte) (n >>> 40 |> byte) (n >>> 32 |> byte) (n >>> 24 |> byte) (n >>> 16 |> byte) (n >>> 8 |> byte) (byte n) out
  
type DictionarySerializer<'k,'v> () =
    static member Serialize (dict: obj, out: obj, write: obj) =
        let dict = dict :?> IDictionary<'k,'v>
        let out = out :?> Stream
        let write = write :?> (obj -> Stream -> unit)

        let length = dict.Count

        if length < 16 then
            out.WriteByte (Format.fixmap length)
        elif length < 65536 then
            out.WriteByte Format.Map16
            out.WriteByte (length >>> 8 |> byte)
            out.WriteByte (byte length)
        else
            out.WriteByte Format.Map32
            writeUnsigned32bitNumber (uint32 length) out false

        for kvp in dict do
            write kvp.Key out
            write kvp.Value out

type ListSerializer<'a> () =
    static member Serialize (list: obj, out: obj, write: obj) =
        let list = list :?> 'a list
        let out = out :?> Stream
        let write = write :?> (obj -> Stream -> unit)

        let length = list.Length

        if length < 16 then
            out.WriteByte (Format.fixarr length)
        elif list.Length < 65536 then
            out.WriteByte Format.Array16
            out.WriteByte (length >>> 8 |> byte)
            out.WriteByte (byte length)
        else
            out.WriteByte Format.Array32
            writeUnsigned32bitNumber (uint32 length) out false

        for x in list do
            write x out

let inline nil (out: Stream) = out.WriteByte Format.Nil
let inline bool x (out: Stream) = out.WriteByte (if x then Format.True else Format.False)

let inline writeSignedNumber bytes (out: Stream) =
    if BitConverter.IsLittleEndian then
        Array.Reverse bytes

    out.Write (bytes, 0, bytes.Length)

let inline uint (n: UInt64) (out: Stream) =
    if n < 128UL then
        out.WriteByte (Format.fixposnum n)
    else
        writeUnsigned64bitNumber n out

let inline int (n: int64) (out: Stream) =
    if n >= 0L then
        uint (uint64 n) out 
    else
        if n > -32L then
            out.WriteByte (Format.fixnegnum n)
        else
            //todo length optimization
            out.WriteByte Format.Int64
            writeSignedNumber (BitConverter.GetBytes n) out

let inline byte b (out: Stream) =
    out.WriteByte b

let inline str (str: string) (out: Stream) =
    let str = Encoding.UTF8.GetBytes str

    if str.Length < 32 then
        out.WriteByte (Format.fixstr str.Length)
    else
        if str.Length < 256 then
            out.WriteByte Format.Str8
        elif str.Length < 65536 then
            out.WriteByte Format.Str16
        else
            out.WriteByte Format.Str32

        writeUnsigned32bitNumber (uint32 str.Length) out false

    out.Write (str, 0, str.Length)

let inline float32 (n: float32) (out: Stream) =
    out.WriteByte Format.Float32
    writeSignedNumber (BitConverter.GetBytes n) out
    
let inline float64 (n: float) (out: Stream) =
    out.WriteByte Format.Float64
    writeSignedNumber (BitConverter.GetBytes n) out

let bin (data: byte[]) (out: Stream) =
    if data.Length < 256 then
        out.WriteByte Format.Bin8
    elif data.Length < 65536 then
        out.WriteByte Format.Bin16
    else
        out.WriteByte Format.Bin32

    writeUnsigned32bitNumber (uint32 data.Length) out false

    use sw = new MemoryStream (data)
    sw.CopyTo out

let inline dateTimeOffset (out: Stream) (dto: DateTimeOffset) =
    out.WriteByte (Format.fixarr 2uy)
    int dto.Ticks out
    int (int64 dto.Offset.TotalMinutes) out

let rec array (out: Stream) (arr: System.Collections.ICollection) =
    if arr.Count < 16 then
        out.WriteByte (Format.fixarr arr.Count)
    elif arr.Count < 65536 then
        out.WriteByte Format.Array16
        out.WriteByte (arr.Count >>> 8 |> FSharp.Core.Operators.byte)
        out.WriteByte (FSharp.Core.Operators.byte arr.Count)
    else
        out.WriteByte Format.Array32
        writeUnsigned32bitNumber (uint32 arr.Count) out false

    for x in arr do
        object x out

and inline tuple (out: Stream) (items: obj[]) =
    array out items

and union (out: Stream) tag (vals: obj[]) =
    out.WriteByte (Format.fixarr 2uy)
    out.WriteByte (Format.fixposnum tag)

    // save 1 byte if the union case has a single parameter
    if vals.Length <> 1 then
        array out vals
    else
        object vals.[0] out

and object (x: obj) (out: Stream) =
    if isNull x then nil out else

    let t = x.GetType()

    match packerCache.TryGetValue (if t.IsArray && t <> typeof<byte[]> then typeof<Array> else t) with
    | true, writer ->
        writer x out
    | _ ->
        if FSharpType.IsRecord (t, true) then
            let fieldReader = FSharpValue.PreComputeRecordReader (t, true)
            packerCache.GetOrAdd (t, fun x (out: Stream) -> fieldReader x |> array out) x out
        elif t.CustomAttributes |> Seq.exists (fun x -> x.AttributeType.Name = "StringEnumAttribute") then
            packerCache.GetOrAdd (t, fun x (out: Stream) ->
                //todo cacheable
                let case, _ = FSharpValue.GetUnionFields (x, t, true)
                //todo when overriden with CompiledName
                str (sprintf "%c%s" (Char.ToLowerInvariant case.Name.[0]) (case.Name.Substring 1)) out) x out
        elif FSharpType.IsUnion (t, true)  then
            if t.IsGenericType && t.GetGenericTypeDefinition () = typedefof<_ list> then
                let listType = t.GetGenericArguments () |> Array.head
                let listSerializer = typedefof<ListSerializer<_>>.MakeGenericType listType
                let d = Delegate.CreateDelegate (typeof<Action<obj, obj, obj>>, listSerializer.GetMethod "Serialize") :?> Action<obj, obj, obj>
                
                packerCache.GetOrAdd (t, fun x out -> d.Invoke (x, out, object)) x out
            else
                let tagReader = FSharpValue.PreComputeUnionTagReader (t, true)
                let cases = FSharpType.GetUnionCases (t, true)
                let fieldReaders = cases |> Array.map (fun c -> c.Tag, FSharpValue.PreComputeUnionReader (c, true))

                packerCache.GetOrAdd (t, fun x out ->
                    let tag = tagReader x
                    let fieldReader = fieldReaders |> Array.find (fun (tag', _) -> tag = tag') |> snd
                    let fieldValues = fieldReader x
                    union out tag fieldValues) x out
        elif FSharpType.IsTuple t then
            let tupleReader = FSharpValue.PreComputeTupleReader t
            packerCache.GetOrAdd (t, fun x out -> tupleReader x |> tuple out) x out
        elif t.IsGenericType && List.contains (t.GetGenericTypeDefinition ()) [ typedefof<Dictionary<_, _>>; typedefof<Map<_, _>> ] then
            let mapTypes = t.GetGenericArguments ()
            let mapSerializer = typedefof<DictionarySerializer<_,_>>.MakeGenericType mapTypes
            let d = Delegate.CreateDelegate (typeof<Action<obj, obj, obj>>, mapSerializer.GetMethod "Serialize") :?> Action<obj, obj, obj>
            
            packerCache.GetOrAdd (t, fun x out -> d.Invoke (x, out, object)) x out
        elif t.IsEnum then
            packerCache.GetOrAdd (t, fun x -> object (Convert.ChangeType (x, typeof<int64>) :?> int64)) x out
        else
            failwithf "Cannot pack %s" t.Name

packerCache.TryAdd (typeof<byte>, fun x out -> byte (x :?> byte) out) |> ignore
packerCache.TryAdd (typeof<sbyte>, fun x out -> int (x :?> sbyte |> int64) out) |> ignore
packerCache.TryAdd (typeof<unit>, fun _ out -> nil out) |> ignore
packerCache.TryAdd (typeof<bool>, fun x out -> bool (x :?> bool) out) |> ignore
packerCache.TryAdd (typeof<string>, fun x out -> str (x :?> string) out) |> ignore
packerCache.TryAdd (typeof<int>, fun x out -> int (x :?> int |> int64) out) |> ignore
packerCache.TryAdd (typeof<int16>, fun x out -> int (x :?> int16 |> int64) out) |> ignore
packerCache.TryAdd (typeof<int64>, fun x out -> int (x :?> int64) out) |> ignore
packerCache.TryAdd (typeof<UInt32>, fun x out -> uint (x :?> UInt32 |> uint64) out) |> ignore
packerCache.TryAdd (typeof<UInt16>, fun x out -> uint (x :?> UInt16 |> uint64) out) |> ignore
packerCache.TryAdd (typeof<UInt64>, fun x out -> uint (x :?> UInt64) out) |> ignore
packerCache.TryAdd (typeof<float32>, fun x out -> float32 (x :?> float32) out) |> ignore
packerCache.TryAdd (typeof<float>, fun x out -> float64 (x :?> float) out) |> ignore
packerCache.TryAdd (typeof<decimal>, fun x out -> float64 (x :?> decimal |> float) out) |> ignore
packerCache.TryAdd (typeof<Array>, fun x out -> array out (x :?> Array)) |> ignore
packerCache.TryAdd (typeof<byte[]>, fun x out -> bin (x :?> byte[]) out) |> ignore
packerCache.TryAdd (typeof<BigInteger>, fun x out -> bin ((x :?> BigInteger).ToByteArray ()) out) |> ignore
packerCache.TryAdd (typeof<Guid>, fun x out -> bin ((x :?> Guid).ToByteArray ()) out) |> ignore
packerCache.TryAdd (typeof<DateTime>, fun x out -> int (x :?> DateTime).Ticks out) |> ignore
packerCache.TryAdd (typeof<DateTimeOffset>, fun x out -> dateTimeOffset out (x :?> DateTimeOffset)) |> ignore
packerCache.TryAdd (typeof<TimeSpan>, fun x out -> int (x :?> TimeSpan).Ticks out) |> ignore

#else

let packerCache = Dictionary<Type, obj -> ResizeArray<byte> -> unit> ()

let cacheGetOrAdd (typ, f) =
    match packerCache.TryGetValue typ with
    | true, f -> f
    | _ ->
        packerCache.Add (typ, f)
        f

let inline write32bitNumber b1 b2 b3 b4 (out: ResizeArray<byte>) writeFormat =
    if b2 > 0uy || b1 > 0uy then
        if writeFormat then out.Add Format.Uint32
        out.Add b1
        out.Add b2
        out.Add b3
        out.Add b4
    elif (b3 > 0uy) then
        if writeFormat then out.Add Format.Uint16
        out.Add b3
        out.Add b4
    else
        if writeFormat then out.Add Format.Uint8
        out.Add b4

let write64bitNumber b1 b2 b3 b4 b5 b6 b7 b8 (out: ResizeArray<byte>) =
    if b4 > 0uy || b3 > 0uy || b2 > 0uy || b1 > 0uy then
        out.Add Format.Uint64
        out.Add b1
        out.Add b2
        out.Add b3
        out.Add b4
        write32bitNumber b5 b6 b7 b8 out false
    else
        write32bitNumber b5 b6 b7 b8 out true

let inline writeUnsigned32bitNumber (n: UInt32) (out: ResizeArray<byte>) =
    write32bitNumber (n >>> 24 |> byte) (n >>> 16 |> byte) (n >>> 8 |> byte) (byte n) out

let inline writeUnsigned64bitNumber (n: UInt64) (out: ResizeArray<byte>) =
    write64bitNumber (n >>> 56 |> byte) (n >>> 48 |> byte) (n >>> 40 |> byte) (n >>> 32 |> byte) (n >>> 24 |> byte) (n >>> 16 |> byte) (n >>> 8 |> byte) (byte n) out
 
let inline nil (out: ResizeArray<byte>) = out.Add Format.Nil
let inline bool x (out: ResizeArray<byte>) = out.Add (if x then Format.True else Format.False)

let inline writeSignedNumber bytes (out: ResizeArray<byte>) =
    if BitConverter.IsLittleEndian then
        Array.Reverse bytes

    out.AddRange bytes

let inline uint (n: UInt64) (out: ResizeArray<byte>) =
    if n < 128UL then
        out.Add (Format.fixposnum n)
    else
        writeUnsigned64bitNumber n out

let inline int (n: int64) (out: ResizeArray<byte>) =
    if n >= 0L then
        uint (uint64 n) out 
    else
        if n > -32L then
            out.Add (Format.fixnegnum n)
        else
            //todo length optimization
            out.Add Format.Int64
            writeSignedNumber (BitConverter.GetBytes n) out

let inline byte b (out: ResizeArray<byte>) =
    out.Add b

let inline str (str: string) (out: ResizeArray<byte>) =
    let str = Encoding.UTF8.GetBytes str

    if str.Length < 32 then
        out.Add (Format.fixstr str.Length)
    else
        if str.Length < 256 then
            out.Add Format.Str8
        elif str.Length < 65536 then
            out.Add Format.Str16
        else
            out.Add Format.Str32

        writeUnsigned32bitNumber (uint32 str.Length) out false

    out.AddRange str

let inline float32 (n: float32) (out: ResizeArray<byte>) =
    out.Add Format.Float32
    writeSignedNumber (BitConverter.GetBytes n) out
    
let inline float64 (n: float) (out: ResizeArray<byte>) =
    out.Add Format.Float64
    writeSignedNumber (BitConverter.GetBytes n) out

let bin (data: byte[]) (out: ResizeArray<byte>) =
    if data.Length < 256 then
        out.Add Format.Bin8
    elif data.Length < 65536 then
        out.Add Format.Bin16
    else
        out.Add Format.Bin32

    writeUnsigned32bitNumber (uint32 data.Length) out false

    out.AddRange data

let inline dateTimeOffset (out: ResizeArray<byte>) (dto: DateTimeOffset) =
    out.Add (Format.fixarr 2uy)
    int dto.Ticks out
    int (int64 dto.Offset.TotalMinutes) out

let rec array (out: ResizeArray<byte>) (arr: System.Collections.ICollection) =
    if arr.Count < 16 then
        out.Add (Format.fixarr arr.Count)
    elif arr.Count < 65536 then
        out.Add Format.Array16
        out.Add (arr.Count >>> 8 |> FSharp.Core.Operators.byte)
        out.Add (FSharp.Core.Operators.byte arr.Count)
    else
        out.Add Format.Array32
        writeUnsigned32bitNumber (uint32 arr.Count) out false

    for x in arr do
        object x out

and inline tuple (out: ResizeArray<byte>) (items: obj[]) =
    array out items

and union (out: ResizeArray<byte>) tag (vals: obj[]) =
    out.Add (Format.fixarr 2uy)
    out.Add (Format.fixposnum tag)

    // save 1 byte if the union case has a single parameter
    if vals.Length <> 1 then
        array out vals
    else
        object vals.[0] out

and object (x: obj) (out: ResizeArray<byte>) =
    if isNull x then nil out else

    let t = x.GetType()

    match packerCache.TryGetValue (if t.IsArray && t <> typeof<byte[]> then typeof<Array> else t) with
    | true, writer ->
        writer x out
    | _ ->
        if FSharpType.IsRecord (t, true) then
            cacheGetOrAdd (t, fun x out -> FSharpValue.GetRecordFields (x, true) |> array out) x out
        elif t.CustomAttributes |> Seq.exists (fun x -> x.AttributeType.Name = "StringEnumAttribute") then
            cacheGetOrAdd (t, fun x out ->
                //todo cacheable
                let case, _ = FSharpValue.GetUnionFields (x, t, true)
                //todo when overriden with CompiledName
                str (sprintf "%c%s" (Char.ToLowerInvariant case.Name.[0]) (case.Name.Substring 1)) out) x out
        elif FSharpType.IsUnion (t, true)  then
            if t.IsGenericType && t.GetGenericTypeDefinition () = typedefof<_ list> then
                let listType = t.GetGenericArguments () |> Array.head
                let listSerializer = typedefof<ListSerializer<_>>.MakeGenericType listType
                let d = Delegate.CreateDelegate (typeof<Action<obj, obj, obj>>, listSerializer.GetMethod "Serialize") :?> Action<obj, obj, obj>
                
                cacheGetOrAdd (t, fun x out -> d.Invoke (x, out, object)) x out
            else
                cacheGetOrAdd (t, fun x out ->
                    let case, fields = FSharpValue.GetUnionFields (x, t, true)
                    union out case.Tag fields) x out
        elif FSharpType.IsTuple t then
            cacheGetOrAdd (t, fun x out -> FSharpValue.GetTupleFields x |> tuple out) x out
        elif t.IsGenericType && List.contains (t.GetGenericTypeDefinition ()) [ typedefof<Dictionary<_, _>>; typedefof<Map<_, _>> ] then
            let mapTypes = t.GetGenericArguments ()
            let mapSerializer = typedefof<DictionarySerializer<_,_>>.MakeGenericType mapTypes
            let d = Delegate.CreateDelegate (typeof<Action<obj, obj, obj>>, mapSerializer.GetMethod "Serialize") :?> Action<obj, obj, obj>
            
            cacheGetOrAdd (t, fun x out -> d.Invoke (x, out, object)) x out
        elif t.IsEnum then
            cacheGetOrAdd (t, fun x -> object (Convert.ChangeType (x, typeof<int64>) :?> int64)) x out
        else
            failwithf "Cannot pack %s" t.Name

packerCache.Add (typeof<byte>, fun x out -> byte (x :?> byte) out)
packerCache.Add (typeof<sbyte>, fun x out -> int (x :?> sbyte |> int64) out)
packerCache.Add (typeof<unit>, fun _ out -> nil out)
packerCache.Add (typeof<bool>, fun x out -> bool (x :?> bool) out)
packerCache.Add (typeof<string>, fun x out -> str (x :?> string) out)
packerCache.Add (typeof<int>, fun x out -> int (x :?> int |> int64) out)
packerCache.Add (typeof<int16>, fun x out -> int (x :?> int16 |> int64) out)
packerCache.Add (typeof<int64>, fun x out -> int (x :?> int64) out)
packerCache.Add (typeof<UInt32>, fun x out -> uint (x :?> UInt32 |> uint64) out)
packerCache.Add (typeof<UInt16>, fun x out -> uint (x :?> UInt16 |> uint64) out)
packerCache.Add (typeof<UInt64>, fun x out -> uint (x :?> UInt64) out)
packerCache.Add (typeof<float32>, fun x out -> float32 (x :?> float32) out)
packerCache.Add (typeof<float>, fun x out -> float64 (x :?> float) out)
packerCache.Add (typeof<decimal>, fun x out -> float64 (x :?> decimal |> float) out)
packerCache.Add (typeof<Array>, fun x out -> array out (x :?> Array))
packerCache.Add (typeof<byte[]>, fun x out -> bin (x :?> byte[]) out)
packerCache.Add (typeof<BigInteger>, fun x out -> bin ((x :?> BigInteger).ToByteArray ()) out)
packerCache.Add (typeof<Guid>, fun x out -> bin ((x :?> Guid).ToByteArray ()) out)
packerCache.Add (typeof<DateTime>, fun x out -> int (x :?> DateTime).Ticks out)
packerCache.Add (typeof<DateTimeOffset>, fun x out -> dateTimeOffset out (x :?> DateTimeOffset))
packerCache.Add (typeof<TimeSpan>, fun x out -> int (x :?> TimeSpan).Ticks out)

#endif
