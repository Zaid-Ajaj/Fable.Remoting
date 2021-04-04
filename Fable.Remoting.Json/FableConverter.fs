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
open Newtonsoft.Json.Linq

type Kind =
    | Other = 0
    | Option = 1
    | Tuple = 2
    | Union = 3
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

module Utilities =
    let quoted (input: string) = input.StartsWith "\"" && input.EndsWith "\""
    let isUnionCaseWihoutFields (inputType: Type) (caseName: string) =
        if FSharpType.IsUnion inputType then
            let cases = FSharpType.GetUnionCases(inputType)
            let foundCase =
                cases
                |> Array.tryFind (fun case -> case.Name = caseName)
            match foundCase with
            | None -> false
            | Some case -> Seq.isEmpty (case.GetFields())
        else
            false

/// Helper for serializing map/dict with non-primitive, non-string keys such as unions and records.
/// Performs additional serialization/deserialization of the key object and uses the resulting JSON
/// representation of the key object as the string key in the serialized map/dict.
type MapSerializer<'k,'v when 'k : comparison>() =
    static member Deserialize(t:Type, reader:JsonReader, serializer:JsonSerializer) =
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
                    let quotedKey =
                        if not (Utilities.quoted kvp.Key) && Utilities.isUnionCaseWihoutFields typeof<'k> kvp.Key
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
    static member Serialize(value: obj, writer:JsonWriter, serializer:JsonSerializer) =
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
    static member Deserialize(t:Type, reader:JsonReader, serializer:JsonSerializer) =
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
    static member Serialize(value: obj, writer:JsonWriter, serializer:JsonSerializer) =
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
module private Cache =
    let jsonConverterTypes = ConcurrentDictionary<Type,Kind>()
    let serializationBinderTypes = ConcurrentDictionary<string,Type>()
    let unionCaseInfoCache = ConcurrentDictionary<Type,string*PropertyInfo array>()

open Cache
open Newtonsoft.Json.Linq

 type InternalLong = { high : int; low: int; unsigned: bool }

