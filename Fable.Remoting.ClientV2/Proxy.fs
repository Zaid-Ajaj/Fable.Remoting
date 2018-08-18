namespace Fable.Remoting.Client

open System
open FSharp.Reflection
open Fable.PowerPack
open Fable.Core
open Fable.Core.JsInterop
open Fable.PowerPack.Fetch
open Fable.SimpleJson
open Fable.Import.Browser

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
        | Some url -> 
            if url.EndsWith("/")
            then sprintf "%s%s" url route
            else sprintf "%s/%s" url route

    [<Emit("arguments")>]
    let arguments() : obj[] = jsNative

    /// Extracts the 'T from Async<'T>
    let extractAsyncArg (asyncType: Type) = 
        asyncType.GenericTypeArguments.[0]

    let proxyFetch options typeName (func: RecordField) =
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

        let route = options.RouteBuilder typeName func.FieldName
        let url = combineRouteWithBaseUrl route options.BaseUrl 
        let funcNeedParameters = 
            match funcArgs with  
            | [| TypeInfo.Async _ |] -> false 
            | [| TypeInfo.Promise _ |] -> false
            | [| TypeInfo.Unit; TypeInfo.Async _ |] -> false 
            | otherwise -> true 

        fun arg0 arg1 arg2 arg3 arg4 arg5 arg6 arg7 ->
            
            console.log(func.FieldName, "has argument count", argumentCount)

            let inputArguments =
               if funcNeedParameters  
               then List.take argumentCount [ box arg0;box arg1;box arg2;box arg3;box arg4;box arg5;box arg6;box arg7 ]
               else [ ]
                        
            let proxyRequest = 
              promise {
                
                let defaultHeaders = 
                    [  ContentType "application/json; charset=utf8"
                       Cookie Fable.Import.Browser.document.cookie ]

                let headers =
                  [ match options.Authorization with 
                    | Some authToken -> 
                      yield Authorization authToken 
                      yield! defaultHeaders
                      yield! options.CustomHeaders
                    | None -> 
                      yield! defaultHeaders
                      yield! options.CustomHeaders  ] 

                // Send RPC request to the server
                let requestProps = [
                    if funcNeedParameters then 
                        yield Body (unbox (Json.stringify inputArguments))
                        yield Method HttpMethod.POST
                    else 
                        yield Method HttpMethod.GET
                    yield Credentials RequestCredentials.Sameorigin
                    yield requestHeaders headers
                ]

                let makeReqProps props = keyValueList CaseRules.LowerFirst props :?> RequestInit
                    
                // use GlobalFetch.fetch to control error handling
                let! response = GlobalFetch.fetch(RequestInfo.Url url, makeReqProps requestProps)
                //let! response = Fetch.fetch url requestProps
                let! responseText = response.text()

                match response.Status with 
                | 200 -> 
                    let parsedJson = SimpleJson.parseNative responseText
                    return Convert.fromJsonAs parsedJson returnType 
                | 500 -> return! raise (ProxyRequestException(response, sprintf "Internal server error (500) while making request to %s" url, responseText)) 
                | _ ->   return! raise (ProxyRequestException(response, sprintf "Http error from server occured while making request to %s" url, responseText)) }
            
            Async.AwaitPromise proxyRequest