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
open Quotations.DerivedPatterns
open Quotations.Patterns 

[<RequireQualifiedAccess>]
module Proxy =
    
    let private converter = FableJsonConverter()
    let private serializer = JsonSerializer()
    serializer.Converters.Add converter

    /// Parses a JSON iput string to a .NET type using Fable JSON converter
    let parseDynamicallyAs<'t> (json: string) = 
        JsonConvert.DeserializeObject<'t>(json, converter)
    
    /// Sends a POST request to the specified url with the arguments of serialized to an input list
    let proxyPost<'t> (functionArguments: obj list) url = 
        let serializedInputArgs = JsonConvert.SerializeObject(functionArguments, converter)
        Request.createUrl Post url 
        |> Request.bodyStringEncoded serializedInputArgs (Encoding.UTF8)
        |> getResponse
        |> Job.bind Response.readBodyAsString 
        |> Job.map parseDynamicallyAs<'t>
        |> Job.toAsync 

    let (|PureAsync|_|) = function 
        | Lambda(_, PropertyGet (Some (server), method, [])) -> Some(method.Name)
        | otherwise -> None
    let (|OneArg|_|) = function 
        | Lambda(_, Application (PropertyGet (Some (server), method, []), Value((value, valueType)))) ->
            Some (method.Name, value)
        | otherwise -> None 
    let (| TwoArgs |_|) = function 
        | Lambda(_, Application (OneArg(methodName, fstArg) , Value((sndArg, valueType)))) ->
            Some (methodName, fstArg, sndArg) 
        | otherwise -> None 

    type Proxy<'t>(builder) = 
        let typeName = typeof<'t>.Name
        member this.CallAs<'u> (expr: Quotations.Expr<'t -> Async<'u>>) : Async<'u> = 
            match expr with 
            | PureAsync(methodName) -> 
                let route = builder typeName methodName 
                proxyPost<'u> [  ] route 
            | OneArg(methodName, arg) -> 
                let route = builder typeName methodName 
                proxyPost<'u> [ arg ] route
            | TwoArgs(methodName, fstArg, sndArg) ->
                let route = builder typeName methodName 
                proxyPost<'u> [ fstArg; sndArg ] route
            | otherwise -> failwithf "Quatation expression %A cannot be processed" expr;

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
    //let proxyFieldsOf<'t>() =
    //    FSharpType.GetRecordFields typeof<'t>
    //    |> Array.map (fun propInfo ->
    //        let fieldName = propInfo.Name
    //        let fieldType = propInfo.PropertyType
    //        if FSharpType.IsFunction fieldType 
    //        then 
    //            let returnType = List.last (flattenFunction fieldType)
    //            if returnType.Name = "FSharpAsync`1"
    //            then ProxyField.Function(fieldName, fieldType, returnType.GetGenericArguments().[0])
    //            else ProxyField.IgnoredInvalidField(fieldName, fieldType)
    //        elif fieldType.Name = "FSharpAsync`1"
    //        then ProxyField.AsyncValue(fieldName, fieldType.GetGenericArguments().[0])
    //        else ProxyField.IgnoredInvalidField(fieldName, fieldType))
    //    |> List.ofSeq


    
    /// Parses a JSON iput string to a .NET type using Fable JSON converter
    //let parseDynamicallyAs (valueType: Type) (json: string) = 
    //    JToken.Parse(json).ToObject(valueType, serializer)
    
    /// Sends a POST request to the specified url with the arguments of serialized to an input list
    //let proxyPost (functionArguments: obj list) url (returnType: Type) = 
    //    let serializedInputArgs = JsonConvert.SerializeObject(functionArguments, converter)
    //    Request.createUrl Post url 
    //    |> Request.bodyStringEncoded serializedInputArgs (Encoding.UTF8)
    //    |> getResponse
    //    |> Job.bind Response.readBodyAsString 
    //    |> Job.map (parseDynamicallyAs returnType)
    //    |> Job.toAsync 
    //    |> box

    //let createField (serverType: Type) routeBuilder = function 
    //    | ProxyField.Function(funcName, funcType, returnType) -> 
    //        let route = routeBuilder serverType.Name funcName
    //        let argCount = List.length (flattenFunction funcType) - 1
    //        printfn "Mapping record field '%s' to route %s" funcName route
    //        match argCount with  
    //        | 1 -> FSharpValue.MakeFunction(funcType, fun a -> proxyPost [a] route returnType)
    //        | 2 -> box (fun a b -> proxyPost [a; b] route returnType) 
    //        | 3 -> box (fun a b c -> proxyPost [a; b; c] route returnType)
    //        | 4 -> box (fun a b c d e -> proxyPost [a; b; c; d; e] route returnType) 
    //        | 5 -> box (fun a b c d e f -> proxyPost [a; b; c; d; e; f] route returnType) 
    //        | 6 -> box (fun a b c d e f g -> proxyPost [a; b; c; d; e; f; g] route returnType)
    //        | n -> failwith "Only up to 6 paramters are supported" 
    //        |> Some 
    //    | ProxyField.AsyncValue(name, returnType) -> 
    //        let route = routeBuilder serverType.Name name
    //        box (proxyPost [] route returnType) 
    //        |> Some
    //    | ProxyField.IgnoredInvalidField(name, _) ->
    //        printfn "Record field '%s' is not a valid proxy field and will be ignored" name
    //        None 

    //let createAn<'t> routeBuilder = 
    //    let serverType = typeof<'t>
    //    let fieldCreator = createField serverType routeBuilder
    //    let fields = 
    //        proxyFieldsOf<'t>()
    //        |> List.choose fieldCreator
    //        |> Array.ofList 
 
    //    FSharpValue.MakeRecord(serverType, fields, false) 
    //    |> unbox<'t>

    let create<'t> builder = Proxy<'t>(builder)