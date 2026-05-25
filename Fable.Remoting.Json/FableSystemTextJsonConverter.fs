namespace Fable.Remoting.Json.SystemTextJson

open System
open System.Collections.Generic
open System.Collections.Concurrent
open System.IO
open System.Reflection
open System.Text
open System.Text.Encodings.Web
open System.Text.Json
open System.Text.Json.Serialization
open System.Text.Unicode
open FSharp.Reflection

// =============================================================================
// Reflection caches
// =============================================================================

[<AutoOpen>]
module private UnionReflection =
    let bindingFlags = BindingFlags.Public ||| BindingFlags.NonPublic ||| BindingFlags.Instance

    type UnionCase = {
        Uci: UnionCaseInfo
        FieldTypes: Type[]
        FieldReader: ValueOption<obj -> obj[]>
        Constructor: obj[] -> obj
    }

    type UnionInfo = {
        UnionType: Type
        TagReader: obj -> int
        Cases: UnionCase[]
        CaseByName: IReadOnlyDictionary<string, UnionCase>
    }

    let private cache = ConcurrentDictionary<Type, UnionInfo>()

    let private canonicalUnion (t: Type) =
        FSharpType.GetUnionCases(t, bindingFlags).[0].DeclaringType

    let getInfo (t: Type) : UnionInfo =
        cache.GetOrAdd(canonicalUnion t, fun union ->
            let cases =
                FSharpType.GetUnionCases(union, bindingFlags)
                |> Array.map (fun uci ->
                    let fields = uci.GetFields()
                    let reader =
                        if fields.Length > 0 then
                            FSharpValue.PreComputeUnionReader(uci, bindingFlags) |> ValueSome
                        else
                            ValueNone
                    {
                        Uci = uci
                        FieldTypes = fields |> Array.map (fun pi -> pi.PropertyType)
                        FieldReader = reader
                        Constructor = FSharpValue.PreComputeUnionConstructor(uci, bindingFlags)
                    })
            let byName =
                let d = Dictionary<string, UnionCase>(cases.Length)
                for c in cases do d.Add(c.Uci.Name, c)
                d :> IReadOnlyDictionary<_, _>
            {
                UnionType = union
                TagReader = FSharpValue.PreComputeUnionTagReader(union, bindingFlags)
                Cases = cases
                CaseByName = byName
            })

[<AutoOpen>]
module private RecordReflection =
    open System.Reflection

    type RecordInfo = {
        RecordType: Type
        FieldNames: string[]
        FieldTypes: Type[]
        Reader: obj -> obj[]
        Constructor: obj[] -> obj
        FieldIndexByName: IReadOnlyDictionary<string, int>
    }

    let private cache = ConcurrentDictionary<Type, RecordInfo>()

    let getInfo (t: Type) : RecordInfo =
        cache.GetOrAdd(t, fun t ->
            let fields = FSharpType.GetRecordFields(t, UnionReflection.bindingFlags)
            let nameIndex =
                let d = Dictionary<string, int>(fields.Length)
                for i in 0 .. fields.Length - 1 do d.Add(fields.[i].Name, i)
                d :> IReadOnlyDictionary<_, _>
            {
                RecordType = t
                FieldNames = fields |> Array.map (fun pi -> pi.Name)
                FieldTypes = fields |> Array.map (fun pi -> pi.PropertyType)
                Reader = FSharpValue.PreComputeRecordReader(t, UnionReflection.bindingFlags)
                Constructor = FSharpValue.PreComputeRecordConstructor(t, UnionReflection.bindingFlags)
                FieldIndexByName = nameIndex
            })

[<AutoOpen>]
module private TupleReflection =
    type TupleInfo = {
        TupleType: Type
        ElementTypes: Type[]
        Reader: obj -> obj[]
        Constructor: obj[] -> obj
    }

    let private cache = ConcurrentDictionary<Type, TupleInfo>()

    let getInfo (t: Type) : TupleInfo =
        cache.GetOrAdd(t, fun t ->
            {
                TupleType = t
                ElementTypes = FSharpType.GetTupleElements(t)
                Reader = FSharpValue.PreComputeTupleReader(t)
                Constructor = FSharpValue.PreComputeTupleConstructor(t)
            })

// =============================================================================
// F# Union converter (Kind.Union)
// =============================================================================
//
// Wire format (matches Fable.Remoting.Json.FableJsonConverter byte-for-byte):
//
//   No-field case  : "<CaseName>"
//   1-field case   : {"<CaseName>": <field>}
//   N-field case   : {"<CaseName>": [<f1>, ..., <fN>]}
//
// Reader accepts five input shapes per BYTE-COMPAT-MAP.md §3.9:
//   1. JsonTokenType.String          → no-field lookup by name
//   2. StartObject single property   → case = property name (writer's output)
//   3. StartObject with __typename   → union-of-records, case-insensitive match
//   4. StartObject {tag,name,fields} → Fable runtime form
//   5. StartArray ["<Case>", ...]    → string-prefixed array form

