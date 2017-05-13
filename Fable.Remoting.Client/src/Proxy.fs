namespace Fable.Remoting.Client

open FSharp.Reflection
open Fable.PowerPack
open Fable.Core
open Fable.Core.JsInterop
open Fable.PowerPack.Fetch
open Fable.PowerPack.Fetch.Fetch_types

module Proxy = 

    [<Emit("$2[$0] = $1")>]
    let setProp (propName: string) (propValue: obj) (any: obj) : unit = jsNative

    let makeTypeArgument (typeArg: System.Type) = 
        let empty = new obj()
        setProp "T" typeArg empty
        empty

    [<Import("ofJson",  "fable-core/Serialize")>]
    let dynamicOfJson(json: string, typeArg: obj) : obj = jsNative

    [<Emit("$0")>]
    let typed<'a> (x: obj) : 'a = jsNative

    let proxyFetch typeName methodName returnType (endpoint: string option) (routeBuilder: string -> string -> string) =
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
                let! response = Fetch.fetch url requestProps 
                let! jsonResponse = response.text()
                let typeArg = makeTypeArgument returnType
                return dynamicOfJson(jsonResponse, typeArg)
            }
            |> Async.AwaitPromise   

    let funcNotSupportedMsg funcName = 
        [ sprintf "Fable.Remoting.Client: Function %s cannot be used for client proxy" funcName
          "Fable.Remoting.Client: Only functions with 1 paramter are supported" ]
        |> String.concat "\n"


    [<PassGenerics>]
    let fields<'t> = 
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


    [<PassGenerics>]
    let create<'t> : 't = 
        // create an empty object literal
        let proxy = obj()
        let typeName = typeof<'t>.Name
        fields<'t>
        |> List.iter (fun field ->
            let funcTypes = snd field
            // Async<T>
            let asyncOfreturnType = funcTypes.[1] 
            // T
            let returnType = asyncOfreturnType.GenericTypeArguments.[0]
            let fieldName = fst field
            setProp fieldName (proxyFetch typeName fieldName returnType None (sprintf "/%s/%s")) proxy
        )
        unbox proxy

    [<PassGenerics>]
    let createWithEndpoint<'t> (endpoint: string) : 't = 
        // create an empty object literal
        let proxy = obj()
        let typeName = typeof<'t>.Name
        let fields = fields<'t>
        for field in fields do
            let funcTypes = snd field
            // Async<T>
            let asyncOfreturnType = funcTypes.[1] 
            // T
            let returnType = asyncOfreturnType.GenericTypeArguments.[0]
            let fieldName = fst field
            setProp fieldName (proxyFetch typeName fieldName returnType (Some endpoint) (sprintf "/%s/%s")) proxy
        unbox proxy

    [<PassGenerics>]
    let createWithEndpointAndBuilder<'t> (endpoint: string) (routeBuilder : string -> string -> string): 't = 
        // create an empty object literal
        let proxy = obj()
        let typeName = typeof<'t>.Name
        let fields = fields<'t>
        for field in fields do
            let funcTypes = snd field
            // Async<T>
            let asyncOfreturnType = funcTypes.[1] 
            // T
            let returnType = asyncOfreturnType.GenericTypeArguments.[0]
            let fieldName = fst field
            setProp fieldName (proxyFetch typeName fieldName returnType (Some endpoint) routeBuilder) proxy
        unbox proxy