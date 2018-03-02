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

    let mutable private authHandler : Option<string option -> unit> = None
    let mutable private forbiddenHandler : Option<string option -> unit> = None
    let mutable private errorHandler : Option<ErrorInfo -> unit> = None

    /// When an error is thrown on the server and it is intercepted by the global `onError` handler, the server can either ignore the error or propagate an object that contains information of the server. In the case that a message is propagated then this error handler intercepts that error serialized as json along with the route and response information 
    let onError (handler: ErrorInfo -> unit) = 
        errorHandler <- Some handler
    
    /// When an unauthorized error is thrown on the server, this handler intercepts that error along with the optional authorization string information
    let onAuthError (handler: string option -> unit) = 
        authHandler <- Some handler

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
            
    type RemoteBuilder<'a>() =
            [<PassGenerics>]
            let proxyFetch options typeName methodName returnType =
                fun arg0 arg1 arg2 arg3 arg4 arg5 arg6 arg7 arg8 arg9 arg10 arg11 arg12 ->
                    let data = [
                        arg0;arg1;arg2;arg3;arg4;arg5;arg6;arg7;arg8;arg9;arg10;arg11;arg12
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

            let funcNotSupportedMsg funcName = 
                [ sprintf "Fable.Remoting.Client: Function %s cannot be used for client proxy" funcName
                  "Fable.Remoting.Client: Only functions with 1 paramter are supported" ]
                |> String.concat "\n"

            

            member __.Yield(_) =
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
                    let asyncOfreturnType = funcTypes.[1] 
                    // T
                    let returnType = asyncOfreturnType.GenericTypeArguments.[0]
                    let fieldName = fst field
                    setProp fieldName (proxyFetch state typeName fieldName returnType) proxy
                )
                unbox proxy    
            [<CustomOperation("at_endpoint")>]
            member __.AtEndpoint(state,endpoint) =
                {state with Endpoint = Some endpoint}
            
            [<CustomOperation("at_some_endpoint")>]
            member __.AtSomeEndpoint(state,endpoint) =
                {state with Endpoint = endpoint}

            [<CustomOperation("use_error_handler")>]
            member __.UseErrorHandler(state,errorHandler) =
                {state with ServerErrorHandler = Some errorHandler}
            
            [<CustomOperation("use_some_error_handler")>]
            member __.UseSomeErrorHandler(state,errorHandler) =
                {state with ServerErrorHandler = errorHandler}

            [<CustomOperation("use_auth_error_handler")>]
            member __.UseAuthErrorHandler(state,errorHandler) =
                {state with AuthErrorHandler = Some errorHandler}
            
            [<CustomOperation("use_some_auth_error_handler")>]
            member __.UseSomeAuthErrorHandler(state,errorHandler) =
                {state with AuthErrorHandler = errorHandler}

            [<CustomOperation("use_forbidden_error_handler")>]
            member __.UseForbiddenErrorHandler(state,errorHandler) =
                {state with ForbiddenErrorHandler = Some errorHandler}
            
            [<CustomOperation("use_some_forbidden_error_handler")>]
            member __.UseSomeForbiddenErrorHandler(state,errorHandler) =
                {state with ForbiddenErrorHandler = errorHandler}

            [<CustomOperation("with_token")>]
            member __.WithToken(state,token) =
                {state with Authorization = Some token}
            
            [<CustomOperation("with_some_token")>]
            member __.WithSomeToken(state,token) =
                {state with Authorization = token}
            
            [<CustomOperation("with_builder")>]
            member __.WithBuilder(state,builder) =
                {state with Builder = builder}
            
            [<CustomOperation("with_default")>]
            member __.WithDefault(state) =
                {state with Builder = RemoteBuilderOptions.Empty.Builder}
            

    [<PassGenerics>]
    let remoting<'a> = RemoteBuilder()

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
