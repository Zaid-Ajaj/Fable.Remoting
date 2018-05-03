namespace Fable.Remoting.DotnetClient

open Fable.Remoting.Json
open Newtonsoft.Json

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
        async {
            let! responseText = Http.makePostRequest url serializedInputArgs
            return parseAs<'t> responseText
        }
        
    type Proxy<'t>(builder) = 
        let typeName = typeof<'t>.Name
        member this.call<'u> (expr: Quotations.Expr<'t -> Async<'u>>) : Async<'u> = 
            match expr with 
            | NoArgs (methodName, args) 
            | OneArg (methodName, args)
            | TwoArgs (methodName, args)
            | ThreeArgs (methodName, args) 
            | FourArgs (methodName, args)
            | FiveArgs (methodName, args) 
            | SixArgs (methodName, args)
            | SevenArgs (methodName, args)
            | EightArgs (methodName, args) -> 
                let route = builder typeName methodName
                proxyPost<'u> args route
            | otherwise -> failwithf "Quatation expression %A cannot be processed" expr;
   
    let create<'t> builder = Proxy<'t>(builder)