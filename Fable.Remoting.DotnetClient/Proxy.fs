namespace Fable.Remoting.DotnetClient

open Fable.Remoting.Json
open System.Text
open HttpFs.Client
open Newtonsoft.Json
open Hopac

[<RequireQualifiedAccess>]
module Proxy =
    
    open Patterns

    let private converter = FableJsonConverter()

    /// Parses a JSON iput string to a .NET type using Fable JSON converter
    let parseAs<'t> (json: string) = 
        JsonConvert.DeserializeObject<'t>(json, converter)
    
    /// Sends a POST request to the specified url with the arguments of serialized to an input list
    let proxyPost<'t> (functionArguments: obj list) url = 
        let serializedInputArgs = JsonConvert.SerializeObject(functionArguments, converter)
        Request.createUrl Post url 
        |> Request.bodyStringEncoded serializedInputArgs (Encoding.UTF8)
        |> getResponse
        |> Job.bind Response.readBodyAsString 
        |> Job.map parseAs<'t>
        |> Job.toAsync 
        
    type Proxy<'t>(builder) = 
        let typeName = typeof<'t>.Name
        member this.CallAs<'u> (expr: Quotations.Expr<'t -> Async<'u>>) : Async<'u> = 
            match expr with 
            | NoArgs (methodName, args) 
            | OneArg (methodName, args)
            | TwoArgs (methodName, args)
            | ThreeArgs (methodName, args) 
            | FourArgs (methodName, args)
            | FiveArgs (methodName, args) -> 
                let route = builder typeName methodName
                proxyPost<'u> args route
            | otherwise -> failwithf "Quatation expression %A cannot be processed" expr;
   
    let create<'t> builder = Proxy<'t>(builder)