type FSharpUnionConverter<'T>() =
    inherit JsonConverter<'T>()

    let info = UnionReflection.getInfo typeof<'T>

    let unionOfRecords =
        info.Cases
        |> Array.forall (fun case ->
            case.FieldTypes.Length = 1 && FSharpType.IsRecord(case.FieldTypes.[0]))

    let lookupCaseInsensitive (name: string) =
        let upper = name.ToUpperInvariant()
        info.Cases |> Array.tryFind (fun c -> c.Uci.Name.ToUpperInvariant() = upper)

    override _.Write(writer: Utf8JsonWriter, value: 'T, options: JsonSerializerOptions) =
        let case = info.Cases.[info.TagReader (box value)]
        match case.FieldReader with
        | ValueNone ->
            writer.WriteStringValue(case.Uci.Name)
        | ValueSome reader ->
            let fields = reader (box value)
            writer.WriteStartObject()
            writer.WritePropertyName(case.Uci.Name)
            if fields.Length = 1 then
                JsonSerializer.Serialize(writer, fields.[0], case.FieldTypes.[0], options)
            else
                writer.WriteStartArray()
                for i in 0 .. fields.Length - 1 do
                    JsonSerializer.Serialize(writer, fields.[i], case.FieldTypes.[i], options)
                writer.WriteEndArray()
            writer.WriteEndObject()

    override _.Read(reader: byref<Utf8JsonReader>, _typeToConvert: Type, options: JsonSerializerOptions) =
        match reader.TokenType with
        | JsonTokenType.Null ->
            Unchecked.defaultof<'T>

        | JsonTokenType.String ->
            let name = reader.GetString()
            match info.CaseByName.TryGetValue(name) with
            | true, case -> case.Constructor [||] :?> 'T
            | false, _ ->
                failwithf "Unknown case '%s' for union type %s" name typeof<'T>.FullName

        | JsonTokenType.StartObject ->
            use doc = JsonDocument.ParseValue(&reader)
            let root = doc.RootElement

            // Shape 4: Fable runtime form {tag, name, fields}.
            let hasTagShape =
                let tagP, _ = root.TryGetProperty("tag")
                let nameP, _ = root.TryGetProperty("name")
                let fieldsP, _ = root.TryGetProperty("fields")
                tagP && nameP && fieldsP

            // Shape 3: __typename-keyed (union of records).
            let hasTypename, typenameElement = root.TryGetProperty("__typename")

            if hasTagShape then
                let caseName = root.GetProperty("name").GetString()
                let case =
                    match info.CaseByName.TryGetValue(caseName) with
                    | true, c -> c
                    | false, _ ->
                        failwithf "Unknown case '%s' (Fable-runtime shape) for union type %s" caseName typeof<'T>.FullName
                let fieldsArr = root.GetProperty("fields")
                if case.FieldTypes.Length = 0 then
                    case.Constructor [||] :?> 'T
                elif case.FieldTypes.Length = 1 then
                    // Per Newtonsoft path: single-field case reads fields.[0].
                    let element = fieldsArr.[0]
                    let v = element.Deserialize(case.FieldTypes.[0], options)
                    case.Constructor [| v |] :?> 'T
                else
                    let elements = fieldsArr.EnumerateArray() |> Seq.toArray
                    let values =
                        Array.init case.FieldTypes.Length (fun i ->
                            elements.[i].Deserialize(case.FieldTypes.[i], options))
                    case.Constructor values :?> 'T

            elif hasTypename && unionOfRecords then
                let caseName = typenameElement.GetString()
                let case =
                    match lookupCaseInsensitive caseName with
                    | Some c -> c
                    | None ->
                        failwithf "Unknown __typename '%s' for union type %s" caseName typeof<'T>.FullName
                // The whole root deserialises to the single record field.
                let v = root.Deserialize(case.FieldTypes.[0], options)
                case.Constructor [| v |] :?> 'T

            else
                // Shape 2: single-property writer roundtrip — {"<Case>": value-or-array}.
                let mutable enumerator = root.EnumerateObject()
                if not (enumerator.MoveNext()) then
                    failwithf "Empty object cannot be deserialised as union %s" typeof<'T>.FullName
                let prop = enumerator.Current
                let caseName = prop.Name
                match info.CaseByName.TryGetValue(caseName) with
                | true, case ->
                    let values =
                        if case.FieldTypes.Length = 0 then
                            [||]
                        elif case.FieldTypes.Length = 1 then
                            [| prop.Value.Deserialize(case.FieldTypes.[0], options) |]
                        else
                            if prop.Value.ValueKind <> JsonValueKind.Array then
                                failwithf
                                    "Union case '%s' of %s has %d fields but JSON value is %A, not an array"
                                    caseName typeof<'T>.FullName case.FieldTypes.Length prop.Value.ValueKind
                            let elements = prop.Value.EnumerateArray() |> Seq.toArray
                            if elements.Length <> case.FieldTypes.Length then
                                failwithf
                                    "Union case '%s' of %s expected %d fields, got %d"
                                    caseName typeof<'T>.FullName case.FieldTypes.Length elements.Length
                            Array.init case.FieldTypes.Length (fun i ->
                                elements.[i].Deserialize(case.FieldTypes.[i], options))
                    case.Constructor values :?> 'T
                | false, _ ->
                    failwithf "Unknown case '%s' for union type %s" caseName typeof<'T>.FullName

        | JsonTokenType.StartArray ->
            // Shape 5: ["<Case>", <f1>, ...].
            use doc = JsonDocument.ParseValue(&reader)
            let elements = doc.RootElement.EnumerateArray() |> Seq.toArray
            if elements.Length = 0 then
                failwithf "Empty array cannot be deserialised as union %s" typeof<'T>.FullName
            let caseName = elements.[0].GetString()
            let case =
                match info.CaseByName.TryGetValue(caseName) with
                | true, c -> c
                | false, _ ->
                    failwithf "Unknown case '%s' (array shape) for union type %s" caseName typeof<'T>.FullName
            if case.FieldTypes.Length = 0 then
                case.Constructor [||] :?> 'T
            else
                let values =
                    Array.init case.FieldTypes.Length (fun i ->
                        elements.[i + 1].Deserialize(case.FieldTypes.[i], options))
                case.Constructor values :?> 'T

        | other ->
            failwithf "Unexpected token %A when reading union %s" other typeof<'T>.FullName

/// Detect Fable.Core.PojoAttribute / StringEnumAttribute on a type without
/// requiring a reference to Fable.Core (matches by attribute FullName, same
/// approach as the Newtonsoft path's `getUnionKind` at
/// FableConverter.fs:156-163).
module private UnionAttributes =
    let hasPojoAttribute (t: Type) =
        t.GetCustomAttributes(false)
        |> Array.exists (fun a -> a.GetType().FullName = "Fable.Core.PojoAttribute")

    let hasStringEnumAttribute (t: Type) =
        t.GetCustomAttributes(false)
        |> Array.exists (fun a -> a.GetType().FullName = "Fable.Core.StringEnumAttribute")

type FSharpUnionConverterFactory() =
    inherit JsonConverterFactory()

    static let isUnionTypeWeConvert (t: Type) =
        t.Name <> "FSharpList`1"
        && t.Name <> "FSharpOption`1"
        && FSharpType.IsUnion(t, UnionReflection.bindingFlags)
        && not (UnionAttributes.hasPojoAttribute t)
        && not (UnionAttributes.hasStringEnumAttribute t)

    override _.CanConvert(typeToConvert: Type) = isUnionTypeWeConvert typeToConvert

    override _.CreateConverter(typeToConvert: Type, _options: JsonSerializerOptions) =
        let converterType = typedefof<FSharpUnionConverter<_>>.MakeGenericType(typeToConvert)
        Activator.CreateInstance(converterType) :?> JsonConverter

// =============================================================================
// F# `[<Fable.Core.Pojo>]` DU converter (Kind.PojoDU)
// =============================================================================
//
// Wire format (matches FableJsonConverter.fs:425-434 byte-for-byte):
//
//   {"type": "<CaseName>", "<Field1>": <v1>, "<Field2>": <v2>, ...}
//
// "type" is the case discriminator; remaining keys are the union case's
// declared field names (from FSharpType.GetUnionCases(t).[i].GetFields()).

type FSharpPojoDUConverter<'T>() =
    inherit JsonConverter<'T>()

    let info = UnionReflection.getInfo typeof<'T>
    let [<Literal>] PojoDuTag = "type"

    override _.Write(writer: Utf8JsonWriter, value: 'T, options: JsonSerializerOptions) =
        let case = info.Cases.[info.TagReader (box value)]
        writer.WriteStartObject()
        writer.WriteString(PojoDuTag, case.Uci.Name)
        match case.FieldReader with
        | ValueNone -> ()
        | ValueSome reader ->
            let fields = reader (box value)
            let fieldInfos = case.Uci.GetFields()
            for i in 0 .. fields.Length - 1 do
                writer.WritePropertyName(fieldInfos.[i].Name)
                JsonSerializer.Serialize(writer, fields.[i], case.FieldTypes.[i], options)
        writer.WriteEndObject()

    override _.Read(reader: byref<Utf8JsonReader>, _: Type, options: JsonSerializerOptions) =
        match reader.TokenType with
        | JsonTokenType.Null -> Unchecked.defaultof<'T>
        | JsonTokenType.StartObject ->
            use doc = JsonDocument.ParseValue(&reader)
            let root = doc.RootElement
            match root.TryGetProperty(PojoDuTag) with
            | true, typeElement ->
                let caseName = typeElement.GetString()
                let case =
                    match info.CaseByName.TryGetValue(caseName) with
                    | true, c -> c
                    | false, _ ->
                        failwithf "Unknown PojoDU case '%s' for union type %s" caseName typeof<'T>.FullName
                let values =
                    case.Uci.GetFields()
                    |> Array.mapi (fun i fi ->
                        match root.TryGetProperty(fi.Name) with
                        | true, fieldEl -> fieldEl.Deserialize(case.FieldTypes.[i], options)
                        | false, _ -> null)
                case.Constructor values :?> 'T
            | false, _ ->
                failwithf "PojoDU JSON missing 'type' discriminator for %s" typeof<'T>.FullName
        | other ->
            failwithf "Unexpected token %A when reading PojoDU %s" other typeof<'T>.FullName

type FSharpPojoDUConverterFactory() =
    inherit JsonConverterFactory()

    override _.CanConvert(t: Type) =
        FSharpType.IsUnion(t, UnionReflection.bindingFlags) && UnionAttributes.hasPojoAttribute t

    override _.CreateConverter(t: Type, _options: JsonSerializerOptions) =
        let converterType = typedefof<FSharpPojoDUConverter<_>>.MakeGenericType(t)
        Activator.CreateInstance(converterType) :?> JsonConverter

// =============================================================================
// F# `[<Fable.Core.StringEnum>]` DU converter (Kind.StringEnum)
// =============================================================================
//
// Wire format (matches FableJsonConverter.fs:444-450 byte-for-byte):
//
//   "<caseName>"   — default: case name with first char lowercased
//   "<compiled>"   — if the case has [<CompiledName "...">], that override
//
// Reader accepts either shape (CompiledName override + lowercased convention).

type FSharpStringEnumConverter<'T>() =
    inherit JsonConverter<'T>()

    let info = UnionReflection.getInfo typeof<'T>

    let nameForCase (uci: UnionCaseInfo) =
        match uci.GetCustomAttributes(typeof<CompiledNameAttribute>) with
        | [| :? CompiledNameAttribute as att |] -> att.CompiledName
        | _ -> uci.Name.Substring(0, 1).ToLowerInvariant() + uci.Name.Substring(1)

    override _.Write(writer: Utf8JsonWriter, value: 'T, _: JsonSerializerOptions) =
        let case = info.Cases.[info.TagReader (box value)]
        writer.WriteStringValue(nameForCase case.Uci)

    override _.Read(reader: byref<Utf8JsonReader>, _: Type, _: JsonSerializerOptions) =
        match reader.TokenType with
        | JsonTokenType.Null -> Unchecked.defaultof<'T>
        | JsonTokenType.String ->
            let wire = reader.GetString()
            let matched =
                info.Cases
                |> Array.tryFind (fun c -> nameForCase c.Uci = wire)
            match matched with
            | Some case -> case.Constructor [||] :?> 'T
            | None ->
                failwithf "Unknown StringEnum value '%s' for %s" wire typeof<'T>.FullName
        | other ->
            failwithf "Unexpected token %A when reading StringEnum %s" other typeof<'T>.FullName

type FSharpStringEnumConverterFactory() =
    inherit JsonConverterFactory()

    override _.CanConvert(t: Type) =
        FSharpType.IsUnion(t, UnionReflection.bindingFlags) && UnionAttributes.hasStringEnumAttribute t

    override _.CreateConverter(t: Type, _options: JsonSerializerOptions) =
        let converterType = typedefof<FSharpStringEnumConverter<_>>.MakeGenericType(t)
        Activator.CreateInstance(converterType) :?> JsonConverter

// =============================================================================
// F# Option converter (Kind.Option)
// =============================================================================
//
// Wire: Some x → JSON of x; None → null (handled by STJ default for ref types).

type FSharpOptionConverter<'T>() =
    inherit JsonConverter<'T option>()

    override _.Write(writer: Utf8JsonWriter, value: 'T option, options: JsonSerializerOptions) =
        match value with
        | Some inner ->
            JsonSerializer.Serialize(writer, inner, typeof<'T>, options)
        | None ->
            // Defensive — STJ's default null-handling means this is unlikely
            // to fire (None has runtime value null for the ref-typed Option<'T>).
            writer.WriteNullValue()

    override _.Read(reader: byref<Utf8JsonReader>, _: Type, options: JsonSerializerOptions) =
        match reader.TokenType with
        | JsonTokenType.Null -> None
        | _ ->
            let inner = JsonSerializer.Deserialize<'T>(&reader, options)
            if isNull (box inner) then None else Some inner

type FSharpOptionConverterFactory() =
    inherit JsonConverterFactory()

    override _.CanConvert(t: Type) =
        t.IsGenericType && t.GetGenericTypeDefinition() = typedefof<_ option>

    override _.CreateConverter(t: Type, _options: JsonSerializerOptions) =
        let innerType = t.GetGenericArguments().[0]
        let converterType = typedefof<FSharpOptionConverter<_>>.MakeGenericType(innerType)
        Activator.CreateInstance(converterType) :?> JsonConverter

// =============================================================================
// F# Tuple converter (Kind.Tuple)
// =============================================================================
//
// Wire: (a, b, c) → [a, b, c]. Each element serialised with its declared type.

type FSharpTupleConverter<'T>() =
    inherit JsonConverter<'T>()

    let info = TupleReflection.getInfo typeof<'T>

    override _.Write(writer: Utf8JsonWriter, value: 'T, options: JsonSerializerOptions) =
        let elements = info.Reader (box value)
        writer.WriteStartArray()
        for i in 0 .. elements.Length - 1 do
            JsonSerializer.Serialize(writer, elements.[i], info.ElementTypes.[i], options)
        writer.WriteEndArray()

    override _.Read(reader: byref<Utf8JsonReader>, _: Type, options: JsonSerializerOptions) =
        match reader.TokenType with
        | JsonTokenType.Null -> Unchecked.defaultof<'T>
        | JsonTokenType.StartArray ->
            use doc = JsonDocument.ParseValue(&reader)
            let elements = doc.RootElement.EnumerateArray() |> Seq.toArray
            let values =
                Array.init info.ElementTypes.Length (fun i ->
                    elements.[i].Deserialize(info.ElementTypes.[i], options))
            info.Constructor values :?> 'T
        | other ->
            failwithf "Unexpected token %A when reading tuple %s" other typeof<'T>.FullName

type FSharpTupleConverterFactory() =
    inherit JsonConverterFactory()

    override _.CanConvert(t: Type) = FSharpType.IsTuple t

    override _.CreateConverter(t: Type, _options: JsonSerializerOptions) =
        let converterType = typedefof<FSharpTupleConverter<_>>.MakeGenericType(t)
        Activator.CreateInstance(converterType) :?> JsonConverter

// =============================================================================
// F# Record converter (Kind.Other / Newtonsoft default for F# records)
// =============================================================================
//
// Wire: {"Field1": v1, "Field2": v2, ...} — declaration order.

type FSharpRecordConverter<'T>() =
    inherit JsonConverter<'T>()

    let info = RecordReflection.getInfo typeof<'T>

    override _.Write(writer: Utf8JsonWriter, value: 'T, options: JsonSerializerOptions) =
        let values = info.Reader (box value)
        writer.WriteStartObject()
        for i in 0 .. info.FieldNames.Length - 1 do
            writer.WritePropertyName(info.FieldNames.[i])
            JsonSerializer.Serialize(writer, values.[i], info.FieldTypes.[i], options)
        writer.WriteEndObject()

    override _.Read(reader: byref<Utf8JsonReader>, _: Type, options: JsonSerializerOptions) =
        match reader.TokenType with
        | JsonTokenType.Null -> Unchecked.defaultof<'T>
        | JsonTokenType.StartObject ->
            use doc = JsonDocument.ParseValue(&reader)
            let values = Array.zeroCreate<obj> info.FieldNames.Length
            for prop in doc.RootElement.EnumerateObject() do
                match info.FieldIndexByName.TryGetValue(prop.Name) with
                | true, idx ->
                    values.[idx] <- prop.Value.Deserialize(info.FieldTypes.[idx], options)
                | false, _ -> ()  // ignore extra fields
            info.Constructor values :?> 'T
        | other ->
            failwithf "Unexpected token %A when reading record %s" other typeof<'T>.FullName

type FSharpRecordConverterFactory() =
    inherit JsonConverterFactory()

    static let hasCliMutableAttribute (t: Type) =
        t.GetCustomAttributes(false)
        |> Array.exists (fun att -> att.GetType().FullName.EndsWith "CLIMutableAttribute")

    // Plain F# records only — CLIMutable records take a separate path because
    // Newtonsoft writes them via Type.GetProperties (different order possible)
    // AND omits null-valued properties.
    override _.CanConvert(t: Type) =
        FSharpType.IsRecord(t, UnionReflection.bindingFlags) && not (hasCliMutableAttribute t)

    override _.CreateConverter(t: Type, _options: JsonSerializerOptions) =
        let converterType = typedefof<FSharpRecordConverter<_>>.MakeGenericType(t)
        Activator.CreateInstance(converterType) :?> JsonConverter

// =============================================================================
// CLIMutable Record converter (Kind.MutableRecord)
// =============================================================================
//
// Wire: {"<Prop>": v, ...} via Type.GetProperties, NULL-VALUED PROPERTIES OMITTED.

type FSharpCliMutableRecordConverter<'T>() =
    inherit JsonConverter<'T>()

    let properties =
        typeof<'T>.GetProperties(BindingFlags.Instance ||| BindingFlags.Public)

    override _.Write(writer: Utf8JsonWriter, value: 'T, options: JsonSerializerOptions) =
        writer.WriteStartObject()
        for prop in properties do
            let v = prop.GetValue(value, null)
            if not (isNull v) then
                writer.WritePropertyName(prop.Name)
                JsonSerializer.Serialize(writer, v, prop.PropertyType, options)
        writer.WriteEndObject()

    override _.Read(reader: byref<Utf8JsonReader>, _: Type, options: JsonSerializerOptions) =
        match reader.TokenType with
        | JsonTokenType.Null -> Unchecked.defaultof<'T>
        | JsonTokenType.StartObject ->
            use doc = JsonDocument.ParseValue(&reader)
            let root = doc.RootElement
            let args =
                properties
                |> Array.map (fun prop ->
                    match root.TryGetProperty(prop.Name) with
                    | true, el -> el.Deserialize(prop.PropertyType, options)
                    | false, _ -> null)
            Activator.CreateInstance(typeof<'T>, args) :?> 'T
        | other ->
            failwithf "Unexpected token %A when reading CLIMutable record %s" other typeof<'T>.FullName

type FSharpCliMutableRecordConverterFactory() =
    inherit JsonConverterFactory()

    static let hasCliMutableAttribute (t: Type) =
        t.GetCustomAttributes(false)
        |> Array.exists (fun att -> att.GetType().FullName.EndsWith "CLIMutableAttribute")

    override _.CanConvert(t: Type) =
        FSharpType.IsRecord(t, UnionReflection.bindingFlags) && hasCliMutableAttribute t

    override _.CreateConverter(t: Type, _options: JsonSerializerOptions) =
        let converterType = typedefof<FSharpCliMutableRecordConverter<_>>.MakeGenericType(t)
        Activator.CreateInstance(converterType) :?> JsonConverter

// =============================================================================
// F# Set converter
// =============================================================================
//
// Wire: Set<T> → [v1, v2, ...]. F# Set's IEnumerable iterates in sorted order
// (since Set requires `comparison`).

type FSharpSetConverter<'T when 'T : comparison>() =
    inherit JsonConverter<Set<'T>>()

    override _.Write(writer: Utf8JsonWriter, value: Set<'T>, options: JsonSerializerOptions) =
        writer.WriteStartArray()
        for item in value do
            JsonSerializer.Serialize(writer, item, typeof<'T>, options)
        writer.WriteEndArray()

    override _.Read(reader: byref<Utf8JsonReader>, _: Type, options: JsonSerializerOptions) =
        match reader.TokenType with
        | JsonTokenType.Null -> Set.empty
        | JsonTokenType.StartArray ->
            use doc = JsonDocument.ParseValue(&reader)
            let mutable result = Set.empty
            for el in doc.RootElement.EnumerateArray() do
                let item = el.Deserialize<'T>(options)
                result <- Set.add item result
            result
        | other ->
            failwithf "Unexpected token %A when reading Set<%s>" other typeof<'T>.FullName

type FSharpSetConverterFactory() =
    inherit JsonConverterFactory()

    override _.CanConvert(t: Type) =
        t.IsGenericType && t.GetGenericTypeDefinition() = typedefof<Set<_>>

    override _.CreateConverter(t: Type, _options: JsonSerializerOptions) =
        let innerType = t.GetGenericArguments().[0]
        let converterType = typedefof<FSharpSetConverter<_>>.MakeGenericType(innerType)
        Activator.CreateInstance(converterType) :?> JsonConverter

// =============================================================================
// F# List converter
// =============================================================================
//
// Wire: list<T> → [v1, v2, ...]. F# list iterates in declaration order via
// IEnumerable. STJ defaults handle this correctly via IList<T> resolution,
// but an explicit converter lets us guarantee the per-element type dispatch
// and avoids ambiguity with the Union factory (FSharpList is also technically
// a union, hence the exclusion in FSharpUnionConverterFactory.CanConvert).

type FSharpListConverter<'T>() =
    inherit JsonConverter<'T list>()

    override _.Write(writer: Utf8JsonWriter, value: 'T list, options: JsonSerializerOptions) =
        writer.WriteStartArray()
        for item in value do
            JsonSerializer.Serialize(writer, item, typeof<'T>, options)
        writer.WriteEndArray()

    override _.Read(reader: byref<Utf8JsonReader>, _: Type, options: JsonSerializerOptions) =
        match reader.TokenType with
        | JsonTokenType.Null -> []
        | JsonTokenType.StartArray ->
            use doc = JsonDocument.ParseValue(&reader)
            doc.RootElement.EnumerateArray()
            |> Seq.map (fun el -> el.Deserialize<'T>(options))
            |> List.ofSeq
        | other ->
            failwithf "Unexpected token %A when reading %s list" other typeof<'T>.FullName

type FSharpListConverterFactory() =
    inherit JsonConverterFactory()

    override _.CanConvert(t: Type) =
        t.IsGenericType && t.GetGenericTypeDefinition() = typedefof<list<_>>

    override _.CreateConverter(t: Type, _options: JsonSerializerOptions) =
        let innerType = t.GetGenericArguments().[0]
        let converterType = typedefof<FSharpListConverter<_>>.MakeGenericType(innerType)
        Activator.CreateInstance(converterType) :?> JsonConverter

// =============================================================================
// Map<string, V> converter (Kind.MapWithStringKey)
// =============================================================================
//
// Wire: {"k": v, ...} — F# Map iterates keys in sorted order.

type FSharpMapStringKeyConverter<'V>() =
    inherit JsonConverter<Map<string, 'V>>()

    override _.Write(writer: Utf8JsonWriter, value: Map<string, 'V>, options: JsonSerializerOptions) =
        writer.WriteStartObject()
        for KeyValue(k, v) in value do
            writer.WritePropertyName(k)
            JsonSerializer.Serialize(writer, v, typeof<'V>, options)
        writer.WriteEndObject()

    override _.Read(reader: byref<Utf8JsonReader>, _: Type, options: JsonSerializerOptions) =
        match reader.TokenType with
        | JsonTokenType.Null -> Map.empty
        | JsonTokenType.StartObject ->
            use doc = JsonDocument.ParseValue(&reader)
            let mutable result = Map.empty
            for prop in doc.RootElement.EnumerateObject() do
                let v = prop.Value.Deserialize<'V>(options)
                result <- Map.add prop.Name v result
            result
        | JsonTokenType.StartArray ->
            // Accept array-of-pairs form: [["k", v], ...]
            use doc = JsonDocument.ParseValue(&reader)
            let mutable result = Map.empty
            for pair in doc.RootElement.EnumerateArray() do
                let elements = pair.EnumerateArray() |> Seq.toArray
                let k = elements.[0].GetString()
                let v = elements.[1].Deserialize<'V>(options)
                result <- Map.add k v result
            result
        | other ->
            failwithf "Unexpected token %A when reading Map<string,%s>" other typeof<'V>.FullName

type FSharpMapStringKeyConverterFactory() =
    inherit JsonConverterFactory()

    override _.CanConvert(t: Type) =
        t.IsGenericType
        && t.GetGenericTypeDefinition() = typedefof<Map<_,_>>
        && t.GetGenericArguments().[0] = typeof<string>

    override _.CreateConverter(t: Type, _options: JsonSerializerOptions) =
        let valueType = t.GetGenericArguments().[1]
        let converterType = typedefof<FSharpMapStringKeyConverter<_>>.MakeGenericType(valueType)
        Activator.CreateInstance(converterType) :?> JsonConverter

// =============================================================================
// Map<K, V> non-string-key converter (Kind.MapOrDictWithNonStringKey)
// =============================================================================
//
// Wire: writer serialises each key via STJ, takes the resulting JSON string
// (including surrounding quotes for primitives), and uses it verbatim as the
// property name. This produces escaped-quote property names like
// {"\"Red\"":10} for Map<Color,int>, matching Newtonsoft's wire format.

[<AbstractClass>]
type private NonStringKeyMapReaderHelper() =
    // Subclassed by FSharpMapNonStringKeyConverter<'K,'V> for the typed read path.
    abstract member ReadFromArray : JsonElement * JsonSerializerOptions -> obj
    abstract member ReadFromObject : JsonElement * JsonSerializerOptions -> obj

type FSharpMapNonStringKeyConverter<'K, 'V when 'K : comparison>() =
    inherit JsonConverter<Map<'K, 'V>>()

    let writerOptionsFor (options: JsonSerializerOptions) =
        // Copy the relevant encoder so key-side serialisation produces the same
        // raw-UTF-8 bytes the value-side does (no \uXXXX escapes).
        JsonWriterOptions(
            Encoder = (if isNull options.Encoder then JavaScriptEncoder.Default else options.Encoder),
            Indented = false,
            SkipValidation = false)

    let isUnionCaseWithoutFields (t: Type) (name: string) =
        if FSharpType.IsUnion(t, UnionReflection.bindingFlags) then
            let info = UnionReflection.getInfo t
            match info.CaseByName.TryGetValue(name) with
            | true, case -> case.FieldTypes.Length = 0
            | false, _ -> false
        else false

    let isNonStringPrimitive (t: Type) =
        t = typeof<DateTimeOffset>
        || t = typeof<DateTime>
        || t = typeof<int64> || t = typeof<uint64>
        || t = typeof<int32> || t = typeof<uint32>
        || t = typeof<int16> || t = typeof<uint16>
        || t = typeof<sbyte> || t = typeof<byte>
        || t = typeof<decimal>
        || t = typeof<System.Numerics.BigInteger>
        || t = typeof<float> || t = typeof<float32>

    let quoted (s: string) =
        s.StartsWith "\"" && s.EndsWith "\""

    override _.Write(writer: Utf8JsonWriter, value: Map<'K, 'V>, options: JsonSerializerOptions) =
        writer.WriteStartObject()
        for KeyValue(k, v) in value do
            // Serialise key via STJ to capture its JSON form, then use that
            // string verbatim as the property name. Newtonsoft does the same
            // by routing through a temp StringWriter.
            use stream = new MemoryStream()
            do
                use keyWriter = new Utf8JsonWriter(stream, writerOptionsFor options)
                JsonSerializer.Serialize(keyWriter, k, typeof<'K>, options)
            let keyJson = Encoding.UTF8.GetString(stream.ToArray())
            writer.WritePropertyName(keyJson)
            JsonSerializer.Serialize(writer, v, typeof<'V>, options)
        writer.WriteEndObject()

    override _.Read(reader: byref<Utf8JsonReader>, _: Type, options: JsonSerializerOptions) =
        match reader.TokenType with
        | JsonTokenType.Null -> Map.empty
        | JsonTokenType.StartObject ->
            use doc = JsonDocument.ParseValue(&reader)
            let mutable result = Map.empty
            for prop in doc.RootElement.EnumerateObject() do
                let key =
                    if typeof<'K> = typeof<Guid> then
                        let cleaned = prop.Name.Replace("\"", "")
                        box (Guid.Parse cleaned) :?> 'K
                    else
                        let shouldQuoteKey =
                            not (quoted prop.Name)
                            && (isUnionCaseWithoutFields typeof<'K> prop.Name
                                || isNonStringPrimitive typeof<'K>)
                        let quotedKey =
                            if shouldQuoteKey then "\"" + prop.Name + "\""
                            else prop.Name
                        JsonSerializer.Deserialize<'K>(quotedKey, options)
                let value = prop.Value.Deserialize<'V>(options)
                result <- Map.add key value result
            result
        | JsonTokenType.StartArray ->
            // Array-of-pairs form: [[k, v], ...] where each k uses its native JSON form.
            use doc = JsonDocument.ParseValue(&reader)
            let mutable result = Map.empty
            for pair in doc.RootElement.EnumerateArray() do
                let elements = pair.EnumerateArray() |> Seq.toArray
                let k = elements.[0].Deserialize<'K>(options)
                let v = elements.[1].Deserialize<'V>(options)
                result <- Map.add k v result
            result
        | other ->
            failwithf
                "Unexpected token %A when reading Map<%s,%s>"
                other typeof<'K>.FullName typeof<'V>.FullName

type FSharpMapNonStringKeyConverterFactory() =
    inherit JsonConverterFactory()

    override _.CanConvert(t: Type) =
        t.IsGenericType
        && t.GetGenericTypeDefinition() = typedefof<Map<_,_>>
        && t.GetGenericArguments().[0] <> typeof<string>

    override _.CreateConverter(t: Type, _options: JsonSerializerOptions) =
        let args = t.GetGenericArguments()
        let converterType = typedefof<FSharpMapNonStringKeyConverter<_,_>>.MakeGenericType(args.[0], args.[1])
        Activator.CreateInstance(converterType) :?> JsonConverter

// =============================================================================
// Int64 / UInt64 / BigInt (Kind.Long, Kind.BigInt)
// =============================================================================
//
// Wire: JSON string. Int64 has a leading '+' sign for non-negative; UInt64 does not.

type Int64Converter() =
    inherit JsonConverter<int64>()

    override _.Write(writer: Utf8JsonWriter, value: int64, _: JsonSerializerOptions) =
        writer.WriteStringValue(sprintf "%+i" value)

    override _.Read(reader: byref<Utf8JsonReader>, _: Type, _: JsonSerializerOptions) =
        match reader.TokenType with
        | JsonTokenType.String -> Int64.Parse(reader.GetString())
        | JsonTokenType.Number -> reader.GetInt64()
        | JsonTokenType.StartObject ->
            // Fable runtime form: {"high": int, "low": int, "unsigned": bool}
            use doc = JsonDocument.ParseValue(&reader)
            let root = doc.RootElement
            let low = root.GetProperty("low").GetInt32()
            let high = root.GetProperty("high").GetInt32()
            let lowBytes = BitConverter.GetBytes(low)
            let highBytes = BitConverter.GetBytes(high)
            BitConverter.ToInt64(Array.append lowBytes highBytes, 0)
        | other ->
            failwithf "Unexpected token %A when reading int64" other

type UInt64Converter() =
    inherit JsonConverter<uint64>()

    override _.Write(writer: Utf8JsonWriter, value: uint64, _: JsonSerializerOptions) =
        writer.WriteStringValue(string value)

    override _.Read(reader: byref<Utf8JsonReader>, _: Type, _: JsonSerializerOptions) =
        match reader.TokenType with
        | JsonTokenType.String -> UInt64.Parse(reader.GetString())
        | JsonTokenType.Number -> reader.GetUInt64()
        | JsonTokenType.StartObject ->
            use doc = JsonDocument.ParseValue(&reader)
            let root = doc.RootElement
            let low = root.GetProperty("low").GetInt32()
            let high = root.GetProperty("high").GetInt32()
            let lowBytes = BitConverter.GetBytes(low)
            let highBytes = BitConverter.GetBytes(high)
            BitConverter.ToUInt64(Array.append lowBytes highBytes, 0)
        | other ->
            failwithf "Unexpected token %A when reading uint64" other

type BigIntConverter() =
    inherit JsonConverter<System.Numerics.BigInteger>()

    override _.Write(writer: Utf8JsonWriter, value: System.Numerics.BigInteger, _: JsonSerializerOptions) =
        writer.WriteStringValue(string value)

    override _.Read(reader: byref<Utf8JsonReader>, _: Type, _: JsonSerializerOptions) =
        match reader.TokenType with
        | JsonTokenType.String -> System.Numerics.BigInteger.Parse(reader.GetString())
        | JsonTokenType.Number -> System.Numerics.BigInteger(reader.GetInt64())
        | other ->
            failwithf "Unexpected token %A when reading BigInteger" other

// =============================================================================
// DateTime / TimeSpan (Kind.DateTime, Kind.TimeSpan)
// =============================================================================
//
// DateTime wire: ISO-8601 round-trip ("O") with 7-digit fractional seconds.
//   Kind.Utc          → emits 'Z' suffix.
//   Kind.Local        → converted to UTC first → emits 'Z'.
//   Kind.Unspecified  → passes through unchanged → emits NO zone marker
//                       (BYTE-COMPAT-MAP §10.2 — surprise vs. source comment).
//
// TimeSpan wire: total milliseconds as JSON number (float).

type DateTimeConverter() =
    inherit JsonConverter<DateTime>()

    override _.Write(writer: Utf8JsonWriter, value: DateTime, _: JsonSerializerOptions) =
        let universal =
            if value.Kind = DateTimeKind.Local then value.ToUniversalTime() else value
        writer.WriteStringValue(universal.ToString("O", System.Globalization.CultureInfo.InvariantCulture))

    override _.Read(reader: byref<Utf8JsonReader>, _: Type, _: JsonSerializerOptions) =
        match reader.TokenType with
        | JsonTokenType.String -> DateTime.Parse(reader.GetString())
        | other -> failwithf "Unexpected token %A when reading DateTime" other

// =============================================================================
// String converter
// =============================================================================
//
// STJ's UnsafeRelaxedJsonEscaping still escapes supplementary-plane codepoints
// (emoji etc.) to \uXXXX\uXXXX surrogate pairs. Newtonsoft passes them through
// as raw UTF-8 bytes. To match byte-equally, we manually escape only the
// characters RFC 8259 requires (", \, control chars U+0000..U+001F), then
// use WriteRawValue to bypass STJ's encoder.

type StringConverter() =
    inherit JsonConverter<string>()

    static let appendEscaped (sb: StringBuilder) (c: char) =
        let cp = int c
        match cp with
        | 0x22 -> sb.Append('\\').Append('"') |> ignore
        | 0x5C -> sb.Append('\\').Append('\\') |> ignore
        | 0x08 -> sb.Append('\\').Append('b') |> ignore
        | 0x0C -> sb.Append('\\').Append('f') |> ignore
        | 0x0A -> sb.Append('\\').Append('n') |> ignore
        | 0x0D -> sb.Append('\\').Append('r') |> ignore
        | 0x09 -> sb.Append('\\').Append('t') |> ignore
        | cp when cp <= 0x1F -> sb.AppendFormat("\\u{0:x4}", cp) |> ignore
        | _ -> sb.Append(c) |> ignore

    override _.Write(writer: Utf8JsonWriter, value: string, _: JsonSerializerOptions) =
        if isNull value then
            writer.WriteNullValue()
        else
            let sb = StringBuilder(value.Length + 2)
            sb.Append('"') |> ignore
            for c in value do
                appendEscaped sb c
            sb.Append('"') |> ignore
            // skipInputValidation=true because we know the produced JSON string
            // is well-formed by construction.
            writer.WriteRawValue(sb.ToString(), true)

    override _.Read(reader: byref<Utf8JsonReader>, _: Type, _: JsonSerializerOptions) =
        match reader.TokenType with
        | JsonTokenType.Null -> null
        | JsonTokenType.String -> reader.GetString()
        | other -> failwithf "Unexpected token %A when reading string" other

module private DoubleFormat =
    /// Format a double the way Newtonsoft does: always preserve a decimal point
    /// for whole-valued doubles. STJ's WriteNumberValue(double) writes "0" for
    /// 0.0, but Newtonsoft writes "0.0" — the latter is the wire-format contract.
    let newtonsoftStyle (v: double) : string =
        let s = v.ToString("R", System.Globalization.CultureInfo.InvariantCulture)
        if s.Contains('.') || s.Contains('e') || s.Contains('E')
           || s = "NaN" || s = "Infinity" || s = "-Infinity"
        then s
        else s + ".0"

/// Double converter — matches Newtonsoft's preserve-trailing-zero behaviour
/// for whole-valued floats.
type DoubleConverter() =
    inherit JsonConverter<double>()

    override _.Write(writer: Utf8JsonWriter, value: double, _: JsonSerializerOptions) =
        let s : string = DoubleFormat.newtonsoftStyle value
        writer.WriteRawValue(s)

    override _.Read(reader: byref<Utf8JsonReader>, _: Type, _: JsonSerializerOptions) =
        reader.GetDouble()

type TimeSpanConverter() =
    inherit JsonConverter<TimeSpan>()

    override _.Write(writer: Utf8JsonWriter, value: TimeSpan, _: JsonSerializerOptions) =
        // TotalMilliseconds is a double; reuse the Newtonsoft-style format so
        // whole-millisecond TimeSpans round-trip as "X.0" not "X".
        let s : string = DoubleFormat.newtonsoftStyle value.TotalMilliseconds
        writer.WriteRawValue(s)

    override _.Read(reader: byref<Utf8JsonReader>, _: Type, _: JsonSerializerOptions) =
        match reader.TokenType with
        | JsonTokenType.Number -> TimeSpan.FromMilliseconds(reader.GetDouble())
        | other -> failwithf "Unexpected token %A when reading TimeSpan" other

// =============================================================================
// DateOnly / TimeOnly (NET6_0_OR_GREATER — present in our net8.0 target)
// =============================================================================
//
// DateOnly wire: day number as JSON integer.
// TimeOnly wire: ticks as JSON string.

type DateOnlyConverter() =
    inherit JsonConverter<DateOnly>()

    override _.Write(writer: Utf8JsonWriter, value: DateOnly, _: JsonSerializerOptions) =
        writer.WriteNumberValue(value.DayNumber)

    override _.Read(reader: byref<Utf8JsonReader>, _: Type, _: JsonSerializerOptions) =
        match reader.TokenType with
        | JsonTokenType.Number -> DateOnly.FromDayNumber(reader.GetInt32())
        | JsonTokenType.String -> DateOnly.FromDayNumber(Int32.Parse(reader.GetString()))
        | other -> failwithf "Unexpected token %A when reading DateOnly" other

type TimeOnlyConverter() =
    inherit JsonConverter<TimeOnly>()

    override _.Write(writer: Utf8JsonWriter, value: TimeOnly, _: JsonSerializerOptions) =
        writer.WriteStringValue(string value.Ticks)

    override _.Read(reader: byref<Utf8JsonReader>, _: Type, _: JsonSerializerOptions) =
        match reader.TokenType with
        | JsonTokenType.String -> TimeOnly(Int64.Parse(reader.GetString()))
        | other -> failwithf "Unexpected token %A when reading TimeOnly" other

// =============================================================================
// DataTable / DataSet
// =============================================================================
//
// Wire: {"schema": "<xml>", "data": "<xml>"} — schema is the result of
// WriteXmlSchema; data is WriteXml.

type DataTableConverter() =
    inherit JsonConverter<System.Data.DataTable>()

    override _.Write(writer: Utf8JsonWriter, value: System.Data.DataTable, _: JsonSerializerOptions) =
        use schemaWriter = new StringWriter()
        use dataWriter = new StringWriter()
        value.WriteXmlSchema(schemaWriter)
        value.WriteXml(dataWriter)
        writer.WriteStartObject()
        writer.WriteString("schema", schemaWriter.ToString())
        writer.WriteString("data", dataWriter.ToString())
        writer.WriteEndObject()

    override _.Read(reader: byref<Utf8JsonReader>, _: Type, _: JsonSerializerOptions) =
        match reader.TokenType with
        | JsonTokenType.Null -> null
        | JsonTokenType.StartObject ->
            use doc = JsonDocument.ParseValue(&reader)
            let schema = doc.RootElement.GetProperty("schema").GetString()
            let data = doc.RootElement.GetProperty("data").GetString()
            let table = new System.Data.DataTable()
            table.ReadXmlSchema(new StringReader(schema))
            table.ReadXml(new StringReader(data)) |> ignore
            table
        | other ->
            failwithf "Unexpected token %A when reading DataTable" other

type DataSetConverter() =
    inherit JsonConverter<System.Data.DataSet>()

    override _.Write(writer: Utf8JsonWriter, value: System.Data.DataSet, _: JsonSerializerOptions) =
        use schemaWriter = new StringWriter()
        use dataWriter = new StringWriter()
        value.WriteXmlSchema(schemaWriter)
        value.WriteXml(dataWriter)
        writer.WriteStartObject()
        writer.WriteString("schema", schemaWriter.ToString())
        writer.WriteString("data", dataWriter.ToString())
        writer.WriteEndObject()

    override _.Read(reader: byref<Utf8JsonReader>, _: Type, _: JsonSerializerOptions) =
        match reader.TokenType with
        | JsonTokenType.Null -> null
        | JsonTokenType.StartObject ->
            use doc = JsonDocument.ParseValue(&reader)
            let schema = doc.RootElement.GetProperty("schema").GetString()
            let data = doc.RootElement.GetProperty("data").GetString()
            let dataset = new System.Data.DataSet()
            dataset.ReadXmlSchema(new StringReader(schema))
            dataset.ReadXml(new StringReader(data)) |> ignore
            dataset
        | other ->
            failwithf "Unexpected token %A when reading DataSet" other

// =============================================================================
// Setup helper — register the full converter set on a JsonSerializerOptions
// =============================================================================
//
// Order matters: more-specific factories first (Option, Tuple, Set, Map*, List
// before Union/Record), so STJ's CanConvert scan picks the right converter.
// The encoder is forced to UnsafeRelaxedJsonEscaping to match Newtonsoft's
// raw-UTF-8 passthrough behaviour (BYTE-COMPAT-MAP §10.1).

module FableConverters =
    /// Register the full Fable.Remoting STJ converter set on an existing
    /// JsonSerializerOptions. The encoder is overwritten to
    /// UnsafeRelaxedJsonEscaping for byte-compat with Newtonsoft's wire format.
    let addTo (options: JsonSerializerOptions) : unit =
        // UnsafeRelaxedJsonEscaping is the closest STJ pre-built encoder to
        // Newtonsoft's behaviour for most characters (no escaping of +, <, >,
        // &, ', and inline " uses \" not "). It still escapes
        // supplementary-plane codepoints (e.g. emoji surrogate pairs) to
        // \uXXXX\uXXXX, so the explicit StringConverter below handles strings
        // via WriteRawValue to bypass the encoder entirely. See
        // BYTE-COMPAT-MAP.md §12.
        options.Encoder <- JavaScriptEncoder.UnsafeRelaxedJsonEscaping

        // Factories — order from most-specific to most-general.
        options.Converters.Add(FSharpOptionConverterFactory())
        options.Converters.Add(FSharpListConverterFactory())
        options.Converters.Add(FSharpSetConverterFactory())
        options.Converters.Add(FSharpMapStringKeyConverterFactory())
        options.Converters.Add(FSharpMapNonStringKeyConverterFactory())
        options.Converters.Add(FSharpTupleConverterFactory())
        options.Converters.Add(FSharpCliMutableRecordConverterFactory())
        options.Converters.Add(FSharpRecordConverterFactory())
        // Pojo + StringEnum DU factories must come BEFORE the regular union
        // factory so the attribute-tagged DUs are caught first (the regular
        // union factory's CanConvert excludes them, but ordering guards
        // against future factory edits that might forget the exclusion).
        options.Converters.Add(FSharpPojoDUConverterFactory())
        options.Converters.Add(FSharpStringEnumConverterFactory())
        options.Converters.Add(FSharpUnionConverterFactory())

        // Single-type converters — strings (raw UTF-8 passthrough), then numbers/dates.
        options.Converters.Add(StringConverter())
        options.Converters.Add(Int64Converter())
        options.Converters.Add(UInt64Converter())
        options.Converters.Add(BigIntConverter())
        options.Converters.Add(DoubleConverter())
        options.Converters.Add(DateTimeConverter())
        options.Converters.Add(TimeSpanConverter())
        options.Converters.Add(DateOnlyConverter())
        options.Converters.Add(TimeOnlyConverter())
        options.Converters.Add(DataTableConverter())
        options.Converters.Add(DataSetConverter())

    /// Create a fresh JsonSerializerOptions with the full Fable.Remoting STJ
    /// converter set registered.
    let create () : JsonSerializerOptions =
        let opts = JsonSerializerOptions()
        addTo opts
        opts