/// Converts F# options, tuples and unions to a format understandable
/// by Fable. Code adapted from Lev Gorodinski's original.
/// See https://goo.gl/F6YiQk
type FableJsonConverter() =
    inherit Newtonsoft.Json.JsonConverter()

    let bindingFlags = BindingFlags.Public ||| BindingFlags.NonPublic ||| BindingFlags.Instance
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

    let getUnionKind (t: Type) =
        t.GetCustomAttributes(false)
        |> Seq.tryPick (fun o ->
            match o.GetType().FullName with
            | "Fable.Core.PojoAttribute" -> Some Kind.PojoDU
            | "Fable.Core.StringEnumAttribute" -> Some Kind.StringEnum
            | _ -> None)
        |> defaultArg <| Kind.Union

    let unionOfRecords (t: Type) =
        FSharpType.GetUnionCases(t, bindingFlags)
        |> Seq.forall (fun case ->
            let fields = case.GetFields()
            fields.Length = 1 && FSharpType.IsRecord(fields.[0].PropertyType))

    let getUci t name =
        FSharpType.GetUnionCases(t, true)
        |> Array.find (fun uci -> uci.Name = name)


    let getUnionCaseNameAndFields value (t:Type) =
        // The type-based caching doesn't work for struct unions, because all cases share one type.
        if t.IsValueType then
            let uci, fields = FSharpValue.GetUnionFields(value, t, true)
            let uciName = uci.Name
            uciName, fields
        else
            match unionCaseInfoCache.TryGetValue t with
            | true, (uciName,fieldPropInfos) ->
                let fields = [|
                    for p in fieldPropInfos -> p.GetValue(value)
                |]
                uciName, fields
            | false, _ ->
                let uci, fields = FSharpValue.GetUnionFields(value, t, true)
                let uciName = uci.Name
                // cases without fields don't have distinct types -> don't cache them.
                if fields.Length > 0 then
                    let fieldPropInfos = uci.GetFields()
                    unionCaseInfoCache.[t] <- (uciName,fieldPropInfos)
                uciName, fields

    override x.CanConvert(t) =
        let kind =
            jsonConverterTypes.GetOrAdd(t, fun t ->
                if t.FullName = "System.DateTime"
                then Kind.DateTime
                elif t.FullName = "System.TimeSpan"
                then Kind.TimeSpan
                elif t.Name = "FSharpOption`1"
                then Kind.Option
                elif t.FullName = "System.Int64" || t.FullName = "System.UInt64"
                then Kind.Long
                elif t.FullName = "System.Numerics.BigInteger"
                then Kind.BigInt
                elif FSharpType.IsTuple t
                then Kind.Tuple
                elif (FSharpType.IsUnion(t, BindingFlags.Instance ||| BindingFlags.NonPublic ||| BindingFlags.Public) && t.Name <> "FSharpList`1")
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
                let _,fields = FSharpValue.GetUnionFields(value, t, true)
                serializer.Serialize(writer, fields.[0])
            | true, Kind.Tuple ->
                let values = FSharpValue.GetTupleFields(value)
                serializer.Serialize(writer, values)
            | true, Kind.PojoDU ->
                let uci, fields = FSharpValue.GetUnionFields(value, t,true)
                writer.WriteStartObject()
                writer.WritePropertyName(PojoDU_TAG)
                writer.WriteValue(uci.Name)
                Seq.zip (uci.GetFields()) fields
                |> Seq.iter (fun (fi, v) ->
                    writer.WritePropertyName(fi.Name)
                    serializer.Serialize(writer, v))
                writer.WriteEndObject()
            | true, Kind.StringEnum ->
                let uci, _ = FSharpValue.GetUnionFields(value, t, true)
                // TODO: Should we cache the case-name pairs somewhere? (see also `ReadJson`)
                match uci.GetCustomAttributes(typeof<CompiledNameAttribute>) with
                | [|:? CompiledNameAttribute as att|] -> att.CompiledName
                | _ -> uci.Name.Substring(0,1).ToLowerInvariant() + uci.Name.Substring(1)
                |> writer.WriteValue
            | true, Kind.Union ->
                let uciName, fields = getUnionCaseNameAndFields value t
                if fields.Length = 0
                then serializer.Serialize(writer, uciName)
                else
                    writer.WriteStartObject()
                    writer.WritePropertyName(uciName)
                    if fields.Length = 1
                    then serializer.Serialize(writer, fields.[0])
                    else serializer.Serialize(writer, fields)
                    writer.WriteEndObject()
            | true, Kind.MapOrDictWithNonStringKey ->
                let mapTypes = t.GetGenericArguments()
                let mapSerializer = typedefof<MapSerializer<_,_>>.MakeGenericType mapTypes
                let mapSerializeMethod = mapSerializer.GetMethod("Serialize")
                mapSerializeMethod.Invoke(null, [| value; writer; serializer |]) |> ignore
            | true, Kind.MapWithStringKey ->
                let mapTypes = t.GetGenericArguments()
                let valueT = mapTypes.[1]
                let mapSerializer = typedefof<MapStringKeySerializer<_>>.MakeGenericType valueT
                let mapSerializeMethod = mapSerializer.GetMethod("Serialize")
                mapSerializeMethod.Invoke(null, [| value; writer; serializer |]) |> ignore
            | true, Kind.DataTable
            | true, Kind.DataSet ->
                DataSetSerializer.Serialize(value, writer, serializer)
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
                let i = serializer.Deserialize(reader, typeof<int>) :?> int
                if t.FullName = "System.UInt64"
                then upcast System.Convert.ToUInt64(i)
                else upcast System.Convert.ToInt64(i)
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
                let json = serializer.Deserialize(reader, typeof<int>) :?> int
                let ts = TimeSpan.FromMilliseconds (float json)
                upcast ts
        | true, Kind.Option ->
            let cases = FSharpType.GetUnionCases(t, true)
            match reader.TokenType with
            | JsonToken.Null ->
                serializer.Deserialize(reader, typeof<obj>) |> ignore
                FSharpValue.MakeUnion(cases.[0], [||], bindingFlags)
            | _ ->
                let innerType = t.GetGenericArguments().[0]
                let innerType =
                    if innerType.IsValueType
                    then (typedefof<Nullable<_>>).MakeGenericType([|innerType|])
                    else innerType
                let value = serializer.Deserialize(reader, innerType)
                if isNull value
                then FSharpValue.MakeUnion(cases.[0], [||], bindingFlags)
                else FSharpValue.MakeUnion(cases.[1], [|value|], bindingFlags)
        | true, Kind.Tuple ->
            match reader.TokenType with
            | JsonToken.StartArray ->
                let values = readElements(reader, FSharpType.GetTupleElements(t), serializer)
                FSharpValue.MakeTuple(values |> List.toArray, t)
            | JsonToken.Null -> null // {"tuple": null}
            | _ -> failwith "invalid token"
        | true, Kind.PojoDU ->
            let dic = serializer.Deserialize(reader, typeof<Dictionary<string,obj>>) :?> Dictionary<string,obj>
            let uciName = dic.[PojoDU_TAG] :?> string
            let uci = getUci t uciName
            let fields = uci.GetFields() |> Array.map (fun fi -> Convert.ChangeType(dic.[fi.Name], fi.PropertyType))
            FSharpValue.MakeUnion(uci, fields, bindingFlags)
        | true, Kind.StringEnum ->
            let name = serializer.Deserialize(reader, typeof<string>) :?> string
            FSharpType.GetUnionCases(t, true)
            |> Array.tryFind (fun uci ->
                // TODO: Should we cache the case-name pairs somewhere? (see also `WriteJson`)
                match uci.GetCustomAttributes(typeof<CompiledNameAttribute>) with
                | [|:? CompiledNameAttribute as att|] -> att.CompiledName = name
                | _ ->
                    let name2 = uci.Name.Substring(0,1).ToLowerInvariant() + uci.Name.Substring(1)
                    name = name2)
            |> function
                | Some uci -> FSharpValue.MakeUnion(uci, [||], bindingFlags )
                | None -> failwithf "Cannot find case corresponding to '%s' for `StringEnum` type %s"
                                name t.FullName
        | true, Kind.Union ->
            match reader.TokenType with
            | JsonToken.String ->
                let name = serializer.Deserialize(reader, typeof<string>) :?> string
                FSharpValue.MakeUnion(getUci t name, [||], bindingFlags)
            | JsonToken.StartObject ->
                let content = serializer.Deserialize<JObject> reader
                if content.Count = 1 && not (content.ContainsKey "__typename") then
                    let firstProperty = content.Properties().First()
                    let name = firstProperty.Name
                    let uci = getUci t name

                    let itemTypes = uci.GetFields() |> Array.map (fun pi -> pi.PropertyType)
                    if itemTypes.Length > 1 then
                        // Then assume we have an array containing
                        // the elements of the union case
                        let items =
                            firstProperty.Value
                            |> unbox<JArray>
                            |> Seq.toArray

                        let values =
                            itemTypes
                            |> Array.zip items
                            |> Array.map (fun (item, itemType) -> serializer.Deserialize(item.CreateReader(), itemType))

                        FSharpValue.MakeUnion(uci, values, bindingFlags)
                    else
                        let value = serializer.Deserialize(firstProperty.Value.CreateReader(), itemTypes.[0])
                        FSharpValue.MakeUnion(uci, [|value|], bindingFlags)
                else if content.ContainsKey "__typename" && unionOfRecords t then
                    let property = content.Property("__typename")
                    let caseName = property.Value.ToObject<string>()
                    let uci =
                        FSharpType.GetUnionCases(t, true)
                        |> Array.find (fun uci -> uci.Name.ToUpper() = caseName.ToUpper())

                    let value = serializer.Deserialize(content.CreateReader(), uci.GetFields().[0].PropertyType)
                    FSharpValue.MakeUnion(uci, [| value |], bindingFlags)
                else if content.Count = 3 && content.ContainsKey "tag" && content.ContainsKey "name" && content.ContainsKey "fields" then
                    let property = content.Property("name")
                    let caseName = property.Value.ToObject<string>()
                    let uci = getUci t caseName
                    let itemTypes = uci.GetFields() |> Array.map (fun pi -> pi.PropertyType)
                    if itemTypes.Length > 1
                    then
                        let values = readElements(content.["fields"].CreateReader(), itemTypes, serializer)
                        FSharpValue.MakeUnion(uci, List.toArray values, bindingFlags)
                    else
                        let value = serializer.Deserialize(content.["fields"].[0].CreateReader(), itemTypes.[0])
                        FSharpValue.MakeUnion(uci, [|value|], bindingFlags)
                else
                    failwith "Unsupported"
            | JsonToken.Null -> null // for { "union": null }
            | JsonToken.StartArray ->
                let unionArray = serializer.Deserialize<JToken>(reader) :?> JArray
                let name = unionArray.[0].Value<string>()
                let unionCaseInfo = getUci t name
                let unionCaseTypes = unionCaseInfo.GetFields() |> Array.map (fun pi -> pi.PropertyType)
                let values = Seq.skip 1 (unionArray.AsJEnumerable())
                let parsedValue =
                    [| 0 .. (unionCaseTypes.Length - 1) |]
                    |> Array.map (fun index ->
                        let value = Seq.item index values
                        value.ToObject(unionCaseTypes.[index], serializer))
                    |> fun unionCaseValues -> FSharpValue.MakeUnion(unionCaseInfo, unionCaseValues, bindingFlags)
                parsedValue
            | _ -> failwithf "Invalid JSON token: %s" (reader.TokenType.ToString())
        | true, Kind.MapOrDictWithNonStringKey ->
            let mapTypes = t.GetGenericArguments()
            let mapSerializer = typedefof<MapSerializer<_,_>>.MakeGenericType mapTypes
            let mapDeserializeMethod = mapSerializer.GetMethod("Deserialize")
            mapDeserializeMethod.Invoke(null, [| t; reader; serializer |])
        | true, Kind.MapWithStringKey ->
            if reader.TokenType = JsonToken.StartObject
            then
              // map is encoded as { key: value }
              let mapTypes = t.GetGenericArguments()
              let valueT = mapTypes.[1]
              let mapSerializer = typedefof<MapStringKeySerializer<_>>.MakeGenericType valueT
              let mapDeserializeMethod = mapSerializer.GetMethod("Deserialize")
              mapDeserializeMethod.Invoke(null, [| t; reader; serializer |])
            else
              // map is encoded as [ [key, value] ] => rewrite as { key: value }
              let tuplesArray = serializer.Deserialize<JToken>(reader) :?> JArray
              let mapLiteral = JObject()
              for tuple in tuplesArray do
                let innerTuple = tuple :?> JArray
                mapLiteral.Add(JProperty(tuple.[0].Value<string>(), tuple.[1]))
              let mapTypes = t.GetGenericArguments()
              let valueT = mapTypes.[1]
              let mapSerializer = typedefof<MapStringKeySerializer<_>>.MakeGenericType valueT
              let mapDeserializeMethod = mapSerializer.GetMethod("Deserialize")
              mapDeserializeMethod.Invoke(null, [| t; mapLiteral.CreateReader(); serializer |])
        | true, Kind.DataTable
        | true, Kind.DataSet ->
            DataSetSerializer.Deserialize(t, reader, serializer)
        | true, _ ->
            serializer.Deserialize(reader, t)