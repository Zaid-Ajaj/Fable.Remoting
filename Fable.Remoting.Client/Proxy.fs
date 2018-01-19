namespace Fable.Remoting.Client

open FSharp.Reflection
open Fable.PowerPack
open Fable.Core
open Fable.Core.JsInterop
open Fable.PowerPack.Fetch

module Proxy = 
    type ErrorInfo = { 
        path: string; 
        methodName: string;
        error: string;
        response: Response
    }

    let mutable private errorHandler : Option<ErrorInfo -> unit> = None

    /// When an error is thrown on the server and it is intercepted by the global `onError` handler, the server can either ignore the error or propagate an object that contains information of the server. In the case that a message is propagated then this error handler intercepts that error serialized as json along with the route and response information 
    let onError (handler: ErrorInfo -> unit) = 
        errorHandler <- Some handler

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
    let private proxyFetch typeName methodName returnType (endpoint: string option) (routeBuilder: string -> string -> string) =
        fun data -> 
            let route = routeBuilder typeName methodName
            let url = 
              match endpoint with
              | Some path -> 
                 if path.EndsWith("/") 
                 then sprintf "%s%s" path route
                 else sprintf "%s/%s" path route
              | None -> route
            promise {
                let requestProps = [
                    Body (unbox (toJson data))
                    Method HttpMethod.POST
                ] 
                let makeReqProps props = 
                    keyValueList CaseRules.LowerFirst props :?> RequestInit

                let! response = GlobalFetch.fetch(RequestInfo.Url url, makeReqProps requestProps)
                //let! response = Fetch.fetch url requestProps
                let! jsonResponse = response.text()
                match response.Status with
                | 200 -> 
                    // success result
                    return ofJsonAsType jsonResponse returnType
                | 500 -> 
                    // Error from server
                    let customError = jsonParse jsonResponse
                    match getAs<bool> customError "ignored" with
                    | true -> return! failwith (getAs<string> customError "error")
                    | false -> 
                        match getAs<bool> customError "handled" with
                        | false -> return! failwith (getAs<string> customError "error")
                        | true -> 
                            // handled and not ignored -> error message propagated
                            let error = stringify (getAs<obj> customError "error")
                            let errorInfo = 
                              { path = url; 
                                methodName = methodName; 
                                error = error;
                                response = response }
                            match errorHandler with
                            | Some handler -> 
                                handler errorInfo
                                return! failwith "Server error"
                            | None -> return! failwith "Server error"
                | _ -> return! failwith "Unknown response status"
            }
            |> Async.AwaitPromise   

    let private funcNotSupportedMsg funcName = 
        [ sprintf "Fable.Remoting.Client: Function %s cannot be used for client proxy" funcName
          "Fable.Remoting.Client: Only functions with 1 paramter are supported" ]
        |> String.concat "\n"

    [<PassGenerics>]
    let private fields<'t> = 
        FSharpType.GetRecordFields typeof<'t>
        |> Seq.filter (fun propInfo -> FSharpType.IsFunction (propInfo.PropertyType))
        |> Seq.map (fun propInfo -> 
            let funcName = propInfo.Name
            let funcParamterTypes = 
                FSharpType.GetFunctionElements (propInfo.PropertyType)
                |> typed<System.Type []>
            if Seq.length funcParamterTypes > 2 then 
                failwith (funcNotSupportedMsg funcName)
            else (funcName, funcParamterTypes)
        )
        |> List.ofSeq

    /// Creates a proxy using a custom endpoint and a route builder
    let [<PassGenerics>] createWithEndpointAndBuilder<'t> (endpoint: string option) (routeBuilder : string -> string -> string): 't = 
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
            setProp fieldName (proxyFetch typeName fieldName returnType endpoint routeBuilder) proxy
        )
        unbox proxy



    /// Creates a proxy that routes method calls to /typeName/methodName
    let [<PassGenerics>] create<'t>  : 't = 
        createWithEndpointAndBuilder<'t> (Some "/") (sprintf "/%s/%s")

    /// Creates a proxy using a custom endpoint and the default route builder.
    [<PassGenerics>]
    let createWithEndpoint<'t> (endpoint: string) : 't = 
        createWithEndpointAndBuilder<'t> (Some endpoint) (sprintf "/%s/%s")

    /// Creates a proxy using the default endpoint = "/" and a custom route builder
    [<PassGenerics>]
    let createWithBuilder<'t> (routeBuilder: string -> string -> string) : 't = 
        createWithEndpointAndBuilder<'t> None routeBuilder