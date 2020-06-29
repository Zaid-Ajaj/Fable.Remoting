namespace Fable.Remoting.Client

open System
open Fable.Core
open Fable.SimpleJson
open Browser.Types
open Fable.Remoting

module internal Blob =
    /// Creates a Blob from the given input string
    [<Emit("new Blob([$0.buffer], { type: 'text/plain' })")>]
    let fromBinaryEncodedText (value: byte[]) : Blob = jsNative
    
    [<Emit("new FileReader()")>]
    let createFileReader() : FileReader = jsNative

    /// Asynchronously reads the blob data content as string
    let readBlobAsText (blob: Blob) : Async<string> =
        Async.FromContinuations <| fun (resolve, _, _) ->
            let reader = createFileReader()
            reader.onload <- fun _ ->
                if reader.readyState = FileReaderState.DONE
                then resolve (unbox reader.result)

            reader.readAsText(blob)

module Proxy =

    [<Emit("$2[$0] = $1")>]
    let setProp (propName: string) (propValue: obj) (any: obj) : unit = jsNative

    [<Emit("$0")>]
    let private typed<'a> (x: obj) : 'a = jsNative

    [<Emit("$0[$1]")>]
    let private getAs<'a> (x: obj) (key: string) : 'a = jsNative
    
    [<Emit("JSON.parse($0)")>]
    let private jsonParse (content: string) : obj = jsNative
    
    [<Emit("JSON.stringify($0)")>]
    let private stringify (x: obj) : string = jsNative
    
    let combineRouteWithBaseUrl route (baseUrl: string option) = 
        match baseUrl with
        | None -> route
        | Some url -> sprintf "%s%s" (url.TrimEnd('/')) route

    [<Emit("arguments")>]
    let arguments() : obj[] = jsNative

    /// Extracts the 'T from Async<'T>
    let extractAsyncArg (asyncType: Type) = 
        asyncType.GenericTypeArguments.[0]

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

    let proxyFetch options typeName (func: RecordField) fieldType =
        let funcArgs : (TypeInfo [ ]) = 
            match func.FieldType with  
            | TypeInfo.Async inner -> [| func.FieldType |]
            | TypeInfo.Promise inner -> [| func.FieldType |]
            | TypeInfo.Func getArgs -> getArgs() 
            | _ -> failwithf "Field %s does not have a valid definiton" func.FieldName

        let argumentCount = (Array.length funcArgs) - 1
        let returnTypeAsync = Array.last funcArgs
        let returnType = 
            match returnTypeAsync with 
            | TypeInfo.Async getAsyncTypeArgument -> getAsyncTypeArgument()
            | TypeInfo.Promise getPromiseTypeArgument -> getPromiseTypeArgument()
            | _ -> failwithf "Expected field %s to have a return type of Async<'t>" func.FieldName

        let readAsBinary = isAsyncOfByteArray returnTypeAsync 
        
        let binaryInput = 
            match func.FieldType with 
            | TypeInfo.Func getArgs -> 
                match getArgs() with 
                | [| input; output |] -> isByteArray input 
                | otherwise -> false 
            | otherwise -> false 
        
        let route = options.RouteBuilder typeName func.FieldName
        let url = combineRouteWithBaseUrl route options.BaseUrl 
        let funcNeedParameters = 
            match funcArgs with  
            | [| TypeInfo.Async _ |] -> false 
            | [| TypeInfo.Promise _ |] -> false
            | [| TypeInfo.Unit; TypeInfo.Async _ |] -> false 
            | otherwise -> true 

        fun arg0 arg1 arg2 arg3 arg4 arg5 arg6 arg7 ->

            let inputArguments =
               if funcNeedParameters  
               then List.take argumentCount [ box arg0;box arg1;box arg2;box arg3;box arg4;box arg5;box arg6;box arg7 ]
               else [ ]
                        
            let contentType = 
                if binaryInput 
                then "application/octet-stream"
                else "application/json; charset=utf8"

            async {
                let headers =
                  [ yield "Content-Type", contentType
                    yield "x-remoting-proxy", "true"
                    yield! options.CustomHeaders
                    match options.Authorization with 
                    | Some authToken -> yield "Authorization", authToken
                    | None -> () ]

                let requestBody = 
                    if binaryInput 
                    then RequestBody.Binary (unbox arg0)
                    else RequestBody.Json (Json.stringify inputArguments)

                if options.IsBinary then
                    // read as arraybuffer and deserialize
                    let! (response, statusCode) = 
                        if funcNeedParameters 
                        then 
                            Http.post url
                            |> Http.withBody requestBody
                            |> Http.withHeaders headers 
                            |> Http.sendAndReadBinary
                        else 
                            Http.get url 
                            |> Http.withHeaders headers  
                            |> Http.sendAndReadBinary
                    
                    match statusCode with 
                    | 200 ->
                        let rec getReturnType typ =
                            let _, res = Reflection.FSharpType.GetFunctionElements typ

                            if Reflection.FSharpType.IsFunction res then
                                getReturnType res
                            else
                                res.GetGenericArguments () |> Array.head
                        
                        return MsgPack.Read.Reader(response).Read (getReturnType fieldType)
                    | 500 ->
                        let responseAsBlob = Blob.fromBinaryEncodedText response
                        let! responseText = Blob.readBlobAsText responseAsBlob
                        let response = { StatusCode = statusCode; ResponseBody = responseText }
                        return! raise (ProxyRequestException(response, sprintf "Internal server error (500) while making request to %s" url, response.ResponseBody)) 
                    | n ->
                        let responseAsBlob = Blob.fromBinaryEncodedText response
                        let! responseText = Blob.readBlobAsText responseAsBlob
                        let response = { StatusCode = statusCode; ResponseBody = responseText }
                        return! raise (ProxyRequestException(response, sprintf "Http error (%d) while making request to %s" n url, response.ResponseBody))
                else
                    match readAsBinary with 
                    | true -> 
                        // don't deserialize, read as arraybuffer and convert to byte[]
                        let! (response, statusCode) = 
                            if funcNeedParameters 
                            then 
                                Http.post url
                                |> Http.withBody requestBody
                                |> Http.withHeaders headers 
                                |> Http.sendAndReadBinary
                            else 
                                Http.get url 
                                |> Http.withHeaders headers  
                                |> Http.sendAndReadBinary
                        
                        match statusCode with 
                        | 200 -> 
                            return unbox response 
                        | 500 ->
                            let responseAsBlob = Blob.fromBinaryEncodedText response
                            let! responseText = Blob.readBlobAsText responseAsBlob
                            let response = { StatusCode = statusCode; ResponseBody = responseText }
                            return! raise (ProxyRequestException(response, sprintf "Internal server error (500) while making request to %s" url, response.ResponseBody)) 
                        | n ->
                            let responseAsBlob = Blob.fromBinaryEncodedText response
                            let! responseText = Blob.readBlobAsText responseAsBlob
                            let response = { StatusCode = statusCode; ResponseBody = responseText }
                            return! raise (ProxyRequestException(response, sprintf "Http error (%d) while making request to %s" n url, response.ResponseBody)) 
                    | false ->
                        // make plain RPC request and let it go through the deserialization pipeline
                        let! response = 
                            if funcNeedParameters 
                            then 
                                Http.post url
                                |> Http.withBody requestBody
                                |> Http.withHeaders headers 
                                |> Http.send 
                            else  
                                Http.get url 
                                |> Http.withHeaders headers  
                                |> Http.send 

                        match response.StatusCode with  
                        | 200 -> 
                            let parsedJson = SimpleJson.parseNative response.ResponseBody
                            return Convert.fromJsonAs parsedJson returnType  
                        | 500 -> return! raise (ProxyRequestException(response, sprintf "Internal server error (500) while making request to %s" url, response.ResponseBody)) 
                        | n ->   return! raise (ProxyRequestException(response, sprintf "Http error (%d) from server occured while making request to %s" n url, response.ResponseBody)) 
            }
