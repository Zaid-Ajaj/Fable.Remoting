namespace Fable.Remoting.Server

open System 
open System.Reflection
open FSharp.Reflection
open Fable.Remoting.Json
open Newtonsoft.Json
open Newtonsoft.Json.Linq

/// Provides utilities to run functions dynamically from record fields
module DynamicRecord = 
   
    /// Invokes a function of a record field given the name of the method, the record itself, and an array of arguments for that function
    let invoke (func: RecordFunctionInfo) args =
        let args = if Array.isEmpty args then [| null |] else args
        let propValue = func.PropertyInfo.GetValue(func.Implementation, null) 
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
                let asyncValue = invoke func methodArgs
                return! boxer.BoxAsyncResult asyncValue
        } 


    /// Verifies whether the input type is valid as a protocol definition of an API. 
    let checkProtocolDefinition (protocolType: Type) = 
        if not (FSharpType.IsRecord protocolType) 
        then failwithf "Protocol definition must be encoded as a record type. The input type '%s' was not a record." protocolType.Name
        else () (* TODO: verify the record fields and supported serializable types *)

    /// Reads the metadata from protocol definition, assumes the shape is checked and is correct
    let createRecordFuncInfo (implementation: 't) =
        let protocolType = implementation.GetType()
        FSharpType.GetRecordFields(protocolType)
        |> List.ofArray
        |> List.map (fun propertyInfo ->
            propertyInfo.Name, 
            {
                FunctionName = propertyInfo.Name
                PropertyInfo = propertyInfo 
                Type = makeRecordFuncType propertyInfo.PropertyType 
                Implementation = implementation
            }) 
        |> Map.ofList    

    let private fableConverter = new FableJsonConverter() :> JsonConverter

    /// Serializes the input value into JSON using Fable converter
    let serialize result = JsonConvert.SerializeObject(result, [| fableConverter |])

    /// Based of function metadata, convert the input JSON into an appropriate array of typed arguments. 
    let createArgsFromJson (func: RecordFunctionInfo) (inputJson: string) (logger: Option<string -> unit>) = 
        match func.Type with 
        | NoArguments _ -> [|  |] 
        | SingleArgument (input, _) when input = typeof<unit> -> [| box () |]
        | SingleArgument (input, _) ->
            let parsedJson = JToken.Parse(inputJson) 
            let serializer = JsonSerializer()
            serializer.Converters.Add fableConverter
            if parsedJson.Type = JTokenType.Array && not (input.IsArray || FSharpType.IsTuple input || input.FullName.StartsWith("Microsoft.FSharp.Collections.FSharpList`1")) then
                let jsonValues = List.ofArray (parsedJson.ToObject<JToken[]>()) 
                match jsonValues with 
                | [ ] -> 
                    // JSON input array is empty -> fine only if input is unit
                    if input = typeof<unit> 
                    then [| box () |]
                    else failwithf "Input JSON array of the arguments for function '%s' was empty while the function expected a value of type '%s'" func.FunctionName (input.FullName)
                | [ singleJsonObject ] -> 
                    Diagnostics.deserializationPhase logger (fun () -> singleJsonObject.ToString()) [| input |]
                    // JSON input array is a single object and function is of single argument then it works fine
                    [| singleJsonObject.ToObject(input, serializer) |]  
                | singleJsonObject :: moreValues -> 
                    Diagnostics.deserializationPhase logger (fun () -> singleJsonObject.ToString()) [| input |]
                    // JSON input array has many values, just take the first one and ignore the rest
                    [| singleJsonObject.ToObject(input, serializer) |]  
            elif parsedJson.Type = JTokenType.Array && (input.IsArray || FSharpType.IsTuple input || input.FullName.StartsWith("Microsoft.FSharp.Collections.FSharpList`1")) then
                // expected type is list-like and the Json is list like
                let jsonValues = parsedJson.ToObject<JToken[]>()
                Array.zip [| input |] jsonValues 
                |> Array.map (fun (jsonType, json) -> json.ToObject(jsonType, serializer))
            else
                Diagnostics.deserializationPhase logger (fun () -> inputJson.ToString()) [| input |]
                 // then the input json is a single object (not an array) and can be deserialized directly
                [| JsonConvert.DeserializeObject(inputJson, input, [| fableConverter |]) |]
        | ManyArguments (inputArgTypes, _) -> 
            let parsedJson = JToken.Parse(inputJson) 
            let serializer = JsonSerializer()
            serializer.Converters.Add fableConverter
            if parsedJson.Type <> JTokenType.Array
            then failwithf "The record function '%s' expected %d arguments to be recieved in the form of a JSON array but the input JSON was not an array" func.FunctionName (List.length inputArgTypes)
            else 
                let jsonValues = List.ofArray (parsedJson.ToObject<JToken[]>()) 
                if (List.length jsonValues <> List.length inputArgTypes) 
                then failwithf "The record function '%s' expected %d arguments but got %d arguments in the JSON array" func.FunctionName (List.length inputArgTypes) (List.length jsonValues) 
                else 
                    Diagnostics.deserializationPhase logger (fun () -> inputJson.ToString()) (Array.ofList inputArgTypes)
                    List.zip inputArgTypes jsonValues 
                    |> List.map (fun (jsonType, json) -> json.ToObject(jsonType, serializer))
                    |> Array.ofList 