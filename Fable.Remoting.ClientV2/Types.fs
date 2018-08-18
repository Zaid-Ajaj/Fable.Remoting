namespace Fable.Remoting.Client 

open Fable.PowerPack.Fetch

type ErrorInfo = {
    path: string;
    methodName: string;
    error: string;
    response: Response
}

type RemoteBuilderOptions = {
    CustomHeaders : HttpRequestHeaders list
    BaseUrl  : string option
    Authorization : string option
    RouteBuilder : (string -> string -> string)
}

type ProxyRequestException(response: Response, errorMsg, reponseText: string) = 
    inherit System.Exception(errorMsg)
    member this.Response = response 
    member this.StatusCode = response.Status
    member this.ResponseText = reponseText 