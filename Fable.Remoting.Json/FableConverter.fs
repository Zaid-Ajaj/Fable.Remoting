namespace Fable.Remoting.Json

#if DOTNETCORE
[<AutoOpen>]
module ReflectionAdapters =
    open System.Reflection

    type System.Type with
        member this.IsValueType = this.GetTypeInfo().IsValueType
        member this.IsGenericType = this.GetTypeInfo().IsGenericType
        member this.GetMethod(name) = this.GetTypeInfo().GetMethod(name)
        member this.GetGenericArguments() = this.GetTypeInfo().GetGenericArguments()
        member this.MakeGenericType(args) = this.GetTypeInfo().MakeGenericType(args)
        member this.GetCustomAttributes(inherits : bool) : obj[] =
            downcast box(CustomAttributeExtensions.GetCustomAttributes(this.GetTypeInfo(), inherits) |> Seq.toArray)
#endif

open System
open System.Linq
open FSharp.Reflection
open Newtonsoft.Json
open System.Reflection
open System.Collections.Generic
open System.Collections.Concurrent
open Newtonsoft.Json.Linq

type Kind =
    | Other = 0
    | Option = 1
    | Tuple = 2
    | Union = 3
    | MutableRecord = 17
    | PojoDU = 4
    | StringEnum = 5
    | DateTime = 6
    | MapOrDictWithNonStringKey = 7
    | MapWithStringKey = 11
    | Long = 8
    | BigInt = 9
    | TimeSpan = 10
    | DataSet = 12
    | DataTable = 13
    | Nullable = 14
#if NET6_0_OR_GREATER
    | DateOnly = 15
    | TimeOnly = 16
#endif

module Utilities =
    let quoted (input: string) = input.StartsWith "\"" && input.EndsWith "\""

    let isNonStringPrimitiveType (inputType: Type) =
        inputType = typeof<DateTimeOffset>
        || inputType = typeof<DateTime>
#if NET6_0_OR_GREATER
        || inputType = typeof<DateOnly>
        || inputType = typeof<TimeOnly>
#endif
        || inputType = typeof<int64>
        || inputType = typeof<uint64>
        || inputType = typeof<int32>
        || inputType = typeof<uint32>
        || inputType = typeof<decimal>
        || inputType = typeof<int16>
        || inputType = typeof<uint16>
        || inputType = typeof<int8>
        || inputType = typeof<uint8>
        || inputType = typeof<bigint>
        || inputType = typeof<float>
        || inputType = typeof<byte>

type IMapSerializer =
    abstract member Serialize: obj * JsonWriter * JsonSerializer -> unit
    abstract member Deserialize: Type * JsonReader * JsonSerializer -> obj

module private Cache =
    type TupleInfo = {
        ElementReader: obj -> obj[]
        ElementTypes: Type[]
        Constructor: obj[] -> obj }

    type UnionCase = {
        Uci: UnionCaseInfo
        FieldReader: ValueOption<obj -> obj[]>
        FieldTypes: Type[]
        Constructor: obj[] -> obj }

    type UnionInfo = {
        TagReader: obj -> int
        Cases: UnionCase[]
        CaseByName: IReadOnlyDictionary<string, UnionCase> }

    let jsonConverterTypes = ConcurrentDictionary<Type,Kind>()
    let mapSerializerCache = ConcurrentDictionary<Type,IMapSerializer>()
    let tupleInfoCache = ConcurrentDictionary<Type,TupleInfo>()
    let unionTypeCache = ConcurrentDictionary<Type,Type>()
    let unionInfoCache = ConcurrentDictionary<Type,UnionInfo>()

open Cache

