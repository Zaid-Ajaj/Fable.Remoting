namespace Fable.Remoting.Json.SystemTextJson

open System
open System.Collections.Generic
open System.Collections.Concurrent
open System.Reflection
open System.Text.Json
open System.Text.Json.Serialization
open FSharp.Reflection

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

    // Some references to an F# DU type land on a compiler-generated subtype
    // rather than the canonical declaring type. Normalise so the cache holds
    // one entry per union regardless of how the type was first surfaced.
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

/// System.Text.Json converter for an F# discriminated union type 'T.
///
/// Wire format matches Fable.Remoting.Json.FableJsonConverter (the Newtonsoft
/// path) byte-for-byte:
///
///   No-field case  : "&lt;CaseName&gt;"
///   1-field case   : {"&lt;CaseName&gt;": &lt;field&gt;}
///   N-field case   : {"&lt;CaseName&gt;": [&lt;f1&gt;, ..., &lt;fN&gt;]}
///
/// See BYTE-COMPAT-MAP.md §3.9 for the five reader input shapes Newtonsoft
/// accepts. Phase 3 (this file) implements the writer-roundtrippable subset:
/// no-field "&lt;CaseName&gt;" strings and single-property object shape. Phase 4
/// will extend the reader to accept the __typename, {tag,name,fields} (Fable
/// runtime), and ["&lt;CaseName&gt;", &lt;f1&gt;, ...] string-prefixed-array input shapes.
type FSharpUnionConverter<'T>() =
    inherit JsonConverter<'T>()

    let info = UnionReflection.getInfo typeof<'T>

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
            // Materialise the object so we can look at the property name
            // before deciding how to read the value. Mirrors the Newtonsoft
            // path's JObject.ReadFrom approach.
            use doc = JsonDocument.ParseValue(&reader)
            let root = doc.RootElement
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
        | other ->
            failwithf "Unexpected token %A when reading union %s" other typeof<'T>.FullName

/// Factory producing typed FSharpUnionConverter&lt;T&gt; for any F# discriminated union.
///
/// Excludes FSharpList`1 and FSharpOption`1: the Newtonsoft path treats both
/// as non-union (lists fall through to default array handling; options have a
/// dedicated Kind.Option branch). Mirroring that here keeps the wire format
/// aligned and avoids fighting STJ's default IEnumerable handling for lists.
type FSharpUnionConverterFactory() =
    inherit JsonConverterFactory()

    static let bindingFlags = BindingFlags.Public ||| BindingFlags.NonPublic ||| BindingFlags.Instance

    static let isUnionTypeWeConvert (t: Type) =
        t.Name <> "FSharpList`1"
        && t.Name <> "FSharpOption`1"
        && FSharpType.IsUnion(t, bindingFlags)

    override _.CanConvert(typeToConvert: Type) = isUnionTypeWeConvert typeToConvert

    override _.CreateConverter(typeToConvert: Type, _options: JsonSerializerOptions) =
        let converterType = typedefof<FSharpUnionConverter<_>>.MakeGenericType(typeToConvert)
        Activator.CreateInstance(converterType) :?> JsonConverter
