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
    let proxyPost<'t> (functionArguments: obj list) url auth = 
        let serializedInputArgs = JsonConvert.SerializeObject(functionArguments, converter)
        async {
            let! responseText = Http.makePostRequest url serializedInputArgs auth
            return parseAs<'t> responseText
        }
        
    type Proxy<'t>(builder) = 
        let typeName = typeof<'t>.Name
        let mutable authHeader = Http.Authorisation.NoToken
        
        /// Adds the specified string as the authorization header for the requests that the proxy makes to the server
        member this.authorisationHeader (header: string) = 
            authHeader <- Http.Authorisation.Token header

        /// Call the proxy function by wrapping it inside a quotation expr:
        /// `async { 
        ///     let proxy = Proxy.create<IServer> (sprintf "http://api.endpoint.org/api/%s/%s")  
        ///     let! result = proxy.call <@ server -> server.getLength "input" @>
        ///  }  `    
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
                proxyPost<'u> args route authHeader
            | otherwise -> failwithf "Quatation expression %A cannot be processed" expr
    let create<'t> builder = Proxy<'t>(builder)