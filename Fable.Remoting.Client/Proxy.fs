namespace Fable.Remoting.Client

open FSharp.Reflection
open Fable.PowerPack
open Fable.Core
open Fable.Core.JsInterop
open Fable.Import.Browser
open Fable.PowerPack.Fetch

module Proxy =
    type ErrorInfo = {
        path: string;
        methodName: string;
        error: string;
        response: Response
    }
    /// On Unauthorized error handler (for backward compatibility)

    let mutable private authHandler : Option<string option -> unit> = None
    /// On Forbidden error handler (for backward compatibility)
    let mutable private forbiddenHandler : Option<string option -> unit> = None
    /// On Server error handler (for backward compatibility)
    let mutable private errorHandler : Option<ErrorInfo -> unit> = None
    /// For backward compatibility. Prefer `use_error_handler` on the computation expression instead
    /// When an error is thrown on the server and it is intercepted by the global `onError` handler,
    /// the server can either ignore the error or propagate an object that contains information of the server.
    /// In the case that a message is propagated then this error handler intercepts that error serialized as json along with the route and response information
    let onError (handler: ErrorInfo -> unit) =
        errorHandler <- Some handler
    /// For backward compatibility. Prefer `use_auth_error_handler` on the computation expression instead
    /// When an unauthorized error is thrown on the server, this handler intercepts that error along with the optional authorization string information
    let onAuthError (handler: string option -> unit) =
        authHandler <- Some handler
    /// For backward compatibility. Prefer `use_forbidden_error_handler` on the computation expression instead
    /// When a forbidden error is thrown on the server, this handler intercepts that error along with the optional authorization string information
    let onForbiddenError (handler: string option -> unit) =
        forbiddenHandler <- Some handler

    type RemoteBuilderOptions =
        {
           AuthErrorHandler         : (string option -> unit) option
           ForbiddenErrorHandler    : (string option -> unit) option
           ServerErrorHandler       : (ErrorInfo -> unit) option
           Endpoint                 : string option
           Authorization            : string option
           Builder                  : (string -> string -> string)
        }
        with
            static member Empty =  {
                   AuthErrorHandler      = None
                   ForbiddenErrorHandler = None
                   ServerErrorHandler    = None
                   Endpoint              = None
                   Authorization         = None
                   Builder               = sprintf ("/%s/%s")
                }
    [<Emit("$2[$0] = $1")>]
    let private setProp (propName: string) (propValue: obj) (any: obj) : unit = jsNative

    [<Emit("$0")>]
    let private typed<'a> (x: obj) : 'a = jsNative

    [<Emit("$0[$1]")>]
    let private getAs<'a> (x: obj) (key: string) : 'a = jsNative
    [<Emit("JSON.parse($0)")>]
    let private jsonParse (content: string) : obj = jsNative
    [<Emit("JSON.stringify($0)")>]
    let private stringify (x: obj) : string = jsNative
    [<PassGenerics>]
    let private fields<'t> =
                FSharpType.GetRecordFields typeof<'t>
                |> Seq.filter (fun propInfo -> FSharpType.IsFunction (propInfo.PropertyType))
                |> Seq.map (fun propInfo ->
                    let funcName = propInfo.Name
                    let funcParamterTypes =
                        FSharpType.GetFunctionElements (propInfo.PropertyType)
                        |> typed<System.Type []>
                    (funcName, funcParamterTypes)
                )
                |> List.ofSeq

    let private proxyFetch options typeName methodName returnType =
        fun arg0 arg1 arg2 arg3 arg4 arg5 arg6 arg7 arg8 arg9 arg10 arg11 arg12 arg13 arg14 arg15->
            let data = [
                box arg0;box arg1;box arg2;box arg3;box arg4;box arg5;box arg6;box arg7;box arg8;box arg9;box arg10;box arg11;box arg12;box arg13;box arg14;box arg15
             ]
            let route = options.Builder typeName methodName
            let url =
              match options.Endpoint with
              | Some path ->
                 if path.EndsWith("/")
                 then sprintf "%s%s" path route
                 else sprintf "%s/%s" path route
              | None -> route
            promise {
                // Send RPC POST request to the server
                let requestProps = [
                    Body (unbox (toJson data))
                    Method HttpMethod.POST
                    Credentials RequestCredentials.Sameorigin
                    requestHeaders
                     [ yield ContentType "application/json; charset=utf8";
                       yield Cookie document.cookie
                       match options.Authorization with
                       | Some auth -> yield Authorization auth
                       | None -> ()  ]
                ]

                let makeReqProps props =
                    keyValueList CaseRules.LowerFirst props :?> RequestInit
                // use GlobalFetch.fetch to control error handling
                let! response = GlobalFetch.fetch(RequestInfo.Url url, makeReqProps requestProps)
                //let! response = Fetch.fetch url requestProps
                let! jsonResponse = response.text()
                match response.Status with
                | 200 ->
                    // success result
                    return ofJsonAsType jsonResponse returnType
                | 401  ->
                    // unauthorized result
                    match options.AuthErrorHandler with
                    |Some handler -> handler options.Authorization
                    |None -> ()
                    return! failwith "Auth error"
                | 403  ->
                    // forbidden result
                    match forbiddenHandler with
                    |Some handler -> handler options.Authorization
                    |None -> ()
                    return! failwith "Forbidden error"
                | 500 ->
                    // Error from server
                    let customError = jsonParse jsonResponse
                    // manually read properties of the the object literal representing the error
                    match getAs<bool> customError "ignored" with
                    | true -> return! failwith (getAs<string> customError "error")
                    | false ->
                        match getAs<bool> customError "handled" with
                        | false ->
                            // throw a generic server error because it was not handled on the server
                            return! failwith (getAs<string> customError "error")
                        | true ->
                            // handled and not ignored -> error message propagated
                            let error = stringify (getAs<obj> customError "error")
                            // collect error information along the response data
                            let errorInfo =
                              { path = url;
                                methodName = methodName;
                                error = error;
                                response = response }
                            // send the error to the client side error handler
                            match options.ServerErrorHandler with
                            | Some handler ->
                                handler errorInfo
                                return! failwith "Server error"
                            | None -> return! failwith "Server error"
                | _ -> return! failwith "Unknown response status"
            }
            |> Async.AwaitPromise

    type RemoteBuilder<'a>() =
            member __.Yield(_) =
                RemoteBuilderOptions.Empty
            /// Enables empty computation expression
            member __.Zero() =
                RemoteBuilderOptions.Empty

            [<PassGenerics>]
            member __.Run(state) : 't =
                // create an empty object literal
                let proxy = obj()
                let typeName = typeof<'t>.Name
                let fields = fields<'t>
                fields |> List.iter (fun field ->
                    let funcTypes = snd field
                    // Async<T>
                    let asyncOfreturnType = funcTypes |> Array.last
                    // T
                    let returnType = asyncOfreturnType.GenericTypeArguments.[0]
                    let fieldName = fst field
                    let normalize n =
                        let fn = proxyFetch state typeName fieldName returnType
                        match n with
                        |0 ->
                            box (fn null null null null null null null null null null null null null null null null)
                        |1 ->
                            box (fun a -> fn a null null null null null null null null null null null null null null null)
                        |2 ->
                            box (fun a b -> fn a b null null null null null null null null null null null null null null)
                        |3 ->
                            box (fun a b c -> fn a b c null null null null null null null null null null null null null)
                        |4 ->
                            box (fun a b c d -> fn a b c d null null null null null null null null null null null null)
                        |5 ->
                            box (fun a b c d e -> fn a b c d e null null null null null null null null null null null)
                        |6 ->
                            box (fun a b c d e f -> fn a b c d e f null null null null null null null null null null)
                        |7 ->
                            box (fun a b c d e f g -> fn a b c d e f g null null null null null null null null null)
                        |8 ->
                            box (fun a b c d e f g h -> fn a b c d e f g h null null null null null null null null)
                        |9 ->
                            box (fun a b c d e f g h i -> fn a b c d e f g h i null null null null null null null)
                        |10 ->
                            box (fun a b c d e f g h i j -> fn a b c d e f g h i j null null null null null null)
                        |11 ->
                            box (fun a b c d e f g h i j k -> fn a b c d e f g h i j k null null null null null)
                        |12 ->
                            box (fun a b c d e f g h i j k l -> fn a b c d e f g h i j k l null null null null)
                        |13 ->
                            box (fun a b c d e f g h i j k l m -> fn a b c d e f g h i j k l m null null null)
                        |14 ->
                            box (fun a b c d e f g h i j k l m n -> fn a b c d e f g h i j k l m n null null)
                        |15 ->
                            box (fun a b c d e f g h i j k l m n o -> fn a b c d e f g h i j k l m n o null)
                        |16 ->
                            box fn
                        |_ -> failwith "Only up to 16 arguments are supported"
                        
                    setProp fieldName (normalize (funcTypes.Length-1)) proxy
                )
                unbox proxy
            /// Pins the proxy at an endpoint
            [<CustomOperation("at_endpoint")>]
            member __.AtEndpoint(state,endpoint) =
                {state with Endpoint = Some endpoint}
            /// Pins the proxy at an optional endpoint. For backward compatibility
            [<CustomOperation("at_some_endpoint")>]
            [<System.Obsolete("For backward compatibility only.")>]
            member __.AtSomeEndpoint(state,endpoint) =
                {state with Endpoint = endpoint}
            /// Sets an optional error handler for server errors.
            [<CustomOperation("use_error_handler")>]
            member __.UseErrorHandler(state,errorHandler) =
                {state with ServerErrorHandler = Some errorHandler}
            /// Sets an optional error handler for server errors. For backward compatibility
            [<CustomOperation("use_some_error_handler")>]
            [<System.Obsolete("For backward compatibility only.")>]
            member __.UseSomeErrorHandler(state,errorHandler) =
                {state with ServerErrorHandler = errorHandler}
            /// Sets an error handler that takes the optional authorization used on the request header for unauthorized errors.
            [<CustomOperation("use_auth_error_handler")>]
            member __.UseAuthErrorHandler(state,errorHandler) =
                {state with AuthErrorHandler = Some errorHandler}
            /// Sets an optional error handler that takes the optional authorization used on the request header for unauthorized errors. For backward compatibility
            [<CustomOperation("use_some_auth_error_handler")>]
            [<System.Obsolete("For backward compatibility only.")>]
            member __.UseSomeAuthErrorHandler(state,errorHandler) =
                {state with AuthErrorHandler = errorHandler}
            /// Sets an error handler that takes the optional authorization used on the request header for forbidden errors.
            [<CustomOperation("use_forbidden_error_handler")>]
            member __.UseForbiddenErrorHandler(state,errorHandler) =
                {state with ForbiddenErrorHandler = Some errorHandler}
            /// Sets an optional error handler that takes the optional authorization used on the request header for forbidden errors. For backward compatibility
            [<System.Obsolete("For backward compatibility only.")>]
            [<CustomOperation("use_some_forbidden_error_handler")>]
            member __.UseSomeForbiddenErrorHandler(state,errorHandler) =
                {state with ForbiddenErrorHandler = errorHandler}
            /// Sets an authorization string to send with the request onto the Authorization header.
            [<CustomOperation("with_token")>]
            member __.WithToken(state,token) =
                {state with Authorization = Some token}
            /// Sets an optional authorization string to send with the request onto the Authorization header. For backward compatibility
            [<CustomOperation("with_some_token")>]
            [<System.Obsolete("For backward compatibility only.")>]
            member __.WithSomeToken(state,token) =
                {state with Authorization = token}
            /// Sets a builder that takes the implementation type and method name. Used to define the proxy path
            [<CustomOperation("with_builder")>]
            member __.WithBuilder(state,builder) =
                {state with Builder = builder}
    /// Computation expression to create a remoting proxy.
    /// Usage:
    /// `let proxy : IType = remoting {()}` for default options at /typeName/methodName
    /// `let proxy : IType = remoting {`
    /// `    with_builder builder` to set a `builder : (string -> string -> string)`
    /// `    at_endpoint endpoint` to set a prefix `endpoint : string`
    /// `    with_token token` to set a `token : string` to be sent on the Authorization header
    /// `    use_error_handler handler` to set a `handler : (ErrorInfo -> unit)` in case of a server error
    /// `    use_auth_error_handler handler` to set a `handler : (string option -> unit)` in case of a Unauthorized error
    /// `    use_forbidden_error_handler handler` to set a `handler : (string option -> unit)` in case of a Forbidden error
    /// `}`
    [<PassGenerics>]
    let remoting<'t> = RemoteBuilder()
    let [<PassGenerics>] private createSecureWithEndpointAndBuilderImpl<'t> (endpoint: string option) (routeBuilder : string -> string -> string) (auth: string option) : 't =
        remoting {
            with_builder routeBuilder
            with_some_token auth
            at_some_endpoint endpoint
            use_some_error_handler errorHandler
            use_some_forbidden_error_handler forbiddenHandler
            use_some_auth_error_handler authHandler
        }

    /// Creates a proxy using a custom endpoint and a route builder
    let [<PassGenerics>] createWithEndpointAndBuilder<'t> (endpoint: string option) (routeBuilder : string -> string -> string): 't =
        createSecureWithEndpointAndBuilderImpl<'t> endpoint routeBuilder None

    /// Creates a proxy that routes method calls to /typeName/methodName
    let [<PassGenerics>] create<'t>  : 't =
        createSecureWithEndpointAndBuilderImpl<'t> (Some "/") (sprintf "/%s/%s") None

    /// Creates a proxy using a custom endpoint and the default route builder.
    [<PassGenerics>]
    let createWithEndpoint<'t> (endpoint: string) : 't =
        createSecureWithEndpointAndBuilderImpl<'t> (Some endpoint) (sprintf "/%s/%s") None

    /// Creates a proxy using the default endpoint = "/" and a custom route builder
    [<PassGenerics>]
    let createWithBuilder<'t> (routeBuilder: string -> string -> string) : 't =
        createSecureWithEndpointAndBuilderImpl<'t> None routeBuilder None

    /// Creates a secure proxy using a custom endpoint and a route builder
    let [<PassGenerics>] createSecureWithEndpointAndBuilder<'t> (endpoint: string option) (routeBuilder : string -> string -> string) (auth: string): 't =
        createSecureWithEndpointAndBuilderImpl<'t> endpoint routeBuilder (Some auth)

    /// Creates a secure proxy that routes method calls to /typeName/methodName
    let [<PassGenerics>] createSecure<'t>  (auth: string): 't =
        createSecureWithEndpointAndBuilderImpl<'t> (Some "/") (sprintf "/%s/%s") (Some auth)

    /// Creates a secure proxy using a custom endpoint and the default route builder.
    [<PassGenerics>]
    let createSecureWithEndpoint<'t> (endpoint: string) (auth: string) : 't =
        createSecureWithEndpointAndBuilderImpl<'t> (Some endpoint) (sprintf "/%s/%s") (Some auth)

    /// Creates a secure proxy using the default endpoint = "/" and a custom route builder
    [<PassGenerics>]
    let createSecureWithBuilder<'t> (routeBuilder: string -> string -> string) (auth: string) :  't =
        createSecureWithEndpointAndBuilderImpl<'t> None routeBuilder (Some auth)
