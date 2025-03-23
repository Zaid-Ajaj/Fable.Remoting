namespace Fable.Remoting.Client 

open System
open Fable.Remoting.ClientServer

type HttpMethod = GET | POST 

type RequestBody = 
    | Empty
    | Json of string 
    | Binary of byte[] 

type CustomResponseSerializer = byte[] -> Type -> obj

type HttpRequest = {
    HttpMethod: HttpMethod
    Url: string 
    Headers: (string * string) list  
    RequestBody : RequestBody
    WithCredentials : bool
}
 
type HttpResponse = {
    StatusCode: int 
    ResponseBody: string
}

type RemoteBuilderOptions = {
    CustomHeaders : (string * string) list
    BaseUrl  : string option
    Authorization : string option
    WithCredentials : bool
    RouteBuilder : (string -> string -> string)
    CustomResponseSerialization : CustomResponseSerializer option
}

type ProxyRequestException(response: HttpResponse, errorMsg, reponseText: string) = 
    inherit System.Exception(errorMsg)
    member this.Response = response 
    member this.StatusCode = response.StatusCode
    member this.ResponseText = reponseText 
    /// Tries to parse the ResponseText into a typed error result.
    member this.ParseCustomErrorResult<'userError>() : CustomErrorResult<'userError> option =
        try
            Fable.Core.JS.JSON.parse this.ResponseText |> unbox |> Some
        with _ -> None
