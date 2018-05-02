namespace Fable.Remoting.DotnetClient

open FSharp.Reflection
open System.Reflection
open Fable.Remoting.Json
open System
open System.Text
open HttpFs.Client
open Newtonsoft.Json
open Newtonsoft.Json.Linq
open Hopac

[<RequireQualifiedAccess>]
module Proxy =

    [<RequireQualifiedAccess>]
    type ProxyField = 
        | Function of name:string * funcType:Type * returnType: Type 
        | AsyncValue of name:string * genericType:Type  
        | IgnoredInvalidField of name:string * Type  

    /// Flattens functions of `(type1 -> type2 -> ... -> typeN)` types to `[type1; type2; typeN]`. Intermediate functions types are expanded as well.
    let rec flattenFunction (functionType: System.Type) = 
        [ if FSharpType.IsFunction functionType then 
            let domain, range = FSharpType.GetFunctionElements functionType
            yield! flattenFunction domain 
            yield! flattenFunction range 
          else 
            yield functionType ] 

    /// Extracts proxy fields with their relevant data from a record type
    let proxyFieldsOf<'t>() =
        FSharpType.GetRecordFields typeof<'t>
        |> Array.map (fun propInfo ->
            let fieldName = propInfo.Name
            let fieldType = propInfo.PropertyType
            if FSharpType.IsFunction fieldType 
            then 
                let returnType = List.last (flattenFunction fieldType)
                if returnType.Name = "FSharpAsync`1"
                then ProxyField.Function(fieldName, fieldType, returnType.GetGenericArguments().[0])
                else ProxyField.IgnoredInvalidField(fieldName, fieldType)
            elif fieldType.Name = "FSharpAsync`1"
            then ProxyField.AsyncValue(fieldName, fieldType.GetGenericArguments().[0])
            else ProxyField.IgnoredInvalidField(fieldName, fieldType))
        |> List.ofSeq

    let private converter = FableJsonConverter()
    let private serializer = JsonSerializer()
    serializer.Converters.Add converter
    
    /// Parses a JSON iput string to a .NET type using Fable JSON converter
    let parseDynamicallyAs (valueType: Type) (json: string) = 
        JToken.Parse(json).ToObject(valueType, serializer)
    
    /// Sends a POST request to the calulated url with the arguments of serialized to an input list
    let proxyPost (functionArguments: obj list) url (returnType: Type) = 
        let serializedInputArgs = JsonConvert.SerializeObject(functionArguments, converter)
        Request.createUrl Post url 
        |> Request.bodyStringEncoded serializedInputArgs (Encoding.UTF8)
        |> getResponse
        |> Job.bind Response.readBodyAsString 
        |> Job.map (parseDynamicallyAs returnType)
        |> Job.toAsync

    let createField (serverType: Type) endpoint routeBuilder = function 
        | ProxyField.Function(funcName, funcType, returnType) -> 
            let route = routeBuilder serverType.Name funcName
            let argCount = List.length (flattenFunction funcType) - 1
            let url = sprintf "%s%s" endpoint route
            printfn "Mapping record field '%s' to route %s" funcName url
            match argCount with  
            | 1 -> FSharpValue.MakeFunction(funcType, fun a -> box (proxyPost [a] url returnType))
            | 2 -> box (fun a b -> proxyPost [a; b] url returnType) 
            | 3 -> box (fun a b c -> proxyPost [a; b; c] url returnType)
            | 4 -> box (fun a b c d e -> proxyPost [a; b; c; d; e] url returnType) 
            | 5 -> box (fun a b c d e f -> proxyPost [a; b; c; d; e; f] url returnType) 
            | 6 -> box (fun a b c d e f g -> proxyPost [a; b; c; d; e; f; g] url returnType)
            | n -> failwith "Only up to 6 paramters are supported" 
            |> Some 
        | ProxyField.AsyncValue(name, returnType) -> 
            let customRoute = routeBuilder serverType.Name name
            let url = sprintf "%s%s" endpoint customRoute
            box (proxyPost [] url returnType) 
            |> Some
        | ProxyField.IgnoredInvalidField(name, _) ->
            printfn "Record field '%s' is not a valid proxy field and will be ignored" name
            None 

    let createAn<'t> (endpoint: string) routeBuilder = 
        let serverType = typeof<'t>
        let fieldCreator = createField serverType endpoint routeBuilder
        let fields = 
            proxyFieldsOf<'t>()
            |> List.choose fieldCreator
            |> List.map unbox<obj> 
            |> Array.ofList 

        FSharpValue.MakeRecord(serverType, fields, false) 
        |> unbox<'t>