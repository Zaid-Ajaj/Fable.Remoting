module Fable.Remoting.MsgPack

open System
open System.IO
open System.Text
open System.Collections.Concurrent
open System.Collections.Generic
open FSharp.Reflection
open System.Numerics
open System.Collections

module Format =
    [<Literal>]
    let nil = 0xc0uy
    [<Literal>]
    let fals = 0xc2uy
    [<Literal>]
    let tru = 0xc3uy

    let inline fixposnum value = byte value
    let inline fixnegnum value = byte value ||| 0b11100000uy
    [<Literal>]
    let uint8 = 0xccuy
    [<Literal>]
    let uint16 = 0xcduy
    [<Literal>]
    let uint32 = 0xceuy
    [<Literal>]
    let uint64 = 0xcfuy

    [<Literal>]
    let int8 = 0xd0uy
    [<Literal>]
    let int16 = 0xd1uy
    [<Literal>]
    let int32 = 0xd2uy
    [<Literal>]
    let int64 = 0xd3uy

    let inline fixstr len = 160uy + byte len
    [<Literal>]
    let str8 = 0xd9uy
    [<Literal>]
    let str16 = 0xdauy
    [<Literal>]
    let str32 = 0xdbuy

    [<Literal>]
    let float32 = 0xcauy
    [<Literal>]
    let float64 = 0xcbuy

    let inline fixarr len = 144uy + byte len
    [<Literal>]
    let array16 = 0xdcuy
    [<Literal>]
    let array32 = 0xdduy

    [<Literal>]
    let bin8 = 0xc4uy
    [<Literal>]
    let bin16 = 0xc5uy
    [<Literal>]
    let bin32 = 0xc6uy

    let inline fixmap len = 128uy + byte len
    [<Literal>]
    let map16 = 0xdeuy
    [<Literal>]
    let map32 = 0xdfuy

#if !FABLE_COMPILER
let packerCache = ConcurrentDictionary<Type, obj -> Stream -> unit> ()

let inline write32bitNumber b1 b2 b3 b4 (s: Stream) writeFormat =
    if b2 > 0uy || b1 > 0uy then
        if writeFormat then s.WriteByte Format.uint32
        s.WriteByte b1
        s.WriteByte b2
        s.WriteByte b3
        s.WriteByte b4
    elif (b3 > 0uy) then
        if writeFormat then s.WriteByte Format.uint16
        s.WriteByte b3
        s.WriteByte b4
    else
        if writeFormat then s.WriteByte Format.uint8
        s.WriteByte b4

let write64bitNumber b1 b2 b3 b4 b5 b6 b7 b8 (s: Stream) =
    if b4 > 0uy || b3 > 0uy || b2 > 0uy || b1 > 0uy then
        s.WriteByte Format.uint64
        s.WriteByte b1
        s.WriteByte b2
        s.WriteByte b3
        s.WriteByte b4
        write32bitNumber b5 b6 b7 b8 s false
    else
        write32bitNumber b5 b6 b7 b8 s true

let inline writeUnsigned32bitNumber (n: UInt32) (s: Stream) =
    write32bitNumber (n >>> 24 |> byte) (n >>> 16 |> byte) (n >>> 8 |> byte) (byte n) s

let inline writeUnsigned64bitNumber (n: UInt64) (s: Stream) =
    write64bitNumber (n >>> 56 |> byte) (n >>> 48 |> byte) (n >>> 40 |> byte) (n >>> 32 |> byte) (n >>> 24 |> byte) (n >>> 16 |> byte) (n >>> 8 |> byte) (byte n) s
  
type DictionarySerializer<'k,'v> () =
    static member Serialize (obj: IDictionary<'k,'v>, s: Stream, write: obj -> Stream -> unit) =
        let kvps = obj |> Seq.map (|KeyValue|) |> Seq.toArray

        if kvps.Length < 16 then
            s.WriteByte (Format.fixmap kvps.Length)
        elif kvps.Length < 65536 then
            s.WriteByte Format.map16
            s.WriteByte (kvps.Length >>> 8 |> byte)
            s.WriteByte (byte kvps.Length)
        else
            s.WriteByte Format.map32
            writeUnsigned32bitNumber (uint32 kvps.Length) s false

        for k, v in kvps do
            write k s
            write v s

