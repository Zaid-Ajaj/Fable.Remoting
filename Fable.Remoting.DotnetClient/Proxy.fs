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

    let (|NoArgs|_|) = function 
        | Lambda(_, PropertyGet (Some (server), method, [])) -> 
            Some(method.Name)
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
            | NoArgs(methodName) -> 
                let route = builder typeName methodName 
                proxyPost<'u> [  ] route 
            | OneArg(methodName, arg) -> 
                let route = builder typeName methodName 
                proxyPost<'u> [ arg ] route
            | TwoArgs(methodName, fstArg, sndArg) ->
                let route = builder typeName methodName 
                proxyPost<'u> [ fstArg; sndArg ] route
            | otherwise -> failwithf "Quatation expression %A cannot be processed" expr;
   
    let create<'t> builder = Proxy<'t>(builder)