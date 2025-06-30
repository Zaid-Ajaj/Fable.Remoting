namespace Fable.Remoting.Client

open Fable.Core.JsInterop
open Fable.SimpleJson
open System
open FSharp.Reflection

module Proxy =
    let combineRouteWithBaseUrl route (baseUrl: string option) =
        match baseUrl with
        | None -> route
        | Some url -> sprintf "%s%s" (url.TrimEnd('/')) route

    let isByteArray = function
        | TypeInfo.Array getElemType ->
            match getElemType() with
            | TypeInfo.Byte -> true
            | otherwise -> false
        | otherwise -> false

    let isAsyncOfByteArray = function
        | TypeInfo.Async getAsyncType ->
            match getAsyncType() with
            | TypeInfo.Array getElemType ->
                match getElemType() with
                | TypeInfo.Byte -> true
                | otherwise -> false
            | otherwise -> false
        | otherwise -> false

    let rec getReturnType typ =
        if FSharpType.IsFunction typ then
            let _, res = FSharpType.GetFunctionElements typ
            getReturnType res
        elif typ.IsGenericType then
            typ.GetGenericArguments () |> Array.head
        else
            typ

    let rec flattenFuncTypes typ = [|
        if FSharpType.IsFunction typ then
            let (domain, range) = FSharpType.GetFunctionElements typ 
            yield! flattenFuncTypes domain 
            yield! flattenFuncTypes range
        else
            yield typ
    |]

    let proxyFetch options typeName (func: RecordField) fieldType =
        let funcArgs : (TypeInfo [ ]) =
            match func.FieldType with
            | TypeInfo.Async inner -> [| func.FieldType |]
            | TypeInfo.Promise inner -> [| func.FieldType |]
            | TypeInfo.Func getArgs -> getArgs()
            | _ -> failwithf "Field %s does not have a valid definiton" func.FieldName

        let argumentCount = (Array.length funcArgs) - 1
        let returnTypeAsync = Array.last funcArgs

        let isMultipart =
            match func.FieldType with
            | TypeInfo.Func getArgs ->
                let args = getArgs ()
                options.IsMultipartEnabled && (Array.exists isByteArray args || (args.Length > 2 && options.CustomRequestSerialization.IsSome))
            | otherwise -> false

        let route = options.RouteBuilder typeName func.FieldName
        let url = combineRouteWithBaseUrl route options.BaseUrl
        let funcNeedParameters =
            match funcArgs with
            | [| TypeInfo.Async _ |] -> false
            | [| TypeInfo.Promise _ |] -> false
            | [| TypeInfo.Unit; TypeInfo.Async _ |] -> false
            | otherwise -> true

        let flattenedFuncType = flattenFuncTypes fieldType |> Array.take argumentCount
        let inputArgumentTypes = Array.take argumentCount funcArgs

        let headers = [
            // xhr will set content-type and boundary for multipart
            match options.CustomRequestSerialization with
            | _ when isMultipart -> ()
            | Some _ -> yield "Content-Type", "application/vnd.msgpack"
            | _ -> yield "Content-Type", "application/json; charset=utf-8"

            yield "x-remoting-proxy", "true"
            yield! options.CustomHeaders
            match options.Authorization with
            | Some authToken -> yield "Authorization", authToken
            | None -> () ]

        let executeRequest =
            if options.CustomResponseSerialization.IsSome || isAsyncOfByteArray returnTypeAsync then
                let onOk =
                    match options.CustomResponseSerialization with
                    | Some serializer ->
                        let returnType = getReturnType fieldType
                        fun response -> serializer response returnType
                    | _ -> box

                fun requestBody -> async {
                    // read as arraybuffer and deserialize
                    let! (response, statusCode) =
                        if funcNeedParameters then
                            Http.post url
                            |> Http.withBody requestBody
                            |> Http.withHeaders headers
                            |> Http.withCredentials options.WithCredentials
                            |> Http.sendAndReadBinary
                        else
                            Http.get url
                            |> Http.withHeaders headers
                            |> Http.withCredentials options.WithCredentials
                            |> Http.sendAndReadBinary

                    match statusCode with
                    | 200 ->
                        return onOk response
                    | n ->
                        let responseAsBlob = InternalUtilities.createBlobWithMimeType !^response "text/plain"
                        let! responseText = InternalUtilities.readBlobAsText responseAsBlob
                        let response = { StatusCode = statusCode; ResponseBody = responseText }
                        let errorMsg = if n = 500 then sprintf "Internal server error (500) while making request to %s" url else sprintf "Http error (%d) while making request to %s" n url
                        return! raise (ProxyRequestException(response, errorMsg, response.ResponseBody))
                }
            else
                let returnType =
                    match returnTypeAsync with
                    | TypeInfo.Async getAsyncTypeArgument -> getAsyncTypeArgument()
                    | TypeInfo.Promise getPromiseTypeArgument -> getPromiseTypeArgument()
                    | TypeInfo.Any getReturnType ->
                        let t = getReturnType()
                        if t.FullName.StartsWith "System.Threading.Tasks.Task`1" then
                            t.GetGenericArguments().[0] |> createTypeInfo
                        else
                            failwithf "Expected field %s to have a return type of Async<'t> or Task<'t>" func.FieldName
                    | _ -> failwithf "Expected field %s to have a return type of Async<'t> or Task<'t>" func.FieldName

                fun requestBody -> async {
                    // make plain RPC request and let it go through the deserialization pipeline
                    let! response =
                        if funcNeedParameters then
                            Http.post url
                            |> Http.withBody requestBody
                            |> Http.withHeaders headers
                            |> Http.withCredentials options.WithCredentials
                            |> Http.send
                        else
                            Http.get url
                            |> Http.withHeaders headers
                            |> Http.withCredentials options.WithCredentials
                            |> Http.send

                    match response.StatusCode with
                    | 200 ->
                        let parsedJson = SimpleJson.parseNative response.ResponseBody
                        return Convert.fromJsonAs parsedJson returnType
                    | 500 -> return! raise (ProxyRequestException(response, sprintf "Internal server error (500) while making request to %s" url, response.ResponseBody))
                    | n ->   return! raise (ProxyRequestException(response, sprintf "Http error (%d) from server occured while making request to %s" n url, response.ResponseBody))
                }

        fun arg0 arg1 arg2 arg3 arg4 arg5 arg6 arg7 ->
            let inputArguments =
               if funcNeedParameters
               then Array.take argumentCount [| box arg0;box arg1;box arg2;box arg3; box arg4; box arg5; box arg6; box arg7 |]
               else [| |]

            let requestBody =
                if isMultipart then
                    inputArguments
                    |> Array.mapi (fun i x ->
                        let typ = inputArgumentTypes.[i]

                        // in theory the input byte array could be untyped, so it's better to check the expected type
                        // than `instanceof Uint8Array` on the actual value
                        match options.CustomRequestSerialization with
                        | _ when isByteArray typ ->
                            InternalUtilities.createBlobWithMimeType !^(InternalUtilities.toUInt8Array !!x) "application/octet-stream"
                        | Some msgPackSerializer ->
                            let data =
                                msgPackSerializer [| x |] [| flattenedFuncType.[i] |]
                                |> InternalUtilities.createUInt8Array
                            InternalUtilities.createBlobWithMimeType !^data "application/vnd.msgpack"
                        | None ->
                            let json = Convert.serialize x typ
                            InternalUtilities.createBlobWithMimeType !^json "application/json"
                    )
                    |> RequestBody.Multipart 
                else
                    match options.CustomRequestSerialization with
                    | Some msgPackSerializer ->
                        msgPackSerializer inputArguments flattenedFuncType
                        |> InternalUtilities.createUInt8Array
                        |> RequestBody.MsgPack
                    | _ ->
                        match inputArgumentTypes.Length with
                        | 1 when not (Convert.arrayLike inputArgumentTypes.[0]) ->
                            let typeInfo = TypeInfo.Tuple(fun _ -> inputArgumentTypes)
                            let requestBodyJson =
                                inputArguments
                                |> Array.tryHead
                                |> Option.map (fun arg -> Convert.serialize arg typeInfo)
                                |> Option.defaultValue "{}"
                            RequestBody.Json requestBodyJson
                        | 1 ->
                            // for array-like types, use an explicit array surrounding the input array argument
                            let requestBodyJson = Convert.serialize [| inputArguments.[0] |] (TypeInfo.Array (fun _ -> inputArgumentTypes.[0]))
                            RequestBody.Json requestBodyJson
                        | n ->
                            let typeInfo = TypeInfo.Tuple(fun _ -> inputArgumentTypes)
                            let requestBodyJson = Convert.serialize inputArguments typeInfo
                            RequestBody.Json requestBodyJson

            executeRequest requestBody
