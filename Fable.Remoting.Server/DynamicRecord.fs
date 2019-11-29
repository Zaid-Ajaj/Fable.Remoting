namespace Fable.Remoting.Server

open System
open System.Reflection
open FSharp.Reflection
open Fable.Remoting.Json
open Newtonsoft.Json
open Newtonsoft.Json.Linq
open System.Collections.Concurrent

/// Provides utilities to run functions dynamically from record fields
module DynamicRecord =

    /// Invokes a function of a record field given the name of the method, the record itself, and an array of arguments for that function
    let invoke (func: RecordFunctionInfo) implementation args =
        let args = if Array.isEmpty args then [| null |] else args
        let propValue = func.PropertyInfo.GetValue(implementation, null)
        let invokeMethod =  propValue.GetType().GetMethods() |> Array.tryFind (fun m -> m.Name = "Invoke")
        match invokeMethod with
        | Some methodInfo -> methodInfo.Invoke(propValue, args)
        | None -> failwithf "Record field '%s' is not a function and cannot be invoked" func.FunctionName

    /// Reads the type of a F# function (a -> b) and turns that into a list of types [a; b]
    let rec flattenLambdaType (propType: Type) =
        [ if FSharpType.IsFunction propType then
            let (domain, range) = FSharpType.GetFunctionElements propType
            yield! flattenLambdaType domain
            yield! flattenLambdaType range
          else
            yield propType ]

    /// Turns a function type ('a -> 'b) into a RecordFuncType that is easier to work with when parsing parameters from JSON and when doing the matching of routes to record functions
    let makeRecordFuncType (propType: Type) =
        let flattenedTypes = flattenLambdaType propType
        match flattenedTypes with
        | [ simpleAsyncValue ] -> RecordFunctionType.NoArguments simpleAsyncValue
        | [ input; output ] -> RecordFunctionType.SingleArgument (input, output)
        | manyArgumentsWithOutput ->
            let lastInputArgIndex = List.length manyArgumentsWithOutput - 1
            match List.splitAt lastInputArgIndex manyArgumentsWithOutput with
            | inputArguments, [ output ] -> RecordFunctionType.ManyArguments (inputArguments, output)
            | _ -> failwith "makeRecordFuncType: Should not happen"

    /// Creates an object that can unbox a generic Async value whose type information is encoded from the provided type
    let createAsyncBoxer (asyncT: Type) =
        typedefof<AsyncBoxer<_>>.MakeGenericType(asyncT)
            |> Activator.CreateInstance
            :?> IAsyncBoxer

    /// Extracts the 'T from Async<'T>
    let extractAsyncArg (asyncType: Type) =
        asyncType.GetGenericArguments().[0]

    /// Invokes an async function or value from a record, given the function metadata
    let invokeAsync (func: RecordFunctionInfo) implementation methodArgs =
       async {
            match func.Type with
            | NoArguments output ->
                let asyncTypeParam = extractAsyncArg output
                let boxer = createAsyncBoxer asyncTypeParam
                let asyncValue = func.PropertyInfo.GetValue(implementation, null)
                return! boxer.BoxAsyncResult asyncValue
            | SingleArgument (_, output)
            | ManyArguments (_, output) ->
                let asyncTypeParam = extractAsyncArg output
                let boxer = createAsyncBoxer asyncTypeParam
                let asyncValue = invoke func implementation methodArgs
                return! boxer.BoxAsyncResult asyncValue
        }

    let private recordFuncInfoCache = ConcurrentDictionary<Type, Map<String, RecordFunctionInfo>>()

    /// Reads the metadata from protocol definition, assumes the shape is checked and is correct
    let createRecordFuncInfo protocolType =
        match recordFuncInfoCache.TryGetValue protocolType with
        | true, x -> x
        | _ ->
            let x =
                FSharpType.GetRecordFields protocolType
                |> Array.map (fun propertyInfo ->
                    propertyInfo.Name,
                    {
                        FunctionName = propertyInfo.Name
                        PropertyInfo = propertyInfo
                        Type = makeRecordFuncType propertyInfo.PropertyType
                    })
                |> Map.ofArray
            recordFuncInfoCache.[protocolType] <- x
            x

    let isAsync (inputType: Type) =
        inputType.FullName.StartsWith("Microsoft.FSharp.Control.FSharpAsync`1")

    /// Verifies whether the input type is valid as a protocol definition of an API.
    let checkProtocolDefinition (implementation: 't) =
        let protocolType = implementation.GetType()
        if not (FSharpType.IsRecord protocolType)
        then failwithf "Protocol definition must be encoded as a record type. The input type '%s' was not a record." protocolType.Name
        else
          let functionInfo = createRecordFuncInfo protocolType
          let functions = Map.toList functionInfo
          for (funcName, info) in functions do
            match info.Type with
            | NoArguments outputType when not (isAsync outputType) ->
                failwithf "The type '%s' of the record field '%s' for record type '%s' is not valid. It must either be Async<'t> or a function that returns Async<'t> (i.e. 'u -> Async<'t>)" outputType.Name funcName protocolType.Name
            | SingleArgument (inputType, outputType) when not (isAsync outputType) ->
                failwithf "The output type '%s' of the record field '%s' for record type '%s' is not valid. The function must return Async<'t> (i.e. 'u -> Async<'t>)" outputType.Name funcName protocolType.Name
            | ManyArguments (inputTypes, outputType) when not (isAsync outputType) ->
                failwithf "The output type '%s' of the record field '%s' for record type '%s' is not valid. The function must return Async<'t> (i.e. 'u -> Async<'t>)" outputType.Name funcName protocolType.Name
            | _ -> ()

    let private fableConverter = new FableJsonConverter() :> JsonConverter

    let private fableSeriazizer =
        let serializer = JsonSerializer()
        serializer.Converters.Add fableConverter
        serializer

    /// Serializes the input value into JSON using Fable converter
    let serialize result = JsonConvert.SerializeObject(result, [| fableConverter |])

    let typeNames inputTypes =
        inputTypes
        |> List.map Diagnostics.typePrinter
        |> String.concat ", "
        |> sprintf "[%s]"

    let internal settings = JsonSerializerSettings(DateParseHandling = DateParseHandling.None)
    /// Based of function metadata, convert the input JSON into an appropriate array of typed arguments.
    let createArgsFromJson (func: RecordFunctionInfo) (inputJson: string) (logger: Option<string -> unit>) =
        match func.Type with
        | NoArguments _ -> [|  |]
        | SingleArgument (input, _) when input = typeof<unit> -> [| box () |]
        | SingleArgument (input, _) ->
            let parsedJson = JsonConvert.DeserializeObject<JToken>(inputJson, settings)
            if parsedJson.Type = JTokenType.Array && not (input.IsArray || FSharpType.IsTuple input || input.FullName.StartsWith("Microsoft.FSharp.Collections.FSharpList`1") || input.FullName.StartsWith("Microsoft.FSharp.Collections.FSharpMap`2")) then
                let jsonValues = List.ofSeq (unbox<JArray> parsedJson)
                match jsonValues with
                | [ ] ->
                    // JSON input array is empty -> fine only if input is unit
                    if input = typeof<unit>
                    then [| box () |]
                    else failwithf "Input JSON array of the arguments for function '%s' was empty while the function expected a value of type '%s'" func.FunctionName (Diagnostics.typePrinter input)
                | [ singleJsonObject ] ->
                    Diagnostics.deserializationPhase logger (fun () -> singleJsonObject.ToString()) [| input |]
                    // JSON input array is a single object and function is of single argument then it works fine
                    [| singleJsonObject.ToObject(input, fableSeriazizer) |]
                | singleJsonObject :: moreValues ->
                    Diagnostics.deserializationPhase logger (fun () -> singleJsonObject.ToString()) [| input |]
                    // JSON input array has many values, just take the first one and ignore the rest
                    [| singleJsonObject.ToObject(input, fableSeriazizer) |]
            elif parsedJson.Type = JTokenType.Array && (input.IsArray || FSharpType.IsTuple input || input.FullName.StartsWith("Microsoft.FSharp.Collections.FSharpList`1") || input.FullName.StartsWith("Microsoft.FSharp.Collections.FSharpMap`2")) then
                // expected type is list-like and the Json is list like
                let jsonValues = Array.ofSeq (unbox<JArray> parsedJson)
                Array.zip [| input |] jsonValues
                |> Array.map (fun (jsonType, json) ->
                    Diagnostics.deserializationPhase logger (fun () -> json.ToString()) [| jsonType |]
                    json.ToObject(jsonType, fableSeriazizer))
            else
                Diagnostics.deserializationPhase logger (fun () -> inputJson.ToString()) [| input |]
                 // then the input json is a single object (not an array) and can be deserialized directly
                [| JsonConvert.DeserializeObject(inputJson, input, [| fableConverter |]) |]
        | ManyArguments (inputArgTypes, _) ->
            let parsedJson = JsonConvert.DeserializeObject<JToken>(inputJson, settings)
            if parsedJson.Type <> JTokenType.Array
            then
                let typeInfo = typeNames inputArgTypes
                failwithf "The record function '%s' expected %d argument(s) of the types %s to be recieved in the form of a JSON array but the input JSON was not an array" func.FunctionName (List.length inputArgTypes) typeInfo
            else
                let jsonValues = List.ofSeq (unbox<JArray> parsedJson)
                if (List.length jsonValues <> List.length inputArgTypes)
                then
                    let typeInfo = typeNames inputArgTypes
                    failwithf "The record function '%s' expected %d argument(s) of the types %s but got %d argument(s) in the input JSON array" func.FunctionName (List.length inputArgTypes) typeInfo (List.length jsonValues)
                else
                    Diagnostics.deserializationPhase logger (fun () -> inputJson.ToString()) (Array.ofList inputArgTypes)
                    List.zip inputArgTypes jsonValues
                    |> List.map (fun (jsonType, json) -> json.ToObject(jsonType, fableSeriazizer))
                    |> Array.ofList

    /// Based of function metadata, tries to convert the input JSON into an appropriate array of typed arguments.
    let tryCreateArgsFromJson (func: RecordFunctionInfo) (inputJson: string) (logger: Option<string -> unit>) =
        try Ok (createArgsFromJson func inputJson logger)
        with | ex ->
            Error { ParsingArgumentsError = ex.Message }

    let routeMethod = function
        | NoArguments outputType when isAsync outputType -> "GET"
        | SingleArgument (input, output) when input = typeof<unit> -> "GET"
        | otherwise -> "POST"

    let makeDocsSchema record (Documentation(docsName, routesDefs)) (routeBuilder: string -> string -> string) =
        match record with
        | TypeInfo.RecordType fields  ->
            let typeName = record.Name
            let schema = JObject()
            let routes = JArray()
            for fieldName, fieldType in fields do
                let routeDocs = List.tryFind (fun routeDocs -> routeDocs.Route = Some fieldName) routesDefs
                let route = JObject()
                route.Add(JProperty("remoteFunction", fieldName))
                route.Add(JProperty("httpMethod", routeMethod (makeRecordFuncType fieldType)))
                route.Add(JProperty("route", routeBuilder typeName fieldName))

                let description =
                    routeDocs
                    |> Option.bind (fun route -> route.Description)
                    |> Option.defaultValue ""

                let alias =
                    routeDocs
                    |> Option.bind (fun route -> route.Alias)
                    |> Option.defaultValue fieldName

                route.Add(JProperty("description", description))
                route.Add(JProperty("alias", alias))

                let examplesJson = JArray()
                match routeDocs with
                | None -> ()
                | Some routeDocs ->
                    for (exampleArgs, description) in routeDocs.Examples do
                        let argsJson = JArray()
                        for arg in exampleArgs do argsJson.Add(JToken.Parse(serialize arg))
                        let exampleJson = JObject()
                        exampleJson.Add(JProperty("description", description))
                        exampleJson.Add(JProperty("arguments", argsJson))
                        examplesJson.Add(exampleJson)

                route.Add(JProperty("examples", examplesJson))
                routes.Add(route)

            schema.Add(JProperty("name", docsName))
            schema.Add(JProperty("routes", routes))
            schema
        | _ ->
            JObject()
