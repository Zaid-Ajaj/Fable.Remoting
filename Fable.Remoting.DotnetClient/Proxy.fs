namespace Fable.Remoting.DotnetClient

// Internal Newtonsoft branch implementation — `FableJsonConverter` is
// deprecated for external consumers; here it's the supported legacy path
// triggered via the default (when no JsonSerializerOptions are passed) and
// will be removed in the next major version.
#nowarn "44"

open Fable.Remoting.Json
open Newtonsoft.Json
open System.Net.Http
open System.Threading.Tasks
open System.Linq.Expressions
open System
open System.Text
open System.Net.Http.Headers
open System.Text.Json

[<RequireQualifiedAccess>]
module Proxy =

    open Patterns
    let private converter = FableJsonConverter()

    let private taskCatch (f: unit -> Task<'a>) =
        task {
            try
                let! res = f ()
                return Choice1Of2 res
            with e ->
                return Choice2Of2 e
        }

    /// Parses a JSON input string to a .NET type using the Fable JSON converter
    /// (Newtonsoft path — preserved for backward compatibility).
    let parseAs<'t> (json: string) =
        let options = JsonSerializerSettings()
        options.Converters.Add converter
        options.DateParseHandling <- DateParseHandling.None
        JsonConvert.DeserializeObject<'t>(json, options)

    /// Parses a JSON input string to a .NET type using a caller-provided
    /// System.Text.Json options instance. Pair with
    /// `Fable.Remoting.Json.SystemTextJson.FableConverters.create()` to
    /// get an options bundle pre-configured for byte-compat with the
    /// Newtonsoft path.
    let parseAsWith<'t> (stjOptions: JsonSerializerOptions) (json: string) =
        JsonSerializer.Deserialize<'t>(json, stjOptions)

    /// Parses a byte array to a .NET type using Message Pack
    let parseAsBinary<'t> (data: byte[]) =
        Fable.Remoting.MsgPack.Read.Reader(data).Read typeof<'t> :?> 't

    let internal createRequestBody (functionArguments: obj list) isMultipartEnabled (stjOptions: JsonSerializerOptions option): HttpContent =
        let serializeOne (value: obj) =
            match stjOptions with
            | None -> JsonConvert.SerializeObject(value, converter)
            | Some opts ->
                // STJ needs the static type to dispatch typed converters; use
                // the runtime type of the boxed value.
                let t = if isNull value then typeof<obj> else value.GetType()
                JsonSerializer.Serialize(value, t, opts)

        let serializeArgs (args: obj list) =
            match stjOptions with
            | None -> JsonConvert.SerializeObject(args, converter)
            | Some opts ->
                // Serialise as a typed obj[] so STJ writes [arg1, arg2, ...]
                // with each element going through its appropriate typed converter.
                let arr = args |> List.toArray
                let sb = StringBuilder()
                use sw = new System.IO.StringWriter(sb)
                use writer = new Utf8JsonWriter(new System.IO.MemoryStream())
                // Simpler: build a JSON array manually
                sb.Append '[' |> ignore
                args
                |> List.iteri (fun i a ->
                    if i > 0 then sb.Append ',' |> ignore
                    let t = if isNull a then typeof<obj> else a.GetType()
                    sb.Append (JsonSerializer.Serialize(a, t, opts)) |> ignore)
                sb.Append ']' |> ignore
                sb.ToString()

        if isMultipartEnabled && functionArguments |> List.exists (fun x -> x :? byte[]) then
            let f = new MultipartFormDataContent ()

            for arg in functionArguments do
                match arg with
                | :? (byte[]) as data ->
                    let c = new ByteArrayContent (data)
                    c.Headers.ContentType <- MediaTypeHeaderValue "application/octet-stream"
                    f.Add c
                | _ ->
                    let ser = serializeOne arg
                    f.Add (new StringContent (ser, Encoding.UTF8, "application/json"))
            f
        else
            let ser = serializeArgs functionArguments
            new StringContent(ser, Encoding.UTF8, "application/json")

    let private parseResponse<'t> (stjOptions: JsonSerializerOptions option) (responseText: string) =
        match stjOptions with
        | None -> parseAs<'t> responseText
        | Some opts -> parseAsWith<'t> opts responseText

    /// Sends a POST request to the specified url with the arguments of serialized to an input list
    let proxyPostTask<'t> (functionArguments: obj list) url client isBinarySerialization isMultipartEnabled (stjOptions: JsonSerializerOptions option) =
        task {
            use content = createRequestBody functionArguments isMultipartEnabled stjOptions

            if isBinarySerialization then
                let! data = Http.makePostRequestBinaryResponse client url content
                return parseAsBinary<'t> data
            else
                let! responseText = Http.makePostRequest client url content
                return parseResponse<'t> stjOptions responseText
        }

    /// Sends a POST request to the specified url with the arguments of serialized to an input list
    let proxyPost<'t> (functionArguments: obj list) url client isBinarySerialization isMultipartEnabled (stjOptions: JsonSerializerOptions option) =
        proxyPostTask<'t> functionArguments url client isBinarySerialization isMultipartEnabled stjOptions |> Async.AwaitTask

    /// Sends a POST request to the specified url safely with the arguments of serialized to an input list, if an exception is thrown, is it caught
    let safeProxyPostTask<'t> (functionArguments: obj list) url client isBinarySerialization isMultipartEnabled (stjOptions: JsonSerializerOptions option) =
        task {
            use content = createRequestBody functionArguments isMultipartEnabled stjOptions

            if isBinarySerialization then
                match! taskCatch (fun () -> Http.makePostRequestBinaryResponse client url content) with
                | Choice1Of2 data -> return Ok (parseAsBinary<'t> data)
                | Choice2Of2 thrownException -> return Error thrownException
            else
                match! taskCatch (fun () -> Http.makePostRequest client url content) with
                | Choice1Of2 responseText -> return Ok (parseResponse<'t> stjOptions responseText)
                | Choice2Of2 thrownException -> return Error thrownException
        }

    /// Sends a POST request to the specified url safely with the arguments of serialized to an input list, if an exception is thrown, is it caught
    let safeProxyPost<'t> (functionArguments: obj list) url client isBinarySerialization isMultipartEnabled (stjOptions: JsonSerializerOptions option) =
        safeProxyPostTask<'t> functionArguments url client isBinarySerialization isMultipartEnabled stjOptions |> Async.AwaitTask

    type Proxy<'t>(builder, client: Option<HttpClient>, isBinarySerialization, ?isMultipartEnabled, ?stjOptions: JsonSerializerOptions) =
        let isMultipartEnabled = isMultipartEnabled |> Option.defaultValue false
        let stjOptions = stjOptions
        let typeName =
            let name = typeof<'t>.Name
            match typeof<'t>.GenericTypeArguments with
            | [|  |] -> name
            | manyArgs -> name.[0 .. name.Length - 3]
        let client = defaultArg client (new HttpClient())
        /// Uses the specified string as the authorization header for the requests that the proxy makes to the server
        member __.authorisationHeader (header: string) =
            client.DefaultRequestHeaders.Remove("Authorization") |> ignore
            client.DefaultRequestHeaders.Add("Authorization", header)
             
        member __.Call<'a> (expr: Expression<Func<'t, Async<'a>>>) : Task<'a> =
            let args = [  ]
            let memberExpr = unbox<MemberExpression> expr.Body  
            let functionName = memberExpr.Member.Name
            let route = builder typeName functionName
            proxyPostTask<'a> args route client isBinarySerialization isMultipartEnabled stjOptions

        member __.Call<'a, 'b> (expr: Expression<Func<'t, FSharpFunc<'a, Async<'b>>>>, input: 'a) : Task<'b> =
            let args = [ box input ]
            let memberExpr = unbox<MemberExpression> expr.Body  
            let functionName = memberExpr.Member.Name
            let route = builder typeName functionName
            proxyPostTask<'b> args route client isBinarySerialization isMultipartEnabled stjOptions

        member __.Call<'a, 'b, 'c> (expr: Expression<Func<'t, FSharpFunc<'a, FSharpFunc<'b, Async<'c>>>>>, arg1: 'a, arg2: 'b) : Task<'c> = 
            let args = [ box arg1; box arg2 ]
            let memberExpr = unbox<MemberExpression> expr.Body  
            let functionName = memberExpr.Member.Name
            let route = builder typeName functionName
            proxyPostTask<'c> args route client isBinarySerialization isMultipartEnabled stjOptions

        member __.Call<'a, 'b, 'c, 'd> (expr: Expression<Func<'t, FSharpFunc<'a, FSharpFunc<'b, FSharpFunc<'c, Async<'d>>>>>>, arg1: 'a, arg2: 'b, arg3: 'c) : Task<'d> = 
            let args = [ box arg1; box arg2; box arg3 ]
            let memberExpr = unbox<MemberExpression> expr.Body  
            let functionName = memberExpr.Member.Name
            let route = builder typeName functionName
            proxyPostTask<'d> args route client isBinarySerialization isMultipartEnabled stjOptions

        /// Returns a new Proxy that opts in to the System.Text.Json
        /// serialiser path with the provided options. Pair with
        /// `Fable.Remoting.Json.SystemTextJson.FableConverters.create()` to
        /// get an options bundle pre-configured for byte-compat with the
        /// Newtonsoft default.
        member __.WithSerializerOptions (opts: JsonSerializerOptions) : Proxy<'t> =
            Proxy<'t>(builder, Some client, isBinarySerialization, isMultipartEnabled, opts)

        /// Call the proxy function by wrapping it inside a quotation expr:
        /// ```
        /// async {
        ///     let proxy = Proxy.create<IServer> (sprintf "http://api.endpoint.org/api/%s/%s")
        ///     let! result = proxy.call <@ server -> server.getLength "input" @>
        ///  }
        /// ```
        member __.call<'u> ([<ReflectedDefinition>] expr: Quotations.Expr<'t -> Task<'u>>) =
            match expr with
            | ProxyLambda(methodName, args) ->
                let route = builder typeName methodName
                proxyPost<'u> args route client isBinarySerialization isMultipartEnabled stjOptions
            | otherwise -> failwithf "Failed to process the following quotation expression\n%A\nThis could be due to the fact that you are providing complex function paramters to your called proxy function like nested records with generic paramters or lists, if that is the case, try binding the paramter to a value outside the qoutation expression and pass that value to the function instead" expr

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
                proxyPost<'u> args route client isBinarySerialization isMultipartEnabled stjOptions
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
        member __.callSafely<'u> ([<ReflectedDefinition>] expr: Quotations.Expr<'t -> Task<'u>>) : Async<Result<'u, exn>> =
            match expr with
            | ProxyLambda(methodName, args) ->
                let route = builder typeName methodName
                safeProxyPost<'u> args route client isBinarySerialization isMultipartEnabled stjOptions
            | otherwise -> failwithf "Failed to process quotation expression\n%A\nThis could be due to the fact that you are providing complex function paramters to your called proxy function like nested records with generic paramters or lists, if that is the case, try binding the paramter to a value outside the qoutation expression and pass that value to the function instead" expr

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
                safeProxyPost<'u> args route client isBinarySerialization isMultipartEnabled stjOptions
            | otherwise -> failwithf "Failed to process quotation expression\n%A\nThis could be due to the fact that you are providing complex function paramters to your called proxy function like nested records with generic paramters or lists, if that is the case, try binding the paramter to a value outside the qoutation expression and pass that value to the function instead" expr

        /// Call the proxy function by wrapping it inside a quotation expr:
        /// ```
        /// task {
        ///     let proxy = Proxy.create<IServer> (sprintf "http://api.endpoint.org/api/%s/%s")
        ///     let! result = proxy.call <@ server -> server.getLength "input" @>
        ///  }
        /// ```
        member __.callTask<'u> ([<ReflectedDefinition>] expr: Quotations.Expr<'t -> Task<'u>>) =
            match expr with
            | ProxyLambda(methodName, args) ->
                let route = builder typeName methodName
                proxyPostTask<'u> args route client isBinarySerialization isMultipartEnabled stjOptions
            | otherwise -> failwithf "Failed to process the following quotation expression\n%A\nThis could be due to the fact that you are providing complex function paramters to your called proxy function like nested records with generic paramters or lists, if that is the case, try binding the paramter to a value outside the qoutation expression and pass that value to the function instead" expr

        /// Call the proxy function by wrapping it inside a quotation expr:
        /// ```
        /// task {
        ///     let proxy = Proxy.create<IServer> (sprintf "http://api.endpoint.org/api/%s/%s")
        ///     let! result = proxy.call <@ server -> server.getLength "input" @>
        ///  }
        /// ```
        member __.callTask<'u> ([<ReflectedDefinition>] expr: Quotations.Expr<'t -> Async<'u>>) =
            match expr with
            | ProxyLambda(methodName, args) ->
                let route = builder typeName methodName
                proxyPostTask<'u> args route client isBinarySerialization isMultipartEnabled stjOptions
            | otherwise -> failwithf "Failed to process the following quotation expression\n%A\nThis could be due to the fact that you are providing complex function paramters to your called proxy function like nested records with generic paramters or lists, if that is the case, try binding the paramter to a value outside the qoutation expression and pass that value to the function instead" expr

        /// Call the proxy function safely by wrapping it inside a quotation expr and catching any thrown exception by the web request
        /// ```
        ///    task {
        ///       let proxy = Proxy.create<IServer> (sprintf "http://api.endpoint.org/api/%s/%s")
        ///       let! result = proxy.callSafely <@ server -> server.getLength "input" @>
        ///       match result with
        ///       | Ok result -> (* do stuff with result *)
        ///       | Error ex -> (* panic! *)
        ///    }
        /// ```
        member __.callSafelyTask<'u> ([<ReflectedDefinition>] expr: Quotations.Expr<'t -> Task<'u>>) : Task<Result<'u, exn>> =
            match expr with
            | ProxyLambda(methodName, args) ->
                let route = builder typeName methodName
                safeProxyPostTask<'u> args route client isBinarySerialization isMultipartEnabled stjOptions
            | otherwise -> failwithf "Failed to process quotation expression\n%A\nThis could be due to the fact that you are providing complex function paramters to your called proxy function like nested records with generic paramters or lists, if that is the case, try binding the paramter to a value outside the qoutation expression and pass that value to the function instead" expr

        /// Call the proxy function safely by wrapping it inside a quotation expr and catching any thrown exception by the web request
        /// ```
        ///    task {
        ///       let proxy = Proxy.create<IServer> (sprintf "http://api.endpoint.org/api/%s/%s")
        ///       let! result = proxy.callSafely <@ server -> server.getLength "input" @>
        ///       match result with
        ///       | Ok result -> (* do stuff with result *)
        ///       | Error ex -> (* panic! *)
        ///    }
        /// ```
        member __.callSafelyTask<'u> ([<ReflectedDefinition>] expr: Quotations.Expr<'t -> Async<'u>>) : Task<Result<'u, exn>> =
            match expr with
            | ProxyLambda(methodName, args) ->
                let route = builder typeName methodName
                safeProxyPostTask<'u> args route client isBinarySerialization isMultipartEnabled stjOptions
            | otherwise -> failwithf "Failed to process quotation expression\n%A\nThis could be due to the fact that you are providing complex function paramters to your called proxy function like nested records with generic paramters or lists, if that is the case, try binding the paramter to a value outside the qoutation expression and pass that value to the function instead" expr

    /// Creates a proxy for a type with a route builder
    let create<'t> builder = Proxy<'t>(builder, None, false)
    /// Creates a proxy with for a type using a route builder and a custom HttpClient that you provide
    let custom<'t> builder client isBinarySerialization = Proxy<'t>(builder, Some client, isBinarySerialization)

    let CreateFromBuilder<'t>(f: Func<string, string, string>, isBinarySerialization) =
        let builder = fun typeName funcName -> f.Invoke(typeName, funcName)
        Proxy<'t>(builder, None, isBinarySerialization)
    let CreateFromBuilderAndClient<'t>(f: Func<string, string, string>, client: HttpClient, isBinarySerialization) = 
        let builder = fun typeName funcName -> f.Invoke(typeName, funcName)
        Proxy<'t>(builder, Some client, isBinarySerialization)