namespace Fable.Remoting.Client

open System
open FSharp.Reflection
open Fable.PowerPack
open Fable.Core
open Fable.Core.JsInterop
open Fable.PowerPack.Fetch

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
    
    /// Turns a function type ('a -> 'b) into a RecordFuncType that is easier to work with when parsing parameters from JSON and when doing the matching of routes to record functions
    let makeRecordFuncType (propType: Type) =
        let flattenedTypes = List.ofArray (typed<Type[]> (FSharpType.GetFunctionElements propType)) 
        match flattenedTypes with  
        | [ simpleAsyncValue ] -> RecordFunctionType.NoArguments simpleAsyncValue
        | [ input; output ] -> RecordFunctionType.SingleArgument (input, output) 
        | manyArgumentsWithOutput ->  
            let lastInputArgIndex = List.length manyArgumentsWithOutput - 1
            match List.splitAt lastInputArgIndex manyArgumentsWithOutput with 
            | inputArguments, [ output ] -> RecordFunctionType.ManyArguments (inputArguments, output) 
            | _ -> failwith "makeRecordFuncType: Should not happen"

    [<PassGenerics>]
    let recordFieldsAsFunctions<'t>() : RecordFuncInfo list =
        FSharpType.GetRecordFields typeof<'t>
        |> Array.choose (fun propInfo ->
            if FSharpType.IsFunction propInfo.PropertyType
            then Some { Name = propInfo.Name; Type = makeRecordFuncType propInfo.PropertyType }
            elif box (propInfo.PropertyType?definition?name) = box (typeof<Async<_>>?definition?name)
            then Some { Name = propInfo.Name; Type = NoArguments propInfo.PropertyType } 
            else None)
        |> List.ofArray

    /// Returns the output type of the record function
    let getOutputType = function 
        | NoArguments outputType -> outputType
        | SingleArgument (_, outputType) -> outputType
        | ManyArguments (_, outputType) -> outputType

    /// Returns the number of arguments for the record function
    let getArgumentCount = function
        | NoArguments _ -> 0
        | SingleArgument _ -> 1
        | ManyArguments (inputTypes, _) -> List.length inputTypes

    /// Returns whether or not a function needs paramters. If not, we will use GET request instead of POST when communicating with the server 
    let functionNeedsParameters = function 
        | NoArguments _ -> false
        | SingleArgument (input, _) -> input <> typeof<unit>
        | ManyArguments _ -> true 

    let combineRouteWithBaseUrl route (baseUrl: string option) = 
        match baseUrl with
        | None -> route
        | Some url -> sprintf "%s%s" (url.TrimEnd('/')) route

    /// Extracts the 'T from Async<'T>
    let extractAsyncArg (asyncType: Type) = 
        asyncType.GenericTypeArguments.[0]

    let proxyFetch options typeName (func: RecordFuncInfo) =
        let argumentCount = getArgumentCount func.Type
        let returnTypeAsync = getOutputType func.Type 
        let returnType = extractAsyncArg returnTypeAsync 
        let route = options.RouteBuilder typeName func.Name
        let url = combineRouteWithBaseUrl route options.BaseUrl 

        fun arg0 arg1 arg2 arg3 arg4 arg5 arg6 arg7 arg8 arg9 arg10 arg11 arg12 arg13 arg14 arg15 ->
            
            let inputArguments =
               if functionNeedsParameters func.Type  
               then List.take argumentCount [ box arg0;box arg1;box arg2;box arg3;box arg4;box arg5;box arg6;box arg7;box arg8;box arg9;box arg10;box arg11;box arg12;box arg13;box arg14;box arg15 ]
               else [ ]
                        
            let proxyRequest = 
              promise {
                
                let defaultHeaders = 
                    [  ContentType "application/json; charset=utf8"
                       Cookie Fable.Import.Browser.document.cookie ]

                let headers =
                  [ yield! defaultHeaders
                    yield! options.CustomHeaders

                    match options.AuthorizationResolve, options.Authorization with 
                    | Some resolveFun, _ -> yield Authorization (resolveFun())
                    | None, Some authToken -> yield Authorization authToken 
                    | _ -> () ]

                // Send RPC request to the server
                let requestProps = [
                    if functionNeedsParameters func.Type then 
                        yield Body (unbox (toJson inputArguments))
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
                | 200 -> return ofJsonAsType responseText returnType 
                | 500 -> return! raise (ProxyRequestException(response, sprintf "Internal server error (500) while making request to %s" url, responseText)) 
                | _ ->   return! raise (ProxyRequestException(response, sprintf "Http error from server occured while making request to %s" url, responseText)) }
            
            Async.AwaitPromise proxyRequest