module private ReflectionHelpers =
    let bindingFlags = BindingFlags.Public ||| BindingFlags.NonPublic ||| BindingFlags.Instance

    let getTupleInfo (t:Type) =
        tupleInfoCache.GetOrAdd(t, Func<_,_>(fun t ->
            { ElementReader = FSharpValue.PreComputeTupleReader(t)
              ElementTypes = FSharpType.GetTupleElements(t)
              Constructor = FSharpValue.PreComputeTupleConstructor(t) }))

    let getUnionType (t:Type) =
        unionTypeCache.GetOrAdd(t, fun t -> FSharpType.GetUnionCases(t, bindingFlags).[0].DeclaringType)

    let getUnionInfo (t:Type) =
        unionInfoCache.GetOrAdd(getUnionType t, Func<_,_>(fun t ->
            let cases =
                FSharpType.GetUnionCases(t, bindingFlags)
                |> Array.map (fun uci ->
                    let fields = uci.GetFields()
                    let fieldReader =
                        if fields.Length > 0 then
                            FSharpValue.PreComputeUnionReader(uci, bindingFlags) |> ValueSome
                        else
                            ValueNone
                    { Uci = uci
                      FieldReader = fieldReader
                      FieldTypes = fields |> Array.map (fun pi -> pi.PropertyType)
                      Constructor = FSharpValue.PreComputeUnionConstructor(uci, bindingFlags) })
            { TagReader = FSharpValue.PreComputeUnionTagReader(t, bindingFlags)
              Cases = cases
              CaseByName = cases.ToDictionary((fun case -> case.Uci.Name), id) }))

    let getUnionCase value (union: UnionInfo) =
        union.Cases.[union.TagReader value]

    let getUnionCaseByName name (union: UnionInfo) =
        union.CaseByName.[name]

    let getUnionCaseInfo value (union: UnionInfo) =
        (getUnionCase value union).Uci

    let getUnionCaseInfoAndFields value (union: UnionInfo) =
        let unionCase = getUnionCase value union
        match unionCase.FieldReader with
        | ValueSome reader -> unionCase.Uci, reader value
        | _ -> unionCase.Uci, [||]

    let isUnionCaseWihoutFields (t: Type) name =
        if FSharpType.IsUnion t then
            let union = getUnionInfo t
            match union.CaseByName.TryGetValue name with
            | true, case -> case.FieldTypes.Length = 0
            | _ -> false
        else
            false

    let getUnionKind (t: Type) =
        t.GetCustomAttributes(false)
        |> Array.tryPick (fun o ->
            match o.GetType().FullName with
            | "Fable.Core.PojoAttribute" -> Some Kind.PojoDU
            | "Fable.Core.StringEnumAttribute" -> Some Kind.StringEnum
            | _ -> None)
        |> defaultArg <| Kind.Union

    let hasCliMutableAttribute (t: Type) = 
        t.GetCustomAttributes(false)
        |> Array.exists (fun attribute -> attribute.GetType().FullName.EndsWith "CLIMutableAttribute")
    
    let unionOfRecords (t: Type) =
        let union = getUnionInfo t
        union.Cases
        |> Array.forall (fun case ->
            case.FieldTypes.Length = 1 && FSharpType.IsRecord(case.FieldTypes.[0]))

open ReflectionHelpers

