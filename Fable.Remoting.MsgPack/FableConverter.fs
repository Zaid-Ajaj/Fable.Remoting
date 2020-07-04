module Fable.Remoting.MsgPack

open System
open System.IO
open System.Text
open System.Collections.Concurrent
open System.Collections.Generic
open FSharp.Reflection
open System.Numerics
open System.Collections
open System.Reflection

module private Format =
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
let unionCaseFieldReaderCache = ConcurrentDictionary<Type, obj -> obj[]> ()

module Write =
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

    let inline byte b (s: Stream) =
        s.WriteByte b

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

    let inline dateTimeOffset (s: Stream) (dto: DateTimeOffset) =
        s.WriteByte (Format.fixarr 2uy)
        int dto.Ticks s
        int (int64 dto.Offset.TotalMinutes) s

    let rec array (s: Stream) (arr: System.Collections.ICollection) =
        if arr.Count < 16 then
            s.WriteByte (Format.fixarr arr.Count)
        elif arr.Count < 65536 then
            s.WriteByte Format.array16
            s.WriteByte (arr.Count >>> 8 |> FSharp.Core.Operators.byte)
            s.WriteByte (FSharp.Core.Operators.byte arr.Count)
        else
            s.WriteByte Format.array32
            writeUnsigned32bitNumber (uint32 arr.Count) s false

        for x in arr do
            object x s

    and inline tuple (s: Stream) (items: obj[]) =
        array s items

    and union (s: Stream) tag (vals: obj[]) =
        s.WriteByte (Format.fixarr 2uy)
        s.WriteByte (Format.fixposnum tag)

        // save 1 byte if the union case has a single parameter
        if vals.Length <> 1 then
            array s vals
        else
            object vals.[0] s

    and object (x: obj) (s: Stream) =
        if isNull x then nil s else

        let t = x.GetType()

        match packerCache.TryGetValue (if t.IsArray && t <> typeof<byte[]> then typeof<Array> else t) with
        | (true, writer) ->
            writer x s
        | _ ->
            if FSharpType.IsRecord (t, true) then
                let fieldReader = FSharpValue.PreComputeRecordReader (t, true)
                packerCache.GetOrAdd (t, fun x (s: Stream) -> fieldReader x |> array s) x s
            elif t.CustomAttributes |> Seq.exists (fun x -> x.AttributeType.Name = "StringEnumAttribute") then
                packerCache.GetOrAdd (t, fun x (s: Stream) ->
                    //todo cacheable
                    let case, _ = FSharpValue.GetUnionFields (x, t, true)
                    //todo when overriden with CompiledName
                    str (sprintf "%c%s" (Char.ToLowerInvariant case.Name.[0]) (case.Name.Substring 1)) s) x s
            elif FSharpType.IsUnion (t, true)  then
                if t.IsGenericType && t.GetGenericTypeDefinition () = typedefof<_ list> then
                    let listType = t.GetGenericArguments () |> Array.head
                    let listSerializer = typedefof<ListSerializer<_>>.MakeGenericType listType
                    let listSerializeMethod = listSerializer.GetMethod "Serialize"
                    
                    packerCache.GetOrAdd (t, fun x s -> listSerializeMethod.Invoke (null, [| x; s; object |]) |> ignore) x s
                else
                    let tagReader = FSharpValue.PreComputeUnionTagReader (t, true)
                    let cases = FSharpType.GetUnionCases (t, true)
                    let fieldReaders = cases |> Array.map (fun c -> c.Tag, FSharpValue.PreComputeUnionReader (c, true))

                    packerCache.GetOrAdd (t, fun x s ->
                        let tag = tagReader x
                        let fieldReader = fieldReaders |> Array.find (fun (tag', _) -> tag = tag') |> snd
                        let fieldValues = fieldReader x
                        union s tag fieldValues) x s
            elif FSharpType.IsTuple t then
                let tupleReader = FSharpValue.PreComputeTupleReader t
                packerCache.GetOrAdd (t, fun x s -> tupleReader x |> tuple s) x s
            elif t.IsGenericType && List.contains (t.GetGenericTypeDefinition ()) [ typedefof<Dictionary<_, _>>; typedefof<Map<_, _>> ] then
                let mapTypes = t.GetGenericArguments ()
                let mapSerializer = typedefof<DictionarySerializer<_,_>>.MakeGenericType mapTypes
                let mapSerializeMethod = mapSerializer.GetMethod "Serialize"
                
                packerCache.GetOrAdd (t, fun x s -> mapSerializeMethod.Invoke (null, [| x; s; object |]) |> ignore) x s
            elif t.IsEnum then
                packerCache.GetOrAdd (t, fun x -> object (Convert.ChangeType (x, typeof<int64>) :?> int64)) x s
            else
                failwithf "Cannot pack %s" t.Name

packerCache.TryAdd (typeof<byte>, fun x s -> Write.byte (x :?> byte) s) |> ignore
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
packerCache.TryAdd (typeof<BigInteger>, fun x s -> Write.bin ((x :?> BigInteger).ToByteArray ()) s) |> ignore
packerCache.TryAdd (typeof<Guid>, fun x s -> Write.bin ((x :?> Guid).ToByteArray ()) s) |> ignore
packerCache.TryAdd (typeof<DateTime>, fun x s -> Write.int (x :?> DateTime).Ticks s) |> ignore
packerCache.TryAdd (typeof<DateTimeOffset>, fun x s -> Write.dateTimeOffset s (x :?> DateTimeOffset)) |> ignore
packerCache.TryAdd (typeof<TimeSpan>, fun x s -> Write.int (x :?> TimeSpan).Ticks s) |> ignore

#endif

let interpretStringAs (typ: Type) str =
#if FABLE_COMPILER
    box str
#else
    if typ = typeof<string> then
        box str
    else
        // todo cacheable
        // String enum
        let case = FSharpType.GetUnionCases (typ, true) |> Array.find (fun y -> y.Name = str)
        FSharpValue.MakeUnion (case, [||], true)
#endif

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
#if !FABLE_COMPILER
    elif typ.IsEnum then Enum.ToObject (typ, int64 n)
#else
    elif typ.IsEnum then float n |> box
#endif
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
    static let keyType = typeof<'k>
    static let valueType = typeof<'v>

    static member Deserialize (len: int, isDictionary, read: Type -> obj) =
        if isDictionary then
            let dict = Dictionary<'k, 'v> (len)

            for _ in 0 .. len - 1 do
                dict.Add (read keyType :?> 'k, read valueType :?> 'v)

            box dict
        else
            let arr = Array.zeroCreate len

            for i in 0 .. len - 1 do
                arr.[i] <- read keyType :?> 'k, read valueType :?> 'v

            Map.ofArray arr |> box

type ListDeserializer<'a> () =
    static let argType = typeof<'a>

    static member Deserialize (len: int, read: Type -> obj) =
        let arr = Array.zeroCreate len

        for i in 0 .. len - 1 do
            arr.[i] <- read argType :?> 'a

        List.ofArray arr
#endif

type Reader (data: byte[]) =
    let mutable pos = 0

#if !FABLE_COMPILER
    static let arrayReaderCache = ConcurrentDictionary<Type, (int * Reader) -> obj> ()
    static let unionConstructorCache = ConcurrentDictionary<UnionCaseInfo, obj [] -> obj> ()
    static let unionCaseFieldCache = ConcurrentDictionary<Type * int, UnionCaseInfo * Type[]> ()
#endif

    let readInt len m =
#if !FABLE_COMPILER
        if BitConverter.IsLittleEndian then
            Array.Reverse (data, pos, len)

        pos <- pos + len
        m (data, pos - len)
#else
        if BitConverter.IsLittleEndian then
            let arr = Array.zeroCreate len
        
            for i in 0 .. len - 1 do
                arr.[i] <- data.[pos + len - 1 - i]
            
            pos <- pos + len
            m (arr, 0)
        else
            pos <- pos + len
            m (data, pos - len)
#endif

    member private _.ReadByte () =
        pos <- pos + 1
        data.[pos - 1]

    member private _.ReadRawBin len =
        pos <- pos + len
        data.[ pos - len .. pos - 1 ]

    member private _.ReadString len =
        pos <- pos + len
        Encoding.UTF8.GetString (data, pos - len, len)

    member private x.ReadUInt8 () =
        x.ReadByte ()

    member private x.ReadInt8 () =
        x.ReadByte () |> sbyte

    member private _.ReadUInt16 () =
        readInt 2 BitConverter.ToUInt16

    member private _.ReadInt16 () =
        readInt 2 BitConverter.ToInt16

    member private _.ReadUInt32 () =
        readInt 4 BitConverter.ToUInt32

    member private _.ReadInt32 () =
        readInt 4 BitConverter.ToInt32

    member private _.ReadUInt64 () =
        readInt 8 BitConverter.ToUInt64

    member private _.ReadInt64 () =
        readInt 8 BitConverter.ToInt64

    member private _.ReadFloat32 () =
        readInt 4 BitConverter.ToSingle

    member private _.ReadFloat64 () =
        readInt 8 BitConverter.ToDouble

    member private x.ReadMap (len: int, t: Type) =
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
            let arr = Array.zeroCreate len
            
            for i in 0 .. len - 1 do
                arr.[i] <- x.Read args.[0] |> box :?> IStructuralComparable, x.Read args.[1]

            arr

        if t.GetGenericTypeDefinition () = typedefof<Dictionary<_, _>> then
            let dict = Dictionary<_, _> len
            pairs |> Array.iter dict.Add
            box dict
        else
            Map.ofArray pairs |> box
#endif

    member private x.ReadRawArray (len: int, elementType: Type) =
#if !FABLE_COMPILER
        let arr = Array.CreateInstance (elementType, len)

        for i in 0 .. len - 1 do
            arr.SetValue (x.Read elementType, i)

        arr
#else
        let arr = Array.zeroCreate len

        for i in 0 .. len - 1 do
            arr.[i] <- x.Read elementType
        
        arr
#endif

    member private x.ReadArray (len, t) =
#if !FABLE_COMPILER
        match arrayReaderCache.TryGetValue t with
        | true, reader ->
            reader (len, x)
        | _ ->
#endif

        if FSharpType.IsRecord t then
#if !FABLE_COMPILER
            let fieldTypes = FSharpType.GetRecordFields t |> Array.map (fun prop -> prop.PropertyType)
            let ctor = FSharpValue.PreComputeRecordConstructor (t, true)
            
            arrayReaderCache.GetOrAdd (t, fun (_, x: Reader) ->
            ctor (fieldTypes |> Array.map x.Read)) (len, x)
#else
            let props = FSharpType.GetRecordFields t
            FSharpValue.MakeRecord (t, props |> Array.map (fun prop -> x.Read prop.PropertyType))
#endif
        elif FSharpType.IsUnion (t, true) then
#if !FABLE_COMPILER
            if t.IsGenericType && t.GetGenericTypeDefinition () = typedefof<_ list> then
                arrayReaderCache.GetOrAdd (t, Func<_, _>(fun (t: Type) ->
                    let argType = t.GetGenericArguments () |> Array.head
                    let listDeserializer = typedefof<ListDeserializer<_>>.MakeGenericType argType
                    let mi = listDeserializer.GetMethod "Deserialize"
                            
                    fun (len: int, x: Reader) ->
                        mi.Invoke (null, [| len; x.Read |]))) (len, x)
            else
                arrayReaderCache.GetOrAdd (t, fun (_, x: Reader) ->
                    let tag = x.Read typeof<int> :?> int
                    let case, fieldTypes =
                        unionCaseFieldCache.GetOrAdd ((t, tag), fun (t, tag) ->
                            let case = FSharpType.GetUnionCases (t, true) |> Array.find (fun x -> x.Tag = tag)
                            let fields = case.GetFields ()
                            case, fields |> Array.map (fun x -> x.PropertyType))

                    let fields =
                        // single parameter is serialized directly, not in an array, saving 1 byte on the array format
                        if fieldTypes.Length = 1 then
                            [| x.Read fieldTypes.[0] |]
                        else
                            // don't care about this byte, it's going to be a fixarr of length fields.Length
                            x.ReadByte () |> ignore
                            fieldTypes |> Array.map x.Read

                    unionConstructorCache.GetOrAdd (case, Func<_, _>(fun case -> FSharpValue.PreComputeUnionConstructor (case, true))) fields) (len, x)
#else        
            let tag = x.Read typeof<int> :?> int
            let case = FSharpType.GetUnionCases (t, true) |> Array.find (fun x -> x.Tag = tag)
            let fieldTypes = case.GetFields () |> Array.map (fun x -> x.PropertyType)

            let fields =
                // single parameter is serialized directly, not in an array, saving 1 byte on the array format
                if fieldTypes.Length = 1 then
                    [| x.Read fieldTypes.[0] |]
                else
                    // don't care about this byte, it's going to be a fixarr of length fields.Length
                    x.ReadByte () |> ignore
                    fieldTypes |> Array.map x.Read

            FSharpValue.MakeUnion (case, fields, true)
#endif

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
            let elementType = t.GetGenericArguments () |> Array.head
            [
                for _ in 0 .. len - 1 ->
                    x.Read elementType
            ] |> box
#endif
        elif FSharpType.IsTuple t then
            // todo precompute, cacheable
            FSharpValue.MakeTuple (FSharpType.GetTupleElements t |> Array.map x.Read, t)
        elif t.IsArray then
            x.ReadRawArray (len, t.GetElementType ()) |> box
        elif t = typeof<DateTimeOffset> then
            let dateTimeTicks = x.Read typeof<int64> :?> int64
            let timeSpanMinutes = x.Read typeof<int16> :?> int16
            DateTimeOffset (dateTimeTicks, TimeSpan.FromMinutes (float timeSpanMinutes)) |> box
        else
            failwithf "Expecting %s at position %d, but the data contains an array." t.Name pos

    member private x.ReadBin (len, t) =
        if t = typeof<Guid> then
            x.ReadRawBin len |> Guid |> box
        elif t = typeof<byte[]> then
            x.ReadRawBin len |> box
        elif t = typeof<bigint> then
            x.ReadRawBin len |> bigint |> box
        else
            failwithf "Expecting %s at position %d, but the data contains bin." t.Name pos

    member x.Read t =
        match x.ReadByte () with
        // fixstr
        | b when b ||| 0b00011111uy = 0b10111111uy -> b &&& 0b00011111uy |> int |> x.ReadString |> interpretStringAs t
        | Format.str8 -> x.ReadByte () |> int |> x.ReadString |> interpretStringAs t
        | Format.str16 -> x.ReadUInt16 () |> int |> x.ReadString |> interpretStringAs t
        | Format.str32 -> x.ReadUInt32 () |> int |> x.ReadString |> interpretStringAs t
        // fixposnum
        | b when b ||| 0b01111111uy = 0b01111111uy -> interpretIntegerAs t b
        // fixnegnum
        | b when b ||| 0b00011111uy = 0b11111111uy -> sbyte b |> interpretIntegerAs t
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
        | b when b ||| 0b00001111uy = 0b10011111uy -> x.ReadArray (b &&& 0b00001111uy |> int, t)
        | Format.array16 ->
            let len = x.ReadUInt16 () |> int
            x.ReadArray (len, t)
        | Format.array32 ->
            let len = x.ReadUInt32 () |> int
            x.ReadArray (len, t)
        // fixmap
        | b when b ||| 0b00001111uy = 0b10001111uy -> x.ReadMap (b &&& 0b00001111uy |> int, t)
        | Format.map16 ->
            let len = x.ReadUInt16 () |> int
            x.ReadMap (len, t)
        | Format.map32 ->
            let len = x.ReadUInt32 () |> int
            x.ReadMap (len, t)
        | Format.bin8 ->
            let len = x.ReadByte () |> int
            x.ReadBin (len, t)
        | Format.bin16 ->
            let len = x.ReadUInt16 () |> int
            x.ReadBin (len, t)
        | Format.bin32 ->
            let len = x.ReadUInt32 () |> int
            x.ReadBin (len, t)
        | b ->
            failwithf "Position %d, byte %d, expected type %s." pos b t.Name
