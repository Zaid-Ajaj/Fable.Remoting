module Fable.Remoting.MsgPack.Write

open System.IO
open System
open System.Collections.Generic
open System.Text
open FSharp.Reflection
open FSharp.NativeInterop
open System.Reflection
open System.Collections.Concurrent

#if !FABLE_COMPILER
open System.Linq.Expressions
open TypeShape
open TypeShape_Utils

#nowarn "9"
#nowarn "51"

let this = Assembly.GetCallingAssembly().GetType("Fable.Remoting.MsgPack.Write")

let internal (|BclIsInstanceOfSystemDataSet|_|) (s: TypeShape) =
  let tableTy =  typeof<System.Data.DataSet>
  if s.Type = tableTy || (s.Type.IsInstanceOfType tableTy && s.Type <> typeof<obj>) then
    Shape.SomeU
  else
    None

let internal (|BclIsInstanceOfSystemDataTable|_|) (s: TypeShape) =
  let tableTy =  typeof<System.Data.DataTable>
  if s.Type = tableTy || (s.Type.IsInstanceOfType tableTy && s.Type <> typeof<obj>) then
    Shape.SomeU
  else
    None

#if NETCOREAPP2_1_OR_GREATER
let inline stackalloc<'a when 'a: unmanaged> length = Span<'a> (NativePtr.stackalloc<'a> length |> NativePtr.toVoidPtr, length)
#endif

#if NET6_0_OR_GREATER
let internal (|DateOnly|_|) (s: TypeShape) = Shape.test<DateOnly> s

let internal (|TimeOnly|_|) (s: TypeShape) = Shape.test<TimeOnly> s
#endif

let inline write32bitNumberBytes b1 b2 b3 b4 (out: Stream) writeFormat =
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

let inline write64bitNumberBytes b1 b2 b3 b4 b5 b6 b7 b8 (out: Stream) =
    if b4 > 0uy || b3 > 0uy || b2 > 0uy || b1 > 0uy then
        out.WriteByte Format.Uint64
        out.WriteByte b1
        out.WriteByte b2
        out.WriteByte b3
        out.WriteByte b4
        out.WriteByte b5
        out.WriteByte b6
        out.WriteByte b7
        out.WriteByte b8
    else
        write32bitNumberBytes b5 b6 b7 b8 out true

let inline write32bitNumber n (out: Stream) =
    write32bitNumberBytes (n >>> 24 |> byte) (n >>> 16 |> byte) (n >>> 8 |> byte) (byte n) out

let inline write64bitNumber n (out: Stream) =
    write64bitNumberBytes (n >>> 56 |> byte) (n >>> 48 |> byte) (n >>> 40 |> byte) (n >>> 32 |> byte) (n >>> 24 |> byte) (n >>> 16 |> byte) (n >>> 8 |> byte) (byte n) out

let inline write32bitNumberFull n (out: Stream) =
    out.WriteByte (n >>> 24 |> byte)
    out.WriteByte (n >>> 16 |> byte)
    out.WriteByte (n >>> 8 |> byte)
    out.WriteByte (byte n)

let inline write64bitNumberFull n (out: Stream) =
    out.WriteByte (n >>> 56 |> byte)
    out.WriteByte (n >>> 48 |> byte)
    out.WriteByte (n >>> 40 |> byte)
    out.WriteByte (n >>> 32 |> byte)
    out.WriteByte (n >>> 24 |> byte)
    out.WriteByte (n >>> 16 |> byte)
    out.WriteByte (n >>> 8 |> byte)
    out.WriteByte (byte n)

let inline writeNil (out: Stream) = out.WriteByte Format.Nil

let inline writeBool b (out: Stream) = out.WriteByte (if b then Format.True else Format.False)

let inline writeByte b (out: Stream) =
    if b < 128uy then
        out.WriteByte (Format.fixposnum b)
    else
        out.WriteByte Format.Uint8
        out.WriteByte b

let inline writeSByte (b: sbyte) (out: Stream) =
    writeByte (byte b) out

let inline writeArrayHeader length (out: Stream) =
    if length < 16 then
        out.WriteByte (Format.fixarr length)
    elif length < 65536 then
        out.WriteByte Format.Array16
        out.WriteByte (length >>> 8 |> byte)
        out.WriteByte (byte length)
    else
        out.WriteByte Format.Array32
        write32bitNumber length out false

let inline writeArray (array: 'a[]) (out: Stream) (elementSerializer: Action<'a, Stream>) =
    if isNull array then writeNil out else

    writeArrayHeader array.Length out
    for x in array do
        elementSerializer.Invoke (x, out)

