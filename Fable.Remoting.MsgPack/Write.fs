module Fable.Remoting.MsgPack.Write

open System.Collections.Concurrent
open System.IO
open System
open System.Collections.Generic
open System.Text
open FSharp.Reflection
open System.Numerics
open FSharp.NativeInterop
open System.Reflection
open System.Linq.Expressions

#if !FABLE_COMPILER
#nowarn "9"

let packerCache = ConcurrentDictionary<Type, obj -> Stream -> unit> ()
let unionCaseFieldReaderCache = ConcurrentDictionary<Type, obj -> obj[]> ()

let createPropGetterFunc (prop: PropertyInfo) =
    let instance = Expression.Parameter (typeof<obj>, "instance")
    
    let expr =
        Expression.Lambda<Func<obj, obj>> (
            Expression.Convert (
                Expression.Property (
                    Expression.Convert (instance, prop.DeclaringType),
                    prop),
                typeof<obj>),
            instance)

    expr.Compile ()

let createUnionTagReaderFunc (unionType: Type) =
    // option does not have the Tag property
    if unionType.IsGenericType && unionType.GetGenericTypeDefinition () = typedefof<_ option> then
        Func<_, _> (fun (unionInstance: obj) -> if isNull unionInstance then 0 else 1)
    else
        let prop = unionType.GetProperty ("Tag", BindingFlags.Public ||| BindingFlags.NonPublic ||| BindingFlags.Instance)
        let instance = Expression.Parameter (typeof<obj>, "instance")

        let expr =
            Expression.Lambda<Func<obj, int>> (
                Expression.Property (Expression.Convert (instance, unionType), prop),
                instance)
        expr.Compile ()

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
 
let arrayHeader length (out: Stream) =
    if length < 16 then
        out.WriteByte (Format.fixarr length)
    elif length < 65536 then
        out.WriteByte Format.Array16
        out.WriteByte (length >>> 8 |> byte)
        out.WriteByte (byte length)
    else
        out.WriteByte Format.Array32
        writeUnsigned32bitNumber (uint32 length) out false

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
        arrayHeader length out        

        for x in list do
            write x out

        length

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

let strHeader length (out: Stream) =
    if length < 32 then
        out.WriteByte (Format.fixstr length)
    else
        if length < 256 then
            out.WriteByte Format.Str8
        elif length < 65536 then
            out.WriteByte Format.Str16
        else
            out.WriteByte Format.Str32

        writeUnsigned32bitNumber (uint32 length) out false

let str (str: string) (out: Stream) =
    if isNull str then nil out else
#if NET_CORE
    let maxLength = Encoding.UTF8.GetMaxByteCount str.Length
    
    // allocate space on the stack if the string is not too long
    if str.Length < 500 then
        let buffer = Span (NativePtr.stackalloc<byte> maxLength |> NativePtr.toVoidPtr, maxLength)
        let bytesWritten = Encoding.UTF8.GetBytes (String.op_Implicit str, buffer)

        strHeader bytesWritten out
        out.Write (Span.op_Implicit (buffer.Slice (0, bytesWritten)))
    else
        let buffer = System.Buffers.ArrayPool.Shared.Rent maxLength

        try
            let bytesWritten = Encoding.UTF8.GetBytes (str, 0, str.Length, buffer, 0)

            strHeader bytesWritten out
            out.Write (buffer, 0, bytesWritten)
        finally
            System.Buffers.ArrayPool.Shared.Return buffer
#else
    let str = Encoding.UTF8.GetBytes str
    strHeader str.Length out
    out.Write (str, 0, str.Length)
#endif

let inline float32 (n: float32) (out: Stream) =
    out.WriteByte Format.Float32
    writeSignedNumber (BitConverter.GetBytes n) out
    
let inline float64 (n: float) (out: Stream) =
    out.WriteByte Format.Float64
    writeSignedNumber (BitConverter.GetBytes n) out

let bin (data: byte[]) (out: Stream) =
    if isNull data then nil out else

    if data.Length < 256 then
        out.WriteByte Format.Bin8
    elif data.Length < 65536 then
        out.WriteByte Format.Bin16
    else
        out.WriteByte Format.Bin32

    writeUnsigned32bitNumber (uint32 data.Length) out false
    out.Write (data, 0, data.Length)

let inline dateTimeOffset (out: Stream) (dto: DateTimeOffset) =
    out.WriteByte (Format.fixarr 2uy)
    int dto.Ticks out
    int (int64 dto.Offset.TotalMinutes) out

let inline array write (arr: Array) (out: Stream) =
    if isNull arr then nil out else
    
    arrayHeader arr.Length out

    for x in arr do
        write x out

let inline record (writersAndPropGetters: ((obj -> Stream -> unit) * Func<obj, obj>)[]) (recordInstance: obj) (out: Stream) =
    arrayHeader writersAndPropGetters.Length out

    for write, p in writersAndPropGetters do
        let y = p.Invoke recordInstance
        write y out

let union tag (writersAndPropGetters: ((obj -> Stream -> unit) * Func<obj, obj>)[]) (unionInstance: obj) (out: Stream) =
    out.WriteByte (Format.fixarr 2uy)
    out.WriteByte (Format.fixposnum tag)

    // save 1 byte if the union case has a single parameter
    if writersAndPropGetters.Length <> 1 then
        arrayHeader writersAndPropGetters.Length out
        
        for write, p in writersAndPropGetters do
            let y = p.Invoke unionInstance
            write y out
    else
        let write, p = writersAndPropGetters.[0]
        write (p.Invoke unionInstance) out

let rec inline tuple (out: Stream) (elements: obj[]) =
    arrayHeader elements.Length out
   
    for x in elements do
        object x out

and object (x: obj) (out: Stream) =
    if isNull x then nil out else

    let t = x.GetType ()

    match packerCache.TryGetValue t with
    | true, writer ->
        writer x out
    | _ ->
        if FSharpType.IsRecord (t, true) then
            let props = t.GetProperties (BindingFlags.Public ||| BindingFlags.NonPublic ||| BindingFlags.Instance)
            let propGetters = props |> Array.map createPropGetterFunc

            // run the serialization, populating packerCache with the element type of the array
            record (propGetters |> Array.map (fun g -> object, g)) x out

            // and from now on skip type lookup of individual field types
            let propGetters =
                Array.zip props propGetters
                |> Array.map (fun (p, g) -> (match packerCache.TryGetValue p.PropertyType with true, writer -> writer | _ -> object), g)

            packerCache.[t] <- record propGetters
        elif t.IsArray then
            let x = x :?> Array

            // run the serialization, populating packerCache with the element type of the array
            array object x out

            if x.Length > 0 then
                // and from now on skip type lookup of individual elements
                let elementType = t.GetElementType ()
                match packerCache.TryGetValue elementType with
                | true, writer ->
                    packerCache.[t] <- fun x -> array writer (x :?> Array)
                | _ ->
                    packerCache.[t] <- fun x -> array object (x :?> Array)

        elif t.CustomAttributes |> Seq.exists (fun x -> x.AttributeType.Name = "StringEnumAttribute") then
            packerCache.GetOrAdd (t, fun x (out: Stream) ->
                //todo cacheable
                let case, _ = FSharpValue.GetUnionFields (x, t, true)
                //todo when overriden with CompiledName
                str (sprintf "%c%s" (Char.ToLowerInvariant case.Name.[0]) (case.Name.Substring 1)) out) x out
        elif FSharpType.IsUnion (t, true) then
            if t.IsGenericType && t.GetGenericTypeDefinition () = typedefof<_ list> then
                let listType = t.GetGenericArguments () |> Array.head
                let listSerializer = typedefof<ListSerializer<_>>.MakeGenericType listType
                let d = Delegate.CreateDelegate (typeof<Func<obj, obj, obj, int>>, listSerializer.GetMethod "Serialize") :?> Func<obj, obj, obj, int>
                
                // run the serialization, populating packerCache with listType
                let listLength = d.Invoke (x, out, object)

                if listLength > 0 then
                    // and from now on skip type lookup of individual elements
                    match packerCache.TryGetValue listType with
                    | true, writer ->
                        packerCache.[t] <- fun x out -> d.Invoke (x, out, writer) |> ignore
                    | _ ->
                        packerCache.[t] <- fun x out -> d.Invoke (x, out, object) |> ignore
            // when t is the actual union type
            elif isNull t.DeclaringType || (FSharpType.IsUnion t.DeclaringType |> not) then
                let tagReader = createUnionTagReaderFunc t
                let fieldReaders =
                    FSharpType.GetUnionCases (t, true)
                    |> Array.map (fun c ->
                        let casePropGetters = c.GetFields () |> Array.map (fun p -> object, createPropGetterFunc p)
                        c, casePropGetters)

                // run the serialization, populating packerCache with listType
                (
                    let tag = tagReader.Invoke x
                    let fieldReaders = fieldReaders |> Array.find (fun (c, _) -> tag = c.Tag) |> snd
                    union tag fieldReaders x out
                )

                // and from now on skip type lookup of individual elements
                let fieldReaders =
                    fieldReaders
                    |> Array.map (fun (case, r) ->
                        let fields = case.GetFields ()
                        case.Tag, Array.zip fields r |> Array.map (fun (p, r) -> (match packerCache.TryGetValue p.PropertyType with true, writer -> writer | _ -> object), snd r))

                packerCache.[t] <- fun x out ->
                    let tag = tagReader.Invoke x
                    let fieldReaders = fieldReaders |> Array.find (fun (tag', _) -> tag = tag') |> snd
                    union tag fieldReaders x out
            // when t is just a specific case type
            else
                let case, _ = FSharpValue.GetUnionFields (x, t, true)
                let fieldProps = case.GetFields ()
                let fieldReaders = fieldProps |> Array.map (fun p -> object, createPropGetterFunc p)

                let union = union case.Tag

                // run the serialization, populating packerCache with the element type of the array
                union fieldReaders x out

                // and from now on skip type lookup of individual field types
                let fieldReaders = Array.zip fieldProps fieldReaders |> Array.map (fun (p, r) -> (match packerCache.TryGetValue p.PropertyType with true, writer -> writer | _ -> object), snd r)
                packerCache.[t] <- union fieldReaders
        elif FSharpType.IsTuple t then
            let tupleReader = FSharpValue.PreComputeTupleReader t
            packerCache.GetOrAdd (t, fun x out -> tupleReader x |> tuple out) x out
        elif t.IsGenericType && List.contains (t.GetGenericTypeDefinition ()) [ typedefof<Dictionary<_, _>>; typedefof<Map<_, _>> ] then
            let mapTypes = t.GetGenericArguments ()
            let mapSerializer = typedefof<DictionarySerializer<_,_>>.MakeGenericType mapTypes
            let d = Delegate.CreateDelegate (typeof<Action<obj, obj, obj>>, mapSerializer.GetMethod "Serialize") :?> Action<obj, obj, obj>
            
            packerCache.GetOrAdd (t, fun x out -> d.Invoke (x, out, object)) x out
        elif t.IsEnum then
            packerCache.GetOrAdd (t, fun x -> int (Convert.ChangeType (x, typeof<int64>) :?> int64)) x out
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

let writeSignedNumber bytes (out: ResizeArray<byte>) =
    if BitConverter.IsLittleEndian then
        Array.rev bytes |> out.AddRange
    else
        out.AddRange bytes

let uint (n: UInt64) (out: ResizeArray<byte>) =
    if n < 128UL then
        out.Add (Format.fixposnum n)
    else
        writeUnsigned64bitNumber n out

let int (n: int64) (out: ResizeArray<byte>) =
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

let float32 (n: float32) (out: ResizeArray<byte>) =
    out.Add Format.Float32
    writeSignedNumber (BitConverter.GetBytes n) out
    
let float64 (n: float) (out: ResizeArray<byte>) =
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

let arrayHeader len (out: ResizeArray<byte>) =
    if len < 16 then
        out.Add (Format.fixarr len)
    elif len < 65536 then
        out.Add Format.Array16
        out.Add (len >>> 8 |> FSharp.Core.Operators.byte)
        out.Add (FSharp.Core.Operators.byte len)
    else
        out.Add Format.Array32
        writeUnsigned32bitNumber (uint32 len) out false

let rec array (out: ResizeArray<byte>) t (arr: System.Collections.ICollection) =
    arrayHeader arr.Count out

    for x in arr do
        object x t out

and map (out: ResizeArray<byte>) keyType valueType (dict: IDictionary<obj, obj>) =
    let length = dict.Count

    if length < 16 then
        out.Add (Format.fixmap length)
    elif length < 65536 then
        out.Add Format.Map16
        out.Add (length >>> 8 |> FSharp.Core.Operators.byte)
        out.Add (FSharp.Core.Operators.byte length)
    else
        out.Add Format.Map32
        writeUnsigned32bitNumber (uint32 length) out false

    for kvp in dict do
        object kvp.Key keyType out
        object kvp.Value valueType out

and inline record (out: ResizeArray<byte>) (types: Type[]) (vals: obj[]) =
    arrayHeader vals.Length out

    for i in 0 .. vals.Length - 1 do
        object vals.[i] types.[i] out

and inline tuple (out: ResizeArray<byte>) (types: Type[]) (vals: obj[]) =
    record out types vals

and union (out: ResizeArray<byte>) tag (types: Type[]) (vals: obj[]) =
    out.Add (Format.fixarr 2uy)
    out.Add (Format.fixposnum tag)

    // save 1 byte if the union case has a single parameter
    if vals.Length <> 1 then
        arrayHeader vals.Length out

        for i in 0 .. vals.Length - 1 do
            object vals.[i] types.[i] out
    else
        object vals.[0] types.[0] out

and object (x: obj) (t: Type) (out: ResizeArray<byte>) =
    if isNull x then nil out else

    match packerCache.TryGetValue t with
    | true, writer ->
        writer x out
    | _ ->
        if FSharpType.IsRecord (t, true) then
            let fieldTypes = FSharpType.GetRecordFields (t, true) |> Array.map (fun x -> x.PropertyType)
            cacheGetOrAdd (t, fun x out -> record out fieldTypes (FSharpValue.GetRecordFields (x, true))) x out
        elif t.IsArray then
            let elementType = t.GetElementType ()
            cacheGetOrAdd (t, fun x out -> array out elementType (x :?> System.Collections.ICollection)) x out
        elif t.IsGenericType && t.GetGenericTypeDefinition () = typedefof<_ list> then
            let elementType = t.GetGenericArguments () |> Array.head
            cacheGetOrAdd (t, fun x out -> array out elementType (x :?> System.Collections.ICollection)) x out
        elif t.IsGenericType && t.GetGenericTypeDefinition () = typedefof<_ option> then
            let elementType = t.GetGenericArguments ()
            cacheGetOrAdd (t, fun x out ->
                let opt = x :?> _ option
                union out (if Option.isNone opt then 0 else 1) elementType [| opt |]) x out
        elif FSharpType.IsUnion (t, true) then
            cacheGetOrAdd (t, fun x out ->
                let case, fields = FSharpValue.GetUnionFields (x, t, true)
                let fieldTypes = case.GetFields () |> Array.map (fun x -> x.PropertyType)
                union out case.Tag fieldTypes fields) x out
        elif FSharpType.IsTuple t then
            let fieldTypes = FSharpType.GetTupleElements t
            cacheGetOrAdd (t, fun x out -> tuple out fieldTypes (FSharpValue.GetTupleFields x)) x out
        elif t.IsGenericType && List.contains (t.GetGenericTypeDefinition ()) [ typedefof<Dictionary<_, _>>; typedefof<Map<_, _>> ] then
            let mapTypes = t.GetGenericArguments ()
            let keyType = mapTypes.[0]
            let valueType = mapTypes.[1]
            cacheGetOrAdd (t, fun x out -> map out keyType valueType (box x :?> IDictionary<obj, obj>)) x out
        elif t.IsEnum then
            cacheGetOrAdd (t, fun x -> int (box x :?> int64)) x out
        elif t.FullName = "Microsoft.FSharp.Core.int16`1" || t.FullName = "Microsoft.FSharp.Core.int32`1" || t.FullName = "Microsoft.FSharp.Core.int64`1" then
            cacheGetOrAdd (t, fun x out -> int (x :?> int64) out) x out
        elif t.FullName = "Microsoft.FSharp.Core.decimal`1" then
            cacheGetOrAdd (t, fun x out -> float64 (x :?> decimal |> float) out) x out
        elif t.FullName = "Microsoft.FSharp.Core.float`1" then
            cacheGetOrAdd (t, fun x out -> float64 (x :?> float) out) x out
        elif t.FullName = "Microsoft.FSharp.Core.float32`1" then
            cacheGetOrAdd (t, fun x out -> float32 (x :?> float32) out) x out
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
packerCache.Add (typeof<byte[]>, fun x out -> bin (x :?> byte[]) out)
packerCache.Add (typeof<BigInteger>, fun x out -> bin ((x :?> BigInteger).ToByteArray ()) out)
packerCache.Add (typeof<Guid>, fun x out -> bin ((x :?> Guid).ToByteArray ()) out)
packerCache.Add (typeof<DateTime>, fun x out -> int (x :?> DateTime).Ticks out)
packerCache.Add (typeof<DateTimeOffset>, fun x out -> dateTimeOffset out (x :?> DateTimeOffset))
packerCache.Add (typeof<TimeSpan>, fun x out -> int (x :?> TimeSpan).Ticks out)

#endif