type ListSerializer<'a> () =
    static member Serialize (list: 'a list, s: Stream, write: obj -> Stream -> unit) =
        if list.Length < 16 then
            s.WriteByte (Format.fixarr list.Length)
        elif list.Length < 65536 then
            s.WriteByte Format.array16
            s.WriteByte (list.Length >>> 8 |> byte)
            s.WriteByte (byte list.Length)
        else
            s.WriteByte Format.array32
            writeUnsigned32bitNumber (uint32 list.Length) s false

        for x in list do
            write x s

module Write =
    let inline nil (s: Stream) = s.WriteByte Format.nil
    let inline bool x (s: Stream) = s.WriteByte (if x then Format.tru else Format.fals)

    let inline writeSignedNumber bytes (s: Stream) =
        if BitConverter.IsLittleEndian then
            Array.Reverse bytes
    
        s.Write (bytes, 0, bytes.Length)

    let inline uint (n: UInt64) (s: Stream) =
        if n < 128UL then
            s.WriteByte (Format.fixposnum n)
        else
            writeUnsigned64bitNumber n s

    let inline int (n: int64) (s: Stream) =
        if n >= 0L then
            uint (uint64 n) s 
        else
            if n > -32L then
                s.WriteByte (Format.fixnegnum n)
            else
                //todo length optimization
                s.WriteByte Format.int64
                writeSignedNumber (BitConverter.GetBytes n) s

    let inline str (str: string) (s: Stream) =
        let str = Encoding.UTF8.GetBytes str

        if str.Length < 32 then
            s.WriteByte (Format.fixstr str.Length)
        else
            if str.Length < 256 then
                s.WriteByte Format.str8
            elif str.Length < 65536 then
                s.WriteByte Format.str16
            else
                s.WriteByte Format.str32

            writeUnsigned32bitNumber (uint32 str.Length) s false

        s.Write (str, 0, str.Length)

    let inline float32 (n: float32) (s: Stream) =
        s.WriteByte Format.float32
        writeSignedNumber (BitConverter.GetBytes n) s
        
    let inline float64 (n: float) (s: Stream) =
        s.WriteByte Format.float64
        writeSignedNumber (BitConverter.GetBytes n) s

    let bin (data: byte[]) (s: Stream) =
        if data.Length < 256 then
            s.WriteByte Format.bin8
        elif data.Length < 65536 then
            s.WriteByte Format.bin16
        else
            s.WriteByte Format.bin32

        writeUnsigned32bitNumber (uint32 data.Length) s false

        use sw = new MemoryStream (data)
        sw.CopyTo s

    let rec array (s: Stream) (arr: System.Collections.IList) =
        if arr.Count < 16 then
            s.WriteByte (Format.fixarr arr.Count)
        elif arr.Count < 65536 then
            s.WriteByte Format.array16
            s.WriteByte (arr.Count >>> 8 |> byte)
            s.WriteByte (byte arr.Count)
        else
            s.WriteByte Format.array32
            writeUnsigned32bitNumber (uint32 arr.Count) s false

        for x in arr do
            write x s

    and inline tuple (s: Stream) (items: obj[]) =
        array s items

    and union (s: Stream) tag (vals: obj[]) =
        s.WriteByte (Format.fixarr 2uy)
        s.WriteByte (Format.fixposnum tag)

        // save 1 byte if the union case has a single parameter
        if vals.Length <> 1 then
            array s vals
        else
            write vals.[0] s

    and write (x: obj) (s: Stream) =
        if isNull x then nil s else

        let t = x.GetType()

        match packerCache.TryGetValue (if t.IsArray && t <> typeof<byte[]> then typeof<Array> else t) with
        | (true, writer) ->
            writer x s
        | _ ->
            if FSharpType.IsRecord t then
                let props = FSharpType.GetRecordFields t

                let writer x (s: Stream) =
                    props
                    |> Array.map (fun prop -> prop.GetValue x)
                    |> array s

                packerCache.TryAdd (t, writer) |> ignore
                writer x s
            elif FSharpType.IsUnion (t, true)  then
                if t.IsGenericType && t.GetGenericTypeDefinition () = typedefof<_ list> then
                    let listType = t.GetGenericArguments () |> Array.head
                    let listSerializer = typedefof<ListSerializer<_>>.MakeGenericType listType
                    let listSerializeMethod = listSerializer.GetMethod "Serialize"
                    
                    let writer x (s: Stream) = listSerializeMethod.Invoke (null, [| x; s; write |]) |> ignore

                    packerCache.TryAdd (t, writer) |> ignore
                    writer x s
                else
                    let writer x (s: Stream) =
                        let case, vals = FSharpValue.GetUnionFields (x, t, true)
                        union s case.Tag vals

                    packerCache.TryAdd (t, writer) |> ignore
                    writer x s
            elif FSharpType.IsTuple t then
                let writer x (s: Stream) =
                    FSharpValue.GetTupleFields x |> tuple s

                packerCache.TryAdd (t, writer) |> ignore
                writer x s
            elif t.IsGenericType && List.contains (t.GetGenericTypeDefinition ()) [ typedefof<Dictionary<_, _>>; typedefof<Map<_, _>> ] then
                let mapTypes = t.GetGenericArguments ()
                let mapSerializer = typedefof<DictionarySerializer<_,_>>.MakeGenericType mapTypes
                let mapSerializeMethod = mapSerializer.GetMethod "Serialize"
                
                let writer x (s: Stream) = mapSerializeMethod.Invoke (null, [| x; s; write |]) |> ignore

                packerCache.TryAdd (t, writer) |> ignore
                writer x s
            else
                failwithf "Cannot pack %s" t.Name

packerCache.TryAdd (typeof<byte>, fun x s -> Write.uint (x :?> byte |> uint64) s) |> ignore
packerCache.TryAdd (typeof<sbyte>, fun x s -> Write.int (x :?> sbyte |> int64) s) |> ignore
packerCache.TryAdd (typeof<unit>, fun _ s -> Write.nil s) |> ignore
packerCache.TryAdd (typeof<bool>, fun x s -> Write.bool (x :?> bool) s) |> ignore
packerCache.TryAdd (typeof<string>, fun x s -> Write.str (x :?> string) s) |> ignore
packerCache.TryAdd (typeof<int>, fun x s -> Write.int (x :?> int |> int64) s) |> ignore
packerCache.TryAdd (typeof<int16>, fun x s -> Write.int (x :?> int16 |> int64) s) |> ignore
packerCache.TryAdd (typeof<int64>, fun x s -> Write.int (x :?> int64) s) |> ignore
packerCache.TryAdd (typeof<UInt32>, fun x s -> Write.uint (x :?> UInt32 |> uint64) s) |> ignore
packerCache.TryAdd (typeof<UInt16>, fun x s -> Write.uint (x :?> UInt16 |> uint64) s) |> ignore
packerCache.TryAdd (typeof<UInt64>, fun x s -> Write.uint (x :?> UInt64) s) |> ignore
packerCache.TryAdd (typeof<float32>, fun x s -> Write.float32 (x :?> float32) s) |> ignore
packerCache.TryAdd (typeof<float>, fun x s -> Write.float64 (x :?> float) s) |> ignore
packerCache.TryAdd (typeof<decimal>, fun x s -> Write.float64 (x :?> decimal |> float) s) |> ignore
packerCache.TryAdd (typeof<Array>, fun x s -> Write.array s (x :?> Array)) |> ignore
packerCache.TryAdd (typeof<byte[]>, fun x s -> Write.bin (x :?> byte[]) s) |> ignore
packerCache.TryAdd (typeof<BigInteger>, fun x s -> Write.array s ((x :?> BigInteger).ToByteArray ())) |> ignore
packerCache.TryAdd (typeof<DateTime>, fun x s -> Write.int (x :?> DateTime).Ticks s) |> ignore
packerCache.TryAdd (typeof<TimeSpan>, fun x s -> Write.int (x :?> TimeSpan).Ticks s) |> ignore
//todo timezone info
//packerCache.TryAdd (typeof<DateTimeOffset>, fun x s -> Write.int (x :?> DateTimeOffset).Ticks s) |> ignore
//todo units of measure

#endif

let inline flip (data: byte[]) pos len =
    let arr = Array.zeroCreate len

    for i in 0 .. len - 1 do
        arr.[i] <- data.[pos + len - 1 - i]

    arr

let inline interpretIntegerAs typ n =
    if typ = typeof<Int32> then int32 n |> box
    elif typ = typeof<Int64> then int64 n |> box
    elif typ = typeof<Int16> then int16 n |> box
    elif typ = typeof<DateTime> then DateTime (int64 n) |> box
    elif typ = typeof<UInt32> then uint32 n |> box
    elif typ = typeof<UInt64> then uint64 n |> box
    elif typ = typeof<UInt16> then uint16 n |> box
    elif typ = typeof<TimeSpan> then TimeSpan (int64 n) |> box
#if FABLE_COMPILER
    elif typ.FullName = "Microsoft.FSharp.Core.int16`1" then int16 n |> box
    elif typ.FullName = "Microsoft.FSharp.Core.int32`1" then int32 n |> box
    elif typ.FullName = "Microsoft.FSharp.Core.int64`1" then int64 n |> box
#endif
    elif typ = typeof<byte> then byte n |> box
    elif typ = typeof<sbyte> then sbyte n |> box
    else failwithf "Cannot interpret integer %A as %s." n typ.Name

let inline interpretFloatAs typ n =
    if typ = typeof<float32> then float32 n |> box
    elif typ = typeof<float> then float n |> box
    elif typ = typeof<decimal> then decimal n |> box
#if FABLE_COMPILER
    elif typ.FullName = "Microsoft.FSharp.Core.float32`1" then float32 n |> box
    elif typ.FullName = "Microsoft.FSharp.Core.float`1" then float n |> box
    elif typ.FullName = "Microsoft.FSharp.Core.decimal`1" then decimal n |> box
#endif
    else failwithf "Cannot interpret float %A as %s." n typ.Name

#if !FABLE_COMPILER
type DictionaryDeserializer<'k,'v when 'k: equality and 'k: comparison> () =
    static member Deserialize (len: int, isDictionary, read: Type -> obj) =
        let keyType = typeof<'k>
        let valueType = typeof<'v>

        if isDictionary then
            let dict = Dictionary<'k, 'v> (len)

            for _ in 0 .. len - 1 do
                dict.Add (read keyType :?> 'k, read valueType :?> 'v)

            box dict
        else
            [|
                for _ in 0 .. len - 1 ->
                    read keyType :?> 'k, read valueType :?> 'v
            |] |> Map.ofArray |> box

type ListDeserializer<'a> () =
    static member Deserialize (len: int, read: Type -> obj) =
        let argType = typeof<'a>

        [
            for _ in 0 .. len - 1 ->
                read argType :?> 'a
        ]
#endif

type Reader (data: byte[]) =
    let mutable pos = 0

    let readInt len m =
        if BitConverter.IsLittleEndian then
            let flipped = flip data pos len
            let x = m (flipped, 0)
            pos <- pos + len
            x
        else
            pos <- pos + len
            m (data, pos - len)

    member _.ReadByte () =
        pos <- pos + 1
        data.[pos - 1]

    member _.ReadByteArray len =
        pos <- pos + len
        data.[ pos - len .. pos - 1 ]

    member _.ReadString len =
        pos <- pos + len
        Encoding.UTF8.GetString (data, pos - len, len)

    member x.ReadUInt8 () =
        x.ReadByte ()

    member x.ReadInt8 () =
        x.ReadByte () |> sbyte

    member _.ReadUInt16 () =
        readInt 2 BitConverter.ToUInt16

    member _.ReadInt16 () =
        readInt 2 BitConverter.ToInt16

    member _.ReadUInt32 () =
        readInt 4 BitConverter.ToUInt32

    member _.ReadInt32 () =
        readInt 4 BitConverter.ToInt32

    member _.ReadUInt64 () =
        readInt 8 BitConverter.ToUInt64

    member _.ReadInt64 () =
        readInt 8 BitConverter.ToInt64

    member _.ReadFloat32 () =
        readInt 4 BitConverter.ToSingle

    member _.ReadFloat64 () =
        readInt 8 BitConverter.ToDouble

    member x.ReadMap (len: int, t: Type) =
        let args = t.GetGenericArguments ()

        if args.Length <> 2 then
            failwithf "Expecting %s, but the data contains a map." t.Name

#if !FABLE_COMPILER
        // todo cache
        let mapDeserializer = typedefof<DictionaryDeserializer<_,_>>.MakeGenericType args
        let mapDeserializeMethod = mapDeserializer.GetMethod "Deserialize"
        let isDictionary = t.GetGenericTypeDefinition () = typedefof<Dictionary<_, _>>
        
        mapDeserializeMethod.Invoke (null, [| len; isDictionary; x.Read |])
#else
        let pairs =
            [|
                for _ in 0 .. len - 1 ->
                    x.Read args.[0] |> box :?> IComparable, x.Read args.[1]
            |]

        if t.GetGenericTypeDefinition () = typedefof<Dictionary<_, _>> then
            let dict = Dictionary<_, _> len
            pairs |> Array.iter dict.Add
            box dict
        else
            Map.ofArray pairs |> box
#endif

    member x.ReadRawArray (len: int, elementType: Type) =
#if !FABLE_COMPILER
        let arr = Array.CreateInstance (elementType, len)

        for i in 0 .. len - 1 do
            arr.SetValue (x.Read elementType, i)

        arr
#else
        [|
            for _ in 0 .. len - 1 ->
                x.Read elementType
        |]
#endif

    member x.ReadList (len: int, elementType: Type) =
#if !FABLE_COMPILER
        let listDeserializer = typedefof<ListDeserializer<_>>.MakeGenericType elementType
        let listDeserializeMethod = listDeserializer.GetMethod "Deserialize"
        
        listDeserializeMethod.Invoke (null, [| len; x.Read |]) |> box
#else
        [
            for _ in 0 .. len - 1 ->
                x.Read elementType
        ]
#endif

    member x.ReadArray (len, t) =
        if FSharpType.IsRecord t then
            let props = FSharpType.GetRecordFields t
            FSharpValue.MakeRecord (t, props |> Array.map (fun prop -> x.Read prop.PropertyType))
        else
            if FSharpType.IsUnion (t, true) then
#if !FABLE_COMPILER
                if t.IsGenericType && t.GetGenericTypeDefinition () = typedefof<_ list> then
                    x.ReadList (len, t.GetGenericArguments () |> Array.head) |> box
                else
#endif
                    let tag = x.Read typeof<int> :?> int
                    let case = FSharpType.GetUnionCases (t, true) |> Array.find (fun y -> y.Tag = tag)
                    let fields = case.GetFields ()

                    let parameters =
                        // single parameter is serialized directly, not in an array
                        if fields.Length = 1 then
                            [| x.Read fields.[0].PropertyType |]
                        else
                            // don't care about this byte, it's going to be a fixarr of length fields.Length
                            x.ReadByte () |> ignore
                            fields |> Array.map (fun y -> x.Read y.PropertyType)

                    FSharpValue.MakeUnion (case, parameters, true)
#if FABLE_COMPILER // Fable does not recognize Option as a union
            elif t.IsGenericType && t.GetGenericTypeDefinition () = typedefof<Option<_>> then
                let tag = x.ReadByte ()

                // none case
                if tag = 0uy then
                    x.ReadByte () |> ignore
                    box null
                else
                    x.Read (t.GetGenericArguments () |> Array.head)
            elif t.IsGenericType && t.GetGenericTypeDefinition () = typedefof<_ list> then
                x.ReadList (len, t.GetGenericArguments () |> Array.head) |> box
#endif
            elif FSharpType.IsTuple t then
                FSharpValue.MakeTuple (FSharpType.GetTupleElements t |> Array.map x.Read, t)
            elif t.IsArray then
                x.ReadRawArray (len, t.GetElementType()) |> box
            elif t.IsGenericType && t.GetGenericTypeDefinition () = typedefof<_ list> then
                x.ReadList (len, t.GetGenericArguments () |> Array.head) |> box
            elif t = typeof<bigint> then
#if !FABLE_COMPILER
                x.ReadRawArray (len, typeof<byte>) :?> byte[] |> bigint |> box
#else
                x.ReadRawArray (len, typeof<byte>) |> box :?> byte[] |> bigint |> box
#endif
            else
                failwithf "Expecting %s at position %d, but the data contains an array." t.Name pos

    member x.Read (t, b) =
        match b with
        // fixstr
        | _ when b ||| 0b00011111uy = 0b10111111uy -> b &&& 0b00011111uy |> int |> x.ReadString |> box
        | Format.str8 -> x.ReadByte () |> int |> x.ReadString |> box
        | Format.str16 -> x.ReadUInt16 () |> int |> x.ReadString |> box
        | Format.str32 -> x.ReadUInt32 () |> int |> x.ReadString |> box
        // fixposnum
        | _ when b ||| 0b01111111uy = 0b01111111uy -> interpretIntegerAs t b
        // fixnegnum
        | _ when b ||| 0b00011111uy = 0b11111111uy -> sbyte b |> interpretIntegerAs t
        | Format.int64 -> x.ReadInt64 () |> interpretIntegerAs t
        | Format.int32 -> x.ReadInt32 () |> interpretIntegerAs t
        | Format.int16 -> x.ReadInt16 () |> interpretIntegerAs t
        | Format.int8 -> x.ReadInt8 () |> interpretIntegerAs t
        | Format.uint8 -> x.ReadUInt8 () |> interpretIntegerAs t
        | Format.uint16 -> x.ReadUInt16 () |> interpretIntegerAs t
        | Format.uint32 -> x.ReadUInt32 () |> interpretIntegerAs t
        | Format.uint64 -> x.ReadUInt64 () |> interpretIntegerAs t
        | Format.float32 -> x.ReadFloat32 () |> interpretFloatAs t
        | Format.float64 -> x.ReadFloat64 () |> interpretFloatAs t
        | Format.nil -> box null
        | Format.tru -> box true
        | Format.fals -> box false
        // fixarr
        | _ when b ||| 0b00001111uy = 0b10011111uy -> x.ReadArray (b &&& 0b00001111uy |> int, t)
        | Format.array16 ->
            let len = x.ReadUInt16 () |> int
            x.ReadArray (len, t)
        | Format.array32 ->
            let len = x.ReadUInt32 () |> int
            x.ReadArray (len, t)
        // fixmap
        | _ when b ||| 0b00001111uy = 0b10001111uy -> x.ReadMap (b &&& 0b00001111uy |> int, t)
        | Format.map16 ->
            let len = x.ReadUInt16 () |> int
            x.ReadMap (len, t)
        | Format.map32 ->
            let len = x.ReadUInt32 () |> int
            x.ReadMap (len, t)
        | Format.bin8 ->
            let len = x.ReadByte () |> int
            x.ReadByteArray len |> box
        | Format.bin16 ->
            let len = x.ReadUInt16 () |> int
            x.ReadByteArray len |> box
        | Format.bin32 ->
            let len = x.ReadUInt32 () |> int
            x.ReadByteArray len |> box
        | _ ->
            failwithf "Position %d, byte %d, expected type %s." pos b t.Name

    member x.Read t =
        let b = x.ReadByte ()
        x.Read (t, b)
            