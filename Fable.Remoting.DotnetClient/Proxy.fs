namespace Fable.Remoting.DotnetClient

open Fable.Remoting.Json
open Newtonsoft.Json
open System.Net.Http
open System.Threading.Tasks
open System.Linq.Expressions
open System

[<RequireQualifiedAccess>]
module Proxy =

    open Patterns
    let private converter = FableJsonConverter()

    /// Parses a JSON iput string to a .NET type using Fable JSON converter
    let parseAs<'t> (json: string) =
        JsonConvert.DeserializeObject<'t>(json, converter)

    /// Sends a POST request to the specified url with the arguments of serialized to an input list
    let proxyPost<'t> (functionArguments: obj list) url client auth =
        let serializedInputArgs = JsonConvert.SerializeObject(functionArguments, converter)
        async {
            let! responseText = Http.makePostRequest client url serializedInputArgs auth
            return parseAs<'t> responseText
        }

    /// Sends a POST request to the specified url safely with the arguments of serialized to an input list, if an exception is thrown, is it catched
    let safeProxyPost<'t> (functionArguments: obj list) url client auth =
        let serializedInputArgs = JsonConvert.SerializeObject(functionArguments, converter)
        async {
            let! catchedResponse = Async.Catch (Http.makePostRequest client url serializedInputArgs auth)
            match catchedResponse with
            | Choice1Of2 responseText -> return Ok (parseAs<'t> responseText)
            | Choice2Of2 thrownException -> return Error thrownException
        }

    type Proxy<'t>(builder, client: Option<HttpClient>) =
        let typeName =
            let name = typeof<'t>.Name
            match typeof<'t>.GenericTypeArguments with
            | [|  |] -> name
            | manyArgs -> name.[0 .. name.Length - 3]
        let mutable authHeader = Http.Authorisation.NoToken
        let client = defaultArg client (new HttpClient())
        /// Uses the specified string as the authorization header for the requests that the proxy makes to the server
        member __.authorisationHeader (header: string) =
            authHeader <- Http.Authorisation.Token header
             
        member __.Call<'a> (expr: Expression<Func<'t, Async<'a>>>) : Task<'a> =
            let args = [  ]
            let memberExpr = unbox<MemberExpression> expr.Body  
            let functionName = memberExpr.Member.Name
            let route = builder typeName functionName
            let asyncPost = proxyPost<'a> args route client authHeader
            Async.StartAsTask asyncPost 

        member __.Call<'a, 'b> (expr: Expression<Func<'t, FSharpFunc<'a, Async<'b>>>>, input: 'a) : Task<'b> =
            let args = [ box input ]
            let memberExpr = unbox<MemberExpression> expr.Body  
            let functionName = memberExpr.Member.Name
            let route = builder typeName functionName
            let asyncPost = proxyPost<'b> args route client authHeader
            Async.StartAsTask asyncPost 

        member __.Call<'a, 'b, 'c> (expr: Expression<Func<'t, FSharpFunc<'a, FSharpFunc<'b, Async<'c>>>>>, arg1: 'a, arg2: 'b) : Task<'c> = 
            let args = [ box arg1; box arg2 ]
            let memberExpr = unbox<MemberExpression> expr.Body  
            let functionName = memberExpr.Member.Name
            let route = builder typeName functionName
            let asyncPost = proxyPost<'c> args route client authHeader
            Async.StartAsTask asyncPost 

        member __.Call<'a, 'b, 'c, 'd> (expr: Expression<Func<'t, FSharpFunc<'a, FSharpFunc<'b, FSharpFunc<'c, Async<'d>>>>>>, arg1: 'a, arg2: 'b, arg3: 'c) : Task<'d> = 
            let args = [ box arg1; box arg2; box arg3 ]
            let memberExpr = unbox<MemberExpression> expr.Body  
            let functionName = memberExpr.Member.Name
            let route = builder typeName functionName
            let asyncPost = proxyPost<'d> args route client authHeader
            Async.StartAsTask asyncPost 

        /// Call the proxy function by wrapping it inside a quotation expr:
        /// ```
        /// async {
        ///     let proxy = Proxy.create<IServer> (sprintf "http://api.endpoint.org/api/%s/%s")
        ///     let! result = proxy.call <@ server -> server.getLength "input" @>
        ///  }
        /// ```
        member __.call<'u> ([<ReflectedDefinition>] expr: Quotations.Expr<'t -> Async<'u>>) =
            match expr with
            | ProxyLambda(methodName, args) ->
                let route = builder typeName methodName
                proxyPost<'u> args route client authHeader
            | otherwise -> failwithf "Failed to process the following quotation expression\n%A\nThis could be due to the fact that you are providing complex function paramters to your called proxy function like nested records with generic paramters or lists, if that is the case, try binding the paramter to a value outside the qoutation expression and pass that value to the function instead" expr

        /// Call the proxy function safely by wrapping it inside a quotation expr and catching any thrown exception by the web request
        /// ```
        ///    async {
        ///       let proxy = Proxy.create<IServer> (sprintf "http://api.endpoint.org/api/%s/%s")
        ///       let! result = proxy.callSafely <@ server -> server.getLength "input" @>
        ///       match result with
        ///       | Ok result -> (* do stuff with result *)
        ///       | Error ex -> (* panic! *)
        ///    }
        /// ```
        member __.callSafely<'u> ([<ReflectedDefinition>] expr: Quotations.Expr<'t -> Async<'u>>) : Async<Result<'u, exn>> =
            match expr with
            | ProxyLambda(methodName, args) ->
                let route = builder typeName methodName
                safeProxyPost<'u> args route client authHeader
            | otherwise -> failwithf "Failed to process quotation expression\n%A\nThis could be due to the fact that you are providing complex function paramters to your called proxy function like nested records with generic paramters or lists, if that is the case, try binding the paramter to a value outside the qoutation expression and pass that value to the function instead" expr

    /// Creates a proxy for a type with a route builder
    let create<'t> builder = Proxy<'t>(builder, None)
    /// Creates a proxy with for a type using a route builder and a custom HttpClient that you provide
    let custom<'t> builder client = Proxy<'t>(builder, Some client)

    let CreateFromBuilder<'t>(f: Func<string, string, string>) =
        let builder = fun typeName funcName -> f.Invoke(typeName, funcName)
        Proxy<'t>(builder, None)
    let CreateFromBuilderAndClient<'t>(f: Func<string, string, string>, client: HttpClient) = 
        let builder = fun typeName funcName -> f.Invoke(typeName, funcName)
        Proxy<'t>(builder, Some client)