/// Helper for serializing map/dict with non-primitive, non-string keys such as unions and records.
/// Performs additional serialization/deserialization of the key object and uses the resulting JSON
/// representation of the key object as the string key in the serialized map/dict.
type MapSerializer<'k,'v when 'k : comparison>() =
    interface IMapSerializer with
        member _.Deserialize(t:Type, reader:JsonReader, serializer:JsonSerializer) =
            let jsonToken = JToken.ReadFrom(reader)
            if jsonToken.Type = JTokenType.Object then
                // use an intermediate dictionary to deserialize the values
                // where the keys are strings.
                // then deserialize the keys separately
                let initialDictionary = serializer.Deserialize<Dictionary<string,'v>>(jsonToken.CreateReader())
                let dictionary = Dictionary<'k,'v>()
                for kvp in initialDictionary do
                    if typeof<'k> = typeof<Guid> then
                        // remove quotes from the Guid
                        let cleanedGuid = kvp.Key.Replace("\"", "")
                        let parsedGuid = Guid.Parse(cleanedGuid)
                        dictionary.Add(unbox<'k> parsedGuid, kvp.Value)
                    else
                        let shouldQuoteKey =
                            not (Utilities.quoted kvp.Key)
                            && (isUnionCaseWihoutFields typeof<'k> kvp.Key || Utilities.isNonStringPrimitiveType typeof<'k>)
                        let quotedKey =
                            if shouldQuoteKey
                            then "\"" + kvp.Key + "\""
                            else kvp.Key
                        use tempReader = new System.IO.StringReader(quotedKey)
                        let key = serializer.Deserialize(tempReader, typeof<'k>) :?> 'k
                        dictionary.Add(key, kvp.Value)

                if t.IsGenericType && t.GetGenericTypeDefinition() = typedefof<Map<_,_>>
                then dictionary |> Seq.map (|KeyValue|) |> Map.ofSeq :> obj
                elif t.IsGenericType && t.GetGenericTypeDefinition() = typedefof<Dictionary<_,_>>
                then dictionary :> obj
                else failwith "MapSerializer input type wasn't a Map or a Dictionary"
            elif jsonToken.Type = JTokenType.Array then
                serializer.Deserialize<list<'k * 'v>>(jsonToken.CreateReader())
                |> Map.ofList :> obj
            else
                failwith "MapSerializer input type wasn't a Map or a Dictionary"

        member _.Serialize(value: obj, writer:JsonWriter, serializer:JsonSerializer) =
            let kvpSeq =
                match value with
                | :? Map<'k,'v> as mapObj -> mapObj |> Map.toSeq
                | :? Dictionary<'k,'v> as dictObj -> dictObj |> Seq.map (|KeyValue|)
                | _ -> failwith "MapSerializer input value wasn't a Map or a Dictionary"
            writer.WriteStartObject()
            use tempWriter = new System.IO.StringWriter()
            kvpSeq
                |> Seq.iter (fun (k,v) ->
                    let key =
                        tempWriter.GetStringBuilder().Clear() |> ignore
                        serializer.Serialize(tempWriter, k)
                        tempWriter.ToString()
                    writer.WritePropertyName(key)
                    serializer.Serialize(writer, v) )
            writer.WriteEndObject()

type MapStringKeySerializer<'v>() =
    interface IMapSerializer with
        member _.Deserialize(t:Type, reader:JsonReader, serializer:JsonSerializer) =
            let dictJson = JObject.ReadFrom(reader) :?> JObject
            let dictionary = Dictionary<string,'v>()
            for prop in dictJson.Properties() do
                let deserializedValue = serializer.Deserialize<'v>(prop.Value.CreateReader())
                dictionary.Add(prop.Name, deserializedValue)
            if t.IsGenericType && t.GetGenericTypeDefinition() = typedefof<Map<_,_>>
            then dictionary |> Seq.map (|KeyValue|) |> Map.ofSeq :> obj
            elif t.IsGenericType && t.GetGenericTypeDefinition() = typedefof<Dictionary<_,_>>
            then dictionary :> obj
            else failwith "MapSerializer input type wasn't a Map or a Dictionary"

        member _.Serialize(value: obj, writer:JsonWriter, serializer:JsonSerializer) =
            let kvpSeq =
                match value with
                | :? Map<string,'v> as mapObj -> mapObj |> Map.toSeq
                | :? Dictionary<string,'v> as dictObj -> dictObj |> Seq.map (|KeyValue|)
                | _ -> failwith "MapSerializer input value wasn't a Map or a Dictionary"
            writer.WriteStartObject()
            kvpSeq
            |> Seq.iter (fun (k,v) ->
                writer.WritePropertyName(k)
                serializer.Serialize(writer, v) )
            writer.WriteEndObject()

type DataSetSerializer() =
    static member Deserialize(t:Type, reader:JsonReader, serializer:JsonSerializer) =
        let jsonToken = JToken.ReadFrom(reader)
        if jsonToken.Type = JTokenType.Object then
            let dictionary = serializer.Deserialize<Dictionary<string,string>>(jsonToken.CreateReader())
            match dictionary.TryGetValue "schema",dictionary.TryGetValue "data" with
            | (true, schema), (true, data) ->
                if t = typeof<System.Data.DataSet> then
                    let dataset = new System.Data.DataSet()
                    dataset.ReadXmlSchema(new System.IO.StringReader(schema))
                    dataset.ReadXml(new System.IO.StringReader(data)) |> ignore
                    box dataset
                elif t = typeof<System.Data.DataTable> then
                    let table = new System.Data.DataTable()
                    table.ReadXmlSchema(new System.IO.StringReader(schema))
                    table.ReadXml(new System.IO.StringReader(data)) |> ignore
                    box table
                else
                    failwith "DataSetSerializer input type wasn't a DataSet"
            | _ -> failwith "DataSetSerializer input type wasn't a DataSet"
        else
            failwith "DataSetSerializer input type wasn't a DataSet"
    static member Serialize(value: obj, writer:JsonWriter, serializer:JsonSerializer) =
        let schema, data =
            match value with
            | :? System.Data.DataTable as table ->
                use stringWriter1 = new System.IO.StringWriter()
                use stringWriter2 = new System.IO.StringWriter()
                table.WriteXmlSchema stringWriter1
                table.WriteXml stringWriter2
                string stringWriter1, string stringWriter2
            | :? System.Data.DataSet as dataset ->
                use stringWriter1 = new System.IO.StringWriter()
                use stringWriter2 = new System.IO.StringWriter()
                dataset.WriteXmlSchema stringWriter1
                dataset.WriteXml stringWriter2
                string stringWriter1, string stringWriter2
            | _ -> failwith "DataSetSerializer input type wasn't a DataTable or a DataSet"
        writer.WriteStartObject()
        writer.WritePropertyName("schema")
        writer.WriteValue(schema)
        writer.WritePropertyName("data")
        writer.WriteValue(data)
        writer.WriteEndObject()

module private MapHelpers =
    let getMapSerializer (t: Type) =
        mapSerializerCache.GetOrAdd(t, fun _ ->
            let type' =
                let mapTypes = t.GetGenericArguments()
                typedefof<MapSerializer<_,_>>.MakeGenericType mapTypes
            Activator.CreateInstance(type') :?> IMapSerializer)

    let getMapStringKeySerializer (t: Type) =
        mapSerializerCache.GetOrAdd(t, fun _ ->
            let type' =
                let mapTypes = t.GetGenericArguments()
                let valueT = mapTypes.[1]
                typedefof<MapStringKeySerializer<_>>.MakeGenericType valueT
            Activator.CreateInstance(type') :?> IMapSerializer)

open MapHelpers

type InternalLong = { high : int; low: int; unsigned: bool }

/// Converts F# options, tuples and unions to a format understandable
/// by Fable. Code adapted from Lev Gorodinski's original.
/// See https://goo.gl/F6YiQk
type FableJsonConverter() =
    inherit Newtonsoft.Json.JsonConverter()

    let [<Literal>] PojoDU_TAG = "type"

    let advance(reader: JsonReader) =
        reader.Read() |> ignore

    let readElements(reader: JsonReader, itemTypes: Type[], serializer: JsonSerializer) =
        let rec read index acc =
            match reader.TokenType with
            | JsonToken.EndArray -> acc
            | _ ->
                let value = serializer.Deserialize(reader, itemTypes.[index])
                advance reader
                read (index + 1) (acc @ [value])
        advance reader
        read 0 List.empty

    override x.CanConvert(t) =
        let kind =
            jsonConverterTypes.GetOrAdd(t, fun t ->
                if t.FullName = "System.DateTime"
                then Kind.DateTime
                elif t.FullName = "System.TimeSpan"
                then Kind.TimeSpan
                elif t.Name = "FSharpOption`1"
                then Kind.Option
                elif t.Name = "Nullable`1"
                then Kind.Nullable
                elif t.FullName = "System.Int64" || t.FullName = "System.UInt64"
                then Kind.Long
                elif t.FullName = "System.Numerics.BigInteger"
                then Kind.BigInt
                elif FSharpType.IsTuple t
                then Kind.Tuple
                elif FSharpType.IsRecord t && hasCliMutableAttribute t
                then Kind.MutableRecord
                elif (FSharpType.IsUnion(t, bindingFlags) && t.Name <> "FSharpList`1")
                then getUnionKind t
                elif t.IsGenericType
                    && (t.GetGenericTypeDefinition() = typedefof<Map<_,_>> || t.GetGenericTypeDefinition() = typedefof<Dictionary<_,_>>)
                    && t.GetGenericArguments().[0] <> typeof<string>
                then
                    Kind.MapOrDictWithNonStringKey
                elif t.IsGenericType && (t.GetGenericTypeDefinition() = typedefof<Map<_,_>>)
                    then Kind.MapWithStringKey
                elif typeof<System.Data.DataTable>.IsAssignableFrom t
                    then Kind.DataTable
                elif typeof<System.Data.DataSet>.IsAssignableFrom t
                    then Kind.DataSet
#if NET6_0_OR_GREATER
                elif t.FullName = "System.TimeOnly"
                    then Kind.TimeOnly
                elif t.FullName = "System.DateOnly"
                    then Kind.DateOnly
#endif
                else Kind.Other)
        kind <> Kind.Other

    override x.WriteJson(writer, value, serializer) =
        if isNull value
        then serializer.Serialize(writer, value)
        else
            let t = value.GetType()
            match jsonConverterTypes.TryGetValue(t) with
            | false, _ ->
                serializer.Serialize(writer, value)
            | true, Kind.Long ->
                if t.FullName = "System.UInt64"
                then serializer.Serialize(writer, string value)
                else serializer.Serialize(writer, sprintf "%+i" (value :?> int64))
            | true, Kind.BigInt ->
                serializer.Serialize(writer, string value)
            | true, Kind.DateTime ->
                let dt = value :?> DateTime
                // Override .ToUniversalTime() behavior and assume DateTime.Kind = Unspecified as UTC values on serialization to avoid breaking roundtrips.
                // Make it up to user code to manage such values (see #613).
                let universalTime = if dt.Kind = DateTimeKind.Local then dt.ToUniversalTime() else dt
                // Make sure the DateTime is saved in UTC and ISO format (see #604)
                serializer.Serialize(writer, universalTime.ToString("O"))
            | true, Kind.TimeSpan ->
                let ts = value :?> TimeSpan
                let milliseconds = ts.TotalMilliseconds
                serializer.Serialize(writer, milliseconds)
            | true, Kind.Option ->
                let _, fields = getUnionInfo t |> getUnionCaseInfoAndFields value
                serializer.Serialize(writer, fields.[0])
            | true, Kind.Nullable ->
                serializer.Serialize(writer, value)
            | true, Kind.Tuple ->
                let tupleInfo = getTupleInfo t
                serializer.Serialize(writer, tupleInfo.ElementReader value)
            | true, Kind.PojoDU ->
                let uci, fields = getUnionInfo t |> getUnionCaseInfoAndFields value
                writer.WriteStartObject()
                writer.WritePropertyName(PojoDU_TAG)
                writer.WriteValue(uci.Name)
                Seq.zip (uci.GetFields()) fields
                |> Seq.iter (fun (fi, v) ->
                    writer.WritePropertyName(fi.Name)
                    serializer.Serialize(writer, v))
                writer.WriteEndObject()
            | true, Kind.MutableRecord -> 
                let properties = t.GetProperties(BindingFlags.Instance ||| BindingFlags.Public)
                writer.WriteStartObject()
                for property in properties do
                    let propertyValue = property.GetValue(value, null)
                    if not (isNull propertyValue) then
                        writer.WritePropertyName(property.Name)
                        serializer.Serialize(writer, propertyValue)
                writer.WriteEndObject()
            | true, Kind.StringEnum ->
                let uci = getUnionInfo t |> getUnionCaseInfo value
                // TODO: Should we cache the case-name pairs somewhere? (see also `ReadJson`)
                match uci.GetCustomAttributes(typeof<CompiledNameAttribute>) with
                | [|:? CompiledNameAttribute as att|] -> att.CompiledName
                | _ -> uci.Name.Substring(0,1).ToLowerInvariant() + uci.Name.Substring(1)
                |> writer.WriteValue
            | true, Kind.Union ->
                let uci, fields = getUnionInfo t |> getUnionCaseInfoAndFields value
                if fields.Length = 0
                then serializer.Serialize(writer, uci.Name)
                else
                    writer.WriteStartObject()
                    writer.WritePropertyName(uci.Name)
                    if fields.Length = 1
                    then serializer.Serialize(writer, fields.[0])
                    else serializer.Serialize(writer, fields)
                    writer.WriteEndObject()
            | true, Kind.MapOrDictWithNonStringKey ->
                let instance = getMapSerializer t
                instance.Serialize(value, writer, serializer)
            | true, Kind.MapWithStringKey ->
                let instance = getMapStringKeySerializer t
                instance.Serialize(value, writer, serializer)
            | true, Kind.DataTable
            | true, Kind.DataSet ->
                DataSetSerializer.Serialize(value, writer, serializer)
#if NET6_0_OR_GREATER
            | true, Kind.DateOnly ->
                (value :?> DateOnly).DayNumber |> writer.WriteValue
            | true, Kind.TimeOnly ->
                (value :?> TimeOnly).Ticks.ToString() |> writer.WriteValue
#endif
            | true, _ ->
                serializer.Serialize(writer, value)

    override x.ReadJson(reader, t, existingValue, serializer) =
        match jsonConverterTypes.TryGetValue(t) with
        | false, _ ->
            serializer.Deserialize(reader, t)
        | true, Kind.Long ->
            match reader.TokenType with
            | JsonToken.String ->
                let json = serializer.Deserialize(reader, typeof<string>) :?> string
                if t.FullName = "System.UInt64"
                then upcast UInt64.Parse(json)
                else upcast Int64.Parse(json)
            | JsonToken.Integer ->
                let data = JValue.Load(reader).ToObject<string>();
                if t.FullName = "System.UInt64"
                then upcast UInt64.Parse(data)
                else upcast Int64.Parse(data)

            | JsonToken.StartObject -> // reading { high: int, low: int, unsigned: bool }
                let internalLong = serializer.Deserialize(reader, typeof<InternalLong>) :?> InternalLong
                let lowBytes = BitConverter.GetBytes(internalLong.low)
                let highBytes = BitConverter.GetBytes(internalLong.high)
                let combinedBytes = Array.concat [ lowBytes; highBytes ]
                let combineBytesIntoInt64 = BitConverter.ToInt64(combinedBytes, 0)
                upcast combineBytesIntoInt64
            | token ->
                failwithf "Expecting int64 but instead %s" (Enum.GetName(typeof<JsonToken>, token))
        | true, Kind.BigInt ->
            match reader.TokenType with
            | JsonToken.String ->
                let json = serializer.Deserialize(reader, typeof<string>) :?> string
                upcast bigint.Parse(json)
            | JsonToken.Integer ->
                let i = serializer.Deserialize(reader, typeof<int>) :?> int
                upcast bigint i
            | token ->
                failwithf "Expecting bigint but got %s" <| Enum.GetName(typeof<JsonToken>, token)
        | true, Kind.DateTime ->
            match reader.Value with
            | :? DateTime -> reader.Value // Avoid culture-sensitive string roundtrip for already parsed dates (see #613).
            | _ ->
                let json = serializer.Deserialize(reader, typeof<string>) :?> string
                upcast DateTime.Parse(json)
        | true, Kind.TimeSpan ->
            match reader.Value with
            | :? TimeSpan -> reader.Value
            | _ ->
                let json = serializer.Deserialize(reader, typeof<float>) :?> float
                let ts = TimeSpan.FromMilliseconds json
                upcast ts
        | true, Kind.Option ->
            let cases = (getUnionInfo t).Cases
            match reader.TokenType with
            | JsonToken.Null ->
                serializer.Deserialize(reader, typeof<obj>) |> ignore
                cases.[0].Constructor [||]
            | _ ->
                let innerType = t.GetGenericArguments().[0]
                let innerType =
                    if innerType.IsValueType
                    then (typedefof<Nullable<_>>).MakeGenericType([|innerType|])
                    else innerType
                let value = serializer.Deserialize(reader, innerType)
                if isNull value
                then cases.[0].Constructor [||]
                else cases.[1].Constructor [|value|]

        | true, Kind.Nullable ->
            match reader.TokenType with
            | JsonToken.Null ->
                Activator.CreateInstance(t)
            | _ ->
                let innerType = t.GetGenericArguments().[0]
                let value = serializer.Deserialize(reader, innerType)
                Activator.CreateInstance(t, [|value|])
        | true, Kind.Tuple ->
            match reader.TokenType with
            | JsonToken.StartArray ->
                let tupleInfo = getTupleInfo t
                let values = readElements(reader, tupleInfo.ElementTypes, serializer)
                tupleInfo.Constructor (values |> List.toArray)
            | JsonToken.Null -> null // {"tuple": null}
            | _ -> failwith "invalid token"
        | true, Kind.PojoDU ->
            let dic = serializer.Deserialize(reader, typeof<Dictionary<string,obj>>) :?> Dictionary<string,obj>
            let uciName = dic.[PojoDU_TAG] :?> string
            let case = getUnionInfo t |> getUnionCaseByName uciName
            let fields = case.Uci.GetFields() |> Array.map (fun fi -> Convert.ChangeType(dic.[fi.Name], fi.PropertyType))
            case.Constructor fields
        | true, Kind.MutableRecord -> 
            let content = serializer.Deserialize<JObject> reader
            let fields = t.GetProperties() |> Array.map (fun property -> 
                match content.TryGetValue property.Name with
                | true, contentProp ->
                    contentProp.ToObject(property.PropertyType, serializer)
                | false, _ ->
                    null
            )

            Activator.CreateInstance(t, fields)
        | true, Kind.StringEnum ->
            let name = serializer.Deserialize(reader, typeof<string>) :?> string
            (getUnionInfo t).Cases
            |> Array.tryFind (fun case ->
                // TODO: Should we cache the case-name pairs somewhere? (see also `WriteJson`)
                let uci = case.Uci
                match uci.GetCustomAttributes(typeof<CompiledNameAttribute>) with
                | [|:? CompiledNameAttribute as att|] -> att.CompiledName = name
                | _ ->
                    let name2 = uci.Name.Substring(0,1).ToLowerInvariant() + uci.Name.Substring(1)
                    name = name2)
            |> function
                | Some case -> case.Constructor [||]
                | None -> failwithf "Cannot find case corresponding to '%s' for `StringEnum` type %s"
                                name t.FullName
        | true, Kind.Union ->
            match reader.TokenType with
            | JsonToken.String ->
                let name = serializer.Deserialize(reader, typeof<string>) :?> string
                let case = getUnionInfo t |> getUnionCaseByName name
                case.Constructor [||]
            | JsonToken.StartObject ->
                let content = serializer.Deserialize<JObject> reader
                if content.Count = 1 && not (content.ContainsKey "__typename") then
                    let firstProperty = content.Properties().First()
                    let name = firstProperty.Name
                    let case = getUnionInfo t |> getUnionCaseByName name

                    if case.FieldTypes.Length > 1 then
                        // Then assume we have an array containing
                        // the elements of the union case
                        let items =
                            firstProperty.Value
                            |> unbox<JArray>
                            |> Seq.toArray

                        let values =
                            case.FieldTypes
                            |> Array.zip items
                            |> Array.map (fun (item, itemType) -> serializer.Deserialize(item.CreateReader(), itemType))

                        case.Constructor values
                    else
                        let value = serializer.Deserialize(firstProperty.Value.CreateReader(), case.FieldTypes.[0])
                        case.Constructor [|value|]
                else if content.ContainsKey "__typename" && unionOfRecords t then
                    let property = content.Property("__typename")
                    let caseName = property.Value.ToObject<string>()
                    let case =
                        (getUnionInfo t).Cases
                        |> Array.find (fun case -> case.Uci.Name.ToUpper() = caseName.ToUpper())

                    let value = serializer.Deserialize(content.CreateReader(), case.FieldTypes.[0])
                    case.Constructor [|value|]
                else if content.Count = 3 && content.ContainsKey "tag" && content.ContainsKey "name" && content.ContainsKey "fields" then
                    let property = content.Property("name")
                    let caseName = property.Value.ToObject<string>()
                    let case = getUnionInfo t |> getUnionCaseByName caseName
                    if case.FieldTypes.Length > 1
                    then
                        let values = readElements(content.["fields"].CreateReader(), case.FieldTypes, serializer)
                        case.Constructor (List.toArray values)
                    else
                        let value = serializer.Deserialize(content.["fields"].[0].CreateReader(), case.FieldTypes.[0])
                        case.Constructor [|value|]
                else
                    failwith "Unsupported"
            | JsonToken.Null -> null // for { "union": null }
            | JsonToken.StartArray ->
                let unionArray = serializer.Deserialize<JToken>(reader) :?> JArray
                let name = unionArray.[0].Value<string>()
                let case = getUnionInfo t |> getUnionCaseByName name
                let values = Seq.skip 1 (unionArray.AsJEnumerable())
                let parsedValue =
                    [| 0 .. (case.FieldTypes.Length - 1) |]
                    |> Array.map (fun index ->
                        let value = Seq.item index values
                        value.ToObject(case.FieldTypes.[index], serializer))
                    |> fun unionCaseValues -> case.Constructor unionCaseValues
                parsedValue
            | _ -> failwithf "Invalid JSON token: %s" (reader.TokenType.ToString())
        | true, Kind.MapOrDictWithNonStringKey ->
            let instance = getMapSerializer t
            instance.Deserialize(t, reader, serializer)
        | true, Kind.MapWithStringKey ->
            if reader.TokenType = JsonToken.StartObject then
                let instance = getMapStringKeySerializer t
                instance.Deserialize(t, reader, serializer)
            else
                // map is encoded as [ [key, value] ] => rewrite as { key: value }
                let tuplesArray = serializer.Deserialize<JToken>(reader) :?> JArray
                let mapLiteral = JObject()
                for tuple in tuplesArray do
                    mapLiteral.Add(JProperty(tuple.[0].Value<string>(), tuple.[1]))
                let instance = getMapStringKeySerializer t
                instance.Deserialize(t, mapLiteral.CreateReader(), serializer)
        | true, Kind.DataTable
        | true, Kind.DataSet ->
            DataSetSerializer.Deserialize(t, reader, serializer)
#if NET6_0_OR_GREATER
        | true, Kind.DateOnly ->
            match reader.TokenType with
            | JsonToken.Integer ->
                // Newtonsoft.Json interprets integers as int64, but day number of a DateOnly is always within the range of int32
                reader.Value :?> int64 |> int |> DateOnly.FromDayNumber |> box
            | JsonToken.String ->
                // the day number is encoded as a string when used as a dictionary key
                reader.Value :?> string |> int |> DateOnly.FromDayNumber |> box
            | _ ->
                failwithf "Expecting day number for DateOnly but got %s" (Enum.GetName(typeof<JsonToken>, reader.TokenType))
        | true, Kind.TimeOnly ->
            reader.Value :?> string |> int64 |> TimeOnly |> box
#endif
        | true, _ ->
            serializer.Deserialize(reader, t)