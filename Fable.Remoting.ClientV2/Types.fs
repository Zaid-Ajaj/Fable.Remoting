namespace Fable.Remoting.Client 

open Fable.PowerPack.Fetch

type HttpMethod = GET | POST 

type HttpRequest = {
    HttpMethod: HttpMethod
    Url: string 
    Headers: (string * string) list  
    RequestBody : Option<string> 
}
 
type HttpResponse = {
    StatusCode: int 
    ResponseBody: string
}


type ErrorInfo = {
    path: string;
    methodName: string;
    error: string;
    response: Response
}

type RemoteBuilderOptions = {
    CustomHeaders : (string * string) list
    BaseUrl  : string option
    Authorization : string option
    RouteBuilder : (string -> string -> string)
}

type ProxyRequestException(response: HttpResponse, errorMsg, reponseText: string) = 
    inherit System.Exception(errorMsg)
    member this.Response = response 
    member this.StatusCode = response.StatusCode
    member this.ResponseText = reponseText 