let inline writeList (list: 'a list) (out: Stream) (elementSerializer: Action<'a, Stream>) =
    writeArrayHeader list.Length out
    for x in list do
        elementSerializer.Invoke (x, out)

let inline writeMapHeader length (out: Stream) =
    if length < 16 then
        out.WriteByte (Format.fixmap length)
    elif length < 65536 then
        out.WriteByte Format.Map16
        out.WriteByte (length >>> 8 |> byte)
        out.WriteByte (byte length)
    else
        out.WriteByte Format.Map32
        write32bitNumber length out false

let inline writeSet (set: Set<'a>) (out: Stream) (elementSerializer: Action<'a, Stream>) =
    writeArrayHeader set.Count out
    for x in set do
        elementSerializer.Invoke (x, out)

let inline writeDict (dict: Dictionary<'key, 'value>) (out: Stream) (keyWriter: Action<'key, Stream>) (valueWriter: Action<'value, Stream>) =
    writeMapHeader dict.Count out
    for kvp in dict do
        keyWriter.Invoke (kvp.Key, out)
        valueWriter.Invoke (kvp.Value, out)

// we could use just one function accepting IDictionary for both Map and Dictionary, but Map.iter is significantly faster than a foreach and doesn't allocate
let inline writeMap (map: Map<'key, 'value>) (out: Stream) (keyWriter: Action<'key, Stream>) (valueWriter: Action<'value, Stream>) =
    writeMapHeader map.Count out
    map |> Map.iter (fun k v ->
        keyWriter.Invoke (k, out)
        valueWriter.Invoke (v, out))

let inline writeUInt64 (n: UInt64) (out: Stream) =
    if n < 128UL then
        out.WriteByte (Format.fixposnum n)
    else
        write64bitNumber n out

let inline writeInt64 (n: int64) (out: Stream) =
    if n >= 0L then
        writeUInt64 (uint64 n) out
    elif n > -32L then
        out.WriteByte (Format.fixnegnum n)
    else
        out.WriteByte Format.Int64
        write64bitNumberFull n out

let inline writeSingle (n: float32) (out: Stream) =
    let mutable n = n
    out.WriteByte Format.Float32
    write32bitNumberFull (NativePtr.toNativeInt &&n |> NativePtr.ofNativeInt |> NativePtr.read<uint32>) out

let inline writeDouble (n: float) (out: Stream) =
    let mutable n = n
    out.WriteByte Format.Float64
    write64bitNumberFull (NativePtr.toNativeInt &&n |> NativePtr.ofNativeInt |> NativePtr.read<uint64>) out

#if NET5_0_OR_GREATER
[<System.Runtime.CompilerServices.SkipLocalsInit>]
#endif
let writeDecimal (n: decimal) (out: Stream) =
#if NET5_0_OR_GREATER
    let bits = stackalloc 4
    Decimal.GetBits (n, bits) |> ignore
#else
    let bits = Decimal.GetBits n
#endif

    out.WriteByte (Format.fixarr 4)
    for b in bits do
        write32bitNumber b out true

let inline writeStringHeader length (out: Stream) =
    if length < 32 then
        out.WriteByte (Format.fixstr length)
    else
        if length < 256 then
            out.WriteByte Format.Str8
        elif length < 65536 then
            out.WriteByte Format.Str16
        else
            out.WriteByte Format.Str32

        write32bitNumber length out false

#if NET5_0_OR_GREATER
[<System.Runtime.CompilerServices.SkipLocalsInit>]
#endif
let writeString (str: string) (out: Stream) =
    if isNull str then writeNil out else
#if NETCOREAPP3_1_OR_GREATER
    let maxLength = Encoding.UTF8.GetMaxByteCount str.Length

    // allocate space on the stack if the string is not too long
    if maxLength < 1500 then
        let buffer = stackalloc maxLength
        let bytesWritten = Encoding.UTF8.GetBytes (String.op_Implicit str, buffer)

        writeStringHeader bytesWritten out
        out.Write (Span.op_Implicit (buffer.Slice (0, bytesWritten)))
        ()
    else
        let buffer = System.Buffers.ArrayPool.Shared.Rent maxLength

        try
            let bytesWritten = Encoding.UTF8.GetBytes (str, 0, str.Length, buffer, 0)

            writeStringHeader bytesWritten out
            out.Write (buffer, 0, bytesWritten)
        finally
            System.Buffers.ArrayPool.Shared.Return buffer
#else
    let str = Encoding.UTF8.GetBytes str
    writeStringHeader str.Length out
    out.Write (str, 0, str.Length)
#endif

let writeBin (data: byte[]) (out: Stream) =
    if isNull data then writeNil out else

    if data.Length < 256 then
        out.WriteByte Format.Bin8
    elif data.Length < 65536 then
        out.WriteByte Format.Bin16
    else
        out.WriteByte Format.Bin32

    write32bitNumber data.Length out false
    out.Write (data, 0, data.Length)

#if NET6_0_OR_GREATER
let inline writeDateOnly (date: DateOnly) (out: Stream) =
    write32bitNumber date.DayNumber out true

let inline writeTimeOnly (time: TimeOnly) (out: Stream) =
    writeInt64 time.Ticks out
#endif

let inline writeDateTime (dt: DateTime) (out: Stream) =
    out.WriteByte (Format.fixarr 2uy)
    writeInt64 dt.Ticks out
    writeInt64 (int64 dt.Kind) out

let inline writeDateTimeOffset (dto: DateTimeOffset) (out: Stream) =
    out.WriteByte (Format.fixarr 2uy)
    writeInt64 dto.Ticks out
    writeInt64 (int64 dto.Offset.TotalMinutes) out

let inline writeTimeSpan (ts: TimeSpan) out =
    writeInt64 ts.Ticks out

#if NET5_0_OR_GREATER
[<System.Runtime.CompilerServices.SkipLocalsInit>]
#endif
let writeGuid (g: Guid) (out: Stream) =
#if NETCOREAPP2_1_OR_GREATER
    let buffer = stackalloc 16
    g.TryWriteBytes buffer |> ignore
    out.WriteByte Format.Bin8
    out.WriteByte 16uy
    out.Write buffer
    ()
#else
    writeBin (g.ToByteArray ()) out
#endif

let inline writeBigInteger (i: bigint) out =
    writeBin (i.ToByteArray ()) out

// todo necessary to take the underlying type into account?
let inline writeEnum (enum: 'enum when 'enum: enum<'underlying>) out =
    writeInt64 (Convert.ChangeType (enum, typeof<int64>) :?> int64) out

let inline writeRecord record out (fieldSerializers: Action<'a, Stream>[]) =
    writeArrayHeader fieldSerializers.Length out
    for f in fieldSerializers do
        f.Invoke (record, out)

let inline writeUnion union (out: Stream) (caseSerializers: Action<'a, Stream>[][]) tagReader =
    let tag = tagReader union
    let fieldSerializers = caseSerializers.[tag]

    if fieldSerializers.Length = 0 then
        out.WriteByte (Format.fixarr 1uy)
        out.WriteByte (Format.fixposnum tag)
    else
        out.WriteByte (Format.fixarr 2uy)
        out.WriteByte (Format.fixposnum tag)

        // write the field directly instead of using an array if the union case has a single field
        // saves 1 byte
        if fieldSerializers.Length = 1 then
            let serializer = fieldSerializers.[0]
            serializer.Invoke (union, out)
        else
            writeArrayHeader fieldSerializers.Length out

            for serializer in fieldSerializers do
                serializer.Invoke (union, out)

let inline writeStringEnum union out (caseNames: string[]) tagReader =
    writeString caseNames.[tagReader union] out

let inline writeTuple tuple (out: Stream) (elementSerializers: Action<'a, Stream>[]) =
    writeArrayHeader elementSerializers.Length out
    for s in elementSerializers do
        s.Invoke (tuple, out)

let inline writeDataTable (table: System.Data.DataTable) out =
  let schema, data =
      use stringWriter1 = new StringWriter()
      use stringWriter2 = new StringWriter()
      table.WriteXmlSchema stringWriter1
      table.WriteXml stringWriter2
      string stringWriter1, string stringWriter2
  writeArray [|schema; data|] out (Action<_,_>(writeString))

let inline writeDataSet (dataset: System.Data.DataSet) out =
  let schema, data =
      use stringWriter1 = new StringWriter()
      use stringWriter2 = new StringWriter()
      dataset.WriteXmlSchema stringWriter1
      dataset.WriteXml stringWriter2
      string stringWriter1, string stringWriter2
  writeArray [|schema; data|] out (Action<_,_>(writeString))

let rec makeSerializer<'T> (): Action<'T, Stream> =
    if typeof<'T> = typeof<obj> then
        failwithf "Cannot serialize System.Object. If you are unable specify the generic parameter for 'makeSerializer', use 'makeSerializerObj' instead."

    let ctx = new TypeGenerationContext ()
    serializerCached<'T> ctx

and private serializerCached<'T> (ctx: TypeGenerationContext): Action<'T, Stream> =
    let delay (c: Cell<Action<'T, Stream>>): Action<'T, Stream> =
        Action<'T, Stream>(fun x out -> c.Value.Invoke (x, out))

    match ctx.InitOrGetCachedValue<Action<'T, Stream>> delay with
    | Cached (value, _) -> value
    | NotCached x ->
        let serializer = makeSerializerAux<'T> ctx
        ctx.Commit x serializer

and private makeSerializerAux<'T> (ctx: TypeGenerationContext): Action<'T, Stream> =
    let w (p: Action<'a, Stream>) = unbox<Action<'T, Stream>> p

    let makeMemberVisitor (m: IShapeReadOnlyMember<'T>) =
        m.Accept {
            new IReadOnlyMemberVisitor<'T, Action<'T, Stream>> with
                member _.Visit (field: ReadOnlyMember<'T, 'a>) =
                    let s = serializerCached<'a> ctx
                    Action<_, _> (fun (x: 'T) out -> s.Invoke (field.Get x, out)) |> w
        }

    match shapeof<'T> with
    | Shape.Unit -> Action<_, _> (fun () out -> writeNil out) |> w
    | Shape.Bool -> Action<_, _> writeBool |> w
    | Shape.Byte -> Action<_, _> writeByte |> w
    | Shape.SByte -> Action<_, _> writeSByte |> w
    | Shape.Char -> Action<_, _> (fun (c: char) out -> writeString (c.ToString ()) out) |> w
    | Shape.String -> Action<_, _> writeString |> w
    | Shape.Int16 -> Action<_, _> (fun (i: int16) out -> writeInt64 (int64 i) out) |> w
    | Shape.Int32 -> Action<_, _> (fun (i: int32) out -> writeInt64 (int64 i) out) |> w
    | Shape.Int64 -> Action<_, _> writeInt64 |> w
    | Shape.UInt16 -> Action<_, _> (fun (i: uint16) out -> writeUInt64 (uint64 i) out) |> w
    | Shape.UInt32 -> Action<_, _> (fun (i: uint32) out -> writeUInt64 (uint64 i) out) |> w
    | Shape.UInt64 -> Action<_, _> writeUInt64 |> w
    | Shape.Single -> Action<_, _> writeSingle |> w
    | Shape.Double -> Action<_, _> writeDouble |> w
    | Shape.Decimal -> Action<_, _> writeDecimal |> w
    | Shape.BigInt -> Action<_, _> writeBigInteger |> w
    | Shape.DateTime -> Action<_, _> writeDateTime |> w
    | Shape.DateTimeOffset -> Action<_, _> writeDateTimeOffset |> w
    | Shape.TimeSpan -> Action<_, _> writeTimeSpan |> w
    | Shape.Guid -> Action<_, _> writeGuid |> w
    | Shape.Array s when s.Rank = 1 ->
        s.Element.Accept {
            new ITypeVisitor<Action<'T, Stream>> with
                member _.Visit<'a> () =
                    if typeof<'a> = typeof<byte> then
                        Action<_, _> writeBin |> w
                    else
                        let s = serializerCached<'a> ctx
                        Action<_, _> (fun x out -> writeArray x out s) |> w
        }
    | Shape.FSharpMap m ->
        m.Accept {
            new IFSharpMapVisitor<Action<'T, Stream>> with
                member _.Visit<'key, 'value when 'key: comparison> () =
                    let keyWriter = serializerCached<'key> ctx
                    let valueWriter = serializerCached<'value> ctx
                    Action<_, _> (fun x out -> writeMap x out keyWriter valueWriter) |> w
        }
    | Shape.FSharpSet s ->
        s.Accept {
            new IFSharpSetVisitor<Action<'T, Stream>> with
                member _.Visit<'a when 'a: comparison> () =
                    let s = serializerCached<'a> ctx
                    Action<_, _> (fun x out -> writeSet x out s) |> w
        }
    | Shape.Dictionary d ->
        d.Accept {
            new IDictionaryVisitor<Action<'T, Stream>> with
                member _.Visit<'key, 'value when 'key: equality> () =
                    let keyWriter = serializerCached<'key> ctx
                    let valueWriter = serializerCached<'value> ctx
                    Action<_, _> (fun x out -> writeDict x out keyWriter valueWriter) |> w
        }
    | Shape.FSharpList s ->
        s.Element.Accept {
            new ITypeVisitor<Action<'T, Stream>> with
                member _.Visit<'a> () =
                    let s = serializerCached<'a> ctx
                    Action<_, _> (fun x out -> writeList x out s) |> w
        }
    | Shape.FSharpRecord (:? ShapeFSharpRecord<'T> as shape) ->
        let fieldSerializers = shape.Fields |> Array.map makeMemberVisitor
        Action<_, _> (fun (record: 'T) out -> writeRecord record out fieldSerializers) |> w
    | Shape.FSharpUnion (:? ShapeFSharpUnion<'T> as shape) ->
        if typeof<'T>.CustomAttributes |> Seq.exists (fun a -> a.AttributeType.Name = "StringEnumAttribute") then
            let caseNames = shape.UnionCases |> Array.map (fun c -> sprintf "%c%s" (Char.ToLowerInvariant c.CaseInfo.Name.[0]) (c.CaseInfo.Name.Substring 1))
            Action<_, _> (fun (union: 'T) out -> writeStringEnum union out caseNames shape.GetTag) |> w
        else
            let caseSerializers = shape.UnionCases |> Array.map (fun c -> Array.map makeMemberVisitor c.Fields)
            Action<_, _> (fun (union: 'T) out -> writeUnion union out caseSerializers shape.GetTag) |> w
    | Shape.Enum e ->
        e.Accept {
            new IEnumVisitor<Action<'T, Stream>> with
                member _.Visit<'enum, 'underlying when 'enum: enum<'underlying> and 'enum: struct and 'enum :> ValueType and 'enum : (new : unit -> 'enum)> () =
                    Action<_, _> (fun (e: 'enum) out -> writeEnum e out) |> w
        }
    | Shape.Tuple (:? ShapeTuple<'T> as shape) ->
        let elementSerializers = shape.Elements |> Array.map makeMemberVisitor
        Action<_, _> (fun (tuple: 'T) out -> writeTuple tuple out elementSerializers) |> w
#if NET6_0_OR_GREATER
    | DateOnly ->
        Action<_, _> (fun date out -> writeDateOnly date out) |> w
    | TimeOnly ->
        Action<_, _> (fun time out -> writeTimeOnly time out) |> w
#endif
    | BclIsInstanceOfSystemDataSet ->
        Action<_, _> (fun (dataset: System.Data.DataSet) out -> writeDataSet dataset out) |> w
    | BclIsInstanceOfSystemDataTable ->
        Action<_, _> (fun (table: System.Data.DataTable) out -> writeDataTable table out) |> w
    | _ ->
        failwithf "Cannot serialize %s." typeof<'T>.Name

// TypeShape requires generic types at compile time, but some applications will only know the types at runtime
// This method bypasses that restriction by emitting IL to construct the delegate on demand
let makeSerializerObj (t: Type) =
    let makeSerializerMi = this.GetMethod(nameof(makeSerializer), BindingFlags.Public ||| BindingFlags.Static).MakeGenericMethod t
    let specializedActionType = typedefof<Action<_, _>>.MakeGenericType [| t; typeof<Stream> |]

    let instance = Expression.Parameter (typeof<obj>, "instance")
    let stream = Expression.Parameter (typeof<Stream>, "stream")
    let serializer = Expression.Variable specializedActionType

    let expr =
        Expression.Lambda<Func<Action<obj, Stream>>> (
            Expression.Block (
                [ serializer ],
                // create the serializer here
                Expression.Assign (serializer, Expression.Call (makeSerializerMi, [])),
                // return a delegate for serialization with the specialized serializer embedded and cast obj to the right type
                Expression.Lambda<Action<obj, Stream>> (
                    Expression.Invoke (serializer, Expression.Convert (instance, t), stream),
                    [ instance; stream ]
                )
            ),
            []
        )

    expr.Compile().Invoke ()

let private serializerCache = ConcurrentDictionary<Type, Action<obj, Stream>> ()

let serializeObj (x: obj) (out: Stream) =
    if isNull x then
        writeNil out
    else
        let t = x.GetType ()

        match serializerCache.TryGetValue t with
        | true, serializer ->
            serializer.Invoke (x, out)
        | _ ->
            let serializer = makeSerializerObj t
            serializerCache.[t] <- serializer
            serializer.Invoke (x, out)

#endif

module Fable =
    let private serializerCache = Dictionary<string, obj -> ResizeArray<byte> -> unit> ()

    let private cacheGetOrAdd (typ: Type, f) =
        match serializerCache.TryGetValue typ.FullName with
        | true, f -> f
        | _ ->
            serializerCache.Add (typ.FullName, f)
            f

    let inline private write32bitNumber b1 b2 b3 b4 (out: ResizeArray<byte>) writeFormat =
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

    let private write64bitNumber b1 b2 b3 b4 b5 b6 b7 b8 (out: ResizeArray<byte>) =
        if b4 > 0uy || b3 > 0uy || b2 > 0uy || b1 > 0uy then
            out.Add Format.Uint64
            out.Add b1
            out.Add b2
            out.Add b3
            out.Add b4
            out.Add b5
            out.Add b6
            out.Add b7
            out.Add b8
        else
            write32bitNumber b5 b6 b7 b8 out true

    let inline private writeUnsigned32bitNumber (n: UInt32) (out: ResizeArray<byte>) =
        write32bitNumber (n >>> 24 |> byte) (n >>> 16 |> byte) (n >>> 8 |> byte) (byte n) out

    let inline private writeUnsigned64bitNumber (n: UInt64) (out: ResizeArray<byte>) =
        write64bitNumber (n >>> 56 |> byte) (n >>> 48 |> byte) (n >>> 40 |> byte) (n >>> 32 |> byte) (n >>> 24 |> byte) (n >>> 16 |> byte) (n >>> 8 |> byte) (byte n) out

    let inline private writeNil (out: ResizeArray<byte>) = out.Add Format.Nil
    let inline private writeBool x (out: ResizeArray<byte>) = out.Add (if x then Format.True else Format.False)

    let private writeSignedNumber bytes (out: ResizeArray<byte>) =
        if BitConverter.IsLittleEndian then
            Array.rev bytes |> out.AddRange
        else
            out.AddRange bytes

    let private writeUInt64 (n: UInt64) (out: ResizeArray<byte>) =
        if n < 128UL then
            out.Add (Format.fixposnum n)
        else
            writeUnsigned64bitNumber n out

    let private writeInt64 (n: int64) (out: ResizeArray<byte>) =
        if n >= 0L then
            writeUInt64 (uint64 n) out
        else
            if n > -32L then
                out.Add (Format.fixnegnum n)
            else
                //todo length optimization
                out.Add Format.Int64
                writeSignedNumber (BitConverter.GetBytes n) out

    let private writeByte b (out: ResizeArray<byte>) =
        if b < 128uy then
            out.Add (Format.fixposnum b)
        else
            out.Add Format.Uint8
            out.Add b

    let inline private writeString (str: string) (out: ResizeArray<byte>) =
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

    let private writeSingle (n: float32) (out: ResizeArray<byte>) =
        out.Add Format.Float32
        writeSignedNumber (BitConverter.GetBytes n) out

    let private writeDouble (n: float) (out: ResizeArray<byte>) =
        out.Add Format.Float64
        writeSignedNumber (BitConverter.GetBytes n) out

    let private writeBin (data: byte[]) (out: ResizeArray<byte>) =
        if data.Length < 256 then
            out.Add Format.Bin8
        elif data.Length < 65536 then
            out.Add Format.Bin16
        else
            out.Add Format.Bin32

        writeUnsigned32bitNumber (uint32 data.Length) out false

        out.AddRange data

    let inline private writeDateTime (out: ResizeArray<byte>) (dto: DateTime) =
        out.Add (Format.fixarr 2uy)
        writeInt64 dto.Ticks out
        writeInt64 (int64 dto.Kind) out

    let inline private writeDateTimeOffset (out: ResizeArray<byte>) (dto: DateTimeOffset) =
        out.Add (Format.fixarr 2uy)
        writeInt64 dto.Ticks out
        writeInt64 (int64 dto.Offset.TotalMinutes) out

#if NET6_0_OR_GREATER
    let inline private writeDateOnly (out: ResizeArray<byte>) (date: DateOnly) =
        writeUnsigned32bitNumber (uint32 date.DayNumber) out true

    let inline private writeTimeOnly (out: ResizeArray<byte>) (time: TimeOnly) =
        writeUInt64 (uint64 time.Ticks) out
#endif

    let private writeArrayHeader len (out: ResizeArray<byte>) =
        if len < 16 then
            out.Add (Format.fixarr len)
        elif len < 65536 then
            out.Add Format.Array16
            out.Add (len >>> 8 |> FSharp.Core.Operators.byte)
            out.Add (FSharp.Core.Operators.byte len)
        else
            out.Add Format.Array32
            writeUnsigned32bitNumber (uint32 len) out false

    let private writeDecimal (n: decimal) (out: ResizeArray<byte>) =
        let bits = Decimal.GetBits n
        
        out.Add (Format.fixarr 4)
        for b in bits do
            writeUnsigned32bitNumber (uint32 b) out true

    let rec private writeArray (out: ResizeArray<byte>) t (arr: System.Collections.ICollection) =
        writeArrayHeader arr.Count out

        for x in arr do
            writeObject x t out

    and private writeMap (out: ResizeArray<byte>) keyType valueType (dict: IDictionary<obj, obj>) =
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
            writeObject kvp.Key keyType out
            writeObject kvp.Value valueType out

    and private writeSet (out: ResizeArray<byte>) t (set: System.Collections.ICollection) =
        writeArrayHeader set.Count out

        for x in set do
            writeObject x t out

    and inline private writeRecord (out: ResizeArray<byte>) (types: Type[]) (vals: obj[]) =
        writeArrayHeader vals.Length out

        for i in 0 .. vals.Length - 1 do
            writeObject vals.[i] types.[i] out

    and inline private writeTuple (out: ResizeArray<byte>) (types: Type[]) (vals: obj[]) =
        writeRecord out types vals

    and private writeUnion (out: ResizeArray<byte>) tag (types: Type[]) (vals: obj[]) =
        if vals.Length = 0 then
            out.Add (Format.fixarr 1uy)
            out.Add (Format.fixposnum tag)
        else        
            out.Add (Format.fixarr 2uy)
            out.Add (Format.fixposnum tag)

            // write the field directly instead of using an array if the union case has a single field
            // saves 1 byte
            if vals.Length = 1 then
                writeObject vals.[0] types.[0] out
            else
                writeArrayHeader vals.Length out

                for i in 0 .. vals.Length - 1 do
                    writeObject vals.[i] types.[i] out

    and writeObject (x: obj) (t: Type) (out: ResizeArray<byte>) =
        #if !FABLE_COMPILER
        raise (NotSupportedException "This function is meant to be used in Fable, please use serializeObj or makeSerializer.")
        #else
        if isNull x then writeNil out else

        match serializerCache.TryGetValue (t.FullName) with
        | true, writer ->
            writer x out
        | _ ->
            if FSharpType.IsRecord (t, true) then
                let fieldTypes = FSharpType.GetRecordFields (t, true) |> Array.map (fun x -> x.PropertyType)
                cacheGetOrAdd (t, fun x out -> writeRecord out fieldTypes (FSharpValue.GetRecordFields (x, true))) x out
            elif t.IsArray then
                let elementType = t.GetElementType ()
                cacheGetOrAdd (t, fun x out -> writeArray out elementType (x :?> System.Collections.ICollection)) x out
            elif FSharpType.IsUnion (t, true) then
                cacheGetOrAdd (t, fun x out ->
                    let case, fields = FSharpValue.GetUnionFields (x, t, true)
                    let fieldTypes = case.GetFields () |> Array.map (fun x -> x.PropertyType)
                    writeUnion out case.Tag fieldTypes fields) x out
            elif FSharpType.IsTuple t then
                let fieldTypes = FSharpType.GetTupleElements t
                cacheGetOrAdd (t, fun x out -> writeTuple out fieldTypes (FSharpValue.GetTupleFields x)) x out
            elif t.IsEnum then
                cacheGetOrAdd (t, fun x -> writeInt64 (box x :?> int64)) x out
            elif t.IsGenericType then
                let tDef = t.GetGenericTypeDefinition()
                let genArgs = t.GetGenericArguments ()

                if tDef = typedefof<_ list> then
                    let elementType = genArgs |> Array.head
                    cacheGetOrAdd (t, fun x out -> writeArray out elementType (x :?> System.Collections.ICollection)) x out
                elif tDef = typedefof<_ option> then
                    cacheGetOrAdd (t, fun x out ->
                        let opt = x :?> _ option
                        let tag, values = if Option.isSome opt then 1, [| opt.Value |] else 0, [| |]
                        writeUnion out tag genArgs values) x out
                elif tDef = typedefof<Dictionary<_, _>> || tDef = typedefof<Map<_, _>> then
                    let keyType = genArgs.[0]
                    let valueType = genArgs.[1]
                    cacheGetOrAdd (t, fun x out -> writeMap out keyType valueType (box x :?> IDictionary<obj, obj>)) x out
                elif tDef = typedefof<Set<_>> then
                    let elementType = genArgs |> Array.head
                    cacheGetOrAdd (t, fun x out -> writeSet out elementType (x :?> System.Collections.ICollection)) x out
                else
                    failwithf "Cannot serialize %s." t.Name
            elif t.FullName = "Microsoft.FSharp.Core.int16`1" || t.FullName = "Microsoft.FSharp.Core.int32`1" || t.FullName = "Microsoft.FSharp.Core.int64`1" then
                cacheGetOrAdd (t, fun x out -> writeInt64 (x :?> int64) out) x out
            elif t.FullName = "Microsoft.FSharp.Core.decimal`1" then
                cacheGetOrAdd (t, fun x out -> writeDecimal (x :?> decimal) out) x out
            elif t.FullName = "Microsoft.FSharp.Core.float`1" then
                cacheGetOrAdd (t, fun x out -> writeDouble (x :?> float) out) x out
            elif t.FullName = "Microsoft.FSharp.Core.float32`1" then
                cacheGetOrAdd (t, fun x out -> writeSingle (x :?> float32) out) x out
            else
                failwithf "Cannot serialize %s." t.Name
        #endif

    let inline writeType<'T> (x: 'T) (out: ResizeArray<byte>) =
        #if !FABLE_COMPILER
        raise (NotSupportedException "This function is meant to be used in Fable, please use serializeObj or makeSerializer.")
        #else
        writeObject x typeof<'T> out
        #endif

#if FABLE_COMPILER
    serializerCache.Add (typeof<byte>.FullName, fun x out -> writeByte (x :?> byte) out)
    serializerCache.Add (typeof<sbyte>.FullName, fun x out -> writeByte (x :?> sbyte |> byte) out)
    serializerCache.Add (typeof<unit>.FullName, fun _ out -> writeNil out)
    serializerCache.Add (typeof<bool>.FullName, fun x out -> writeBool (x :?> bool) out)
    serializerCache.Add (typeof<char>.FullName, fun x out -> writeString (x :?> string) out) // There are only strings in JS
    serializerCache.Add (typeof<string>.FullName, fun x out -> writeString (x :?> string) out)
    serializerCache.Add (typeof<int>.FullName, fun x out -> writeInt64 (x :?> int |> int64) out)
    serializerCache.Add (typeof<int16>.FullName, fun x out -> writeInt64 (x :?> int16 |> int64) out)
    serializerCache.Add (typeof<int64>.FullName, fun x out -> writeInt64 (x :?> int64) out)
    serializerCache.Add (typeof<UInt32>.FullName, fun x out -> writeUInt64 (x :?> UInt32 |> uint64) out)
    serializerCache.Add (typeof<UInt16>.FullName, fun x out -> writeUInt64 (x :?> UInt16 |> uint64) out)
    serializerCache.Add (typeof<UInt64>.FullName, fun x out -> writeUInt64 (x :?> UInt64) out)
    serializerCache.Add (typeof<float32>.FullName, fun x out -> writeSingle (x :?> float32) out)
    serializerCache.Add (typeof<float>.FullName, fun x out -> writeDouble (x :?> float) out)
    serializerCache.Add (typeof<decimal>.FullName, fun x out -> writeDecimal (x :?> decimal) out)
    serializerCache.Add (typeof<byte[]>.FullName, fun x out -> writeBin (x :?> byte[]) out)
    serializerCache.Add (typeof<bigint>.FullName, fun x out -> writeBin ((x :?> bigint).ToByteArray ()) out)
    serializerCache.Add (typeof<Guid>.FullName, fun x out -> writeBin ((x :?> Guid).ToByteArray ()) out)
    serializerCache.Add (typeof<DateTime>.FullName, fun x out -> writeDateTime out (x :?> DateTime))
    serializerCache.Add (typeof<DateTimeOffset>.FullName, fun x out -> writeDateTimeOffset out (x :?> DateTimeOffset))
#if NET6_0_OR_GREATER
    serializerCache.Add (typeof<DateOnly>.FullName, fun x out -> writeDateOnly out (x :?> DateOnly))
    serializerCache.Add (typeof<TimeOnly>.FullName, fun x out -> writeTimeOnly out (x :?> TimeOnly))
#endif
    serializerCache.Add (typeof<TimeSpan>.FullName, fun x out -> writeInt64 (x :?> TimeSpan).Ticks out)
#endif
