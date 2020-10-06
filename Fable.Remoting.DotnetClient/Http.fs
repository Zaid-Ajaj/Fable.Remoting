namespace Fable.Remoting.DotnetClient 

open System.Net.Http
open System.Text

[<RequireQualifiedAccess>]
module Http = 

    type ProxyRequestException(response: HttpResponseMessage, errorMsg, reponseText: string) = 
        inherit System.Exception(errorMsg)
        member __.Response = response 
        member __.StatusCode = response.StatusCode
        member __.ResponseText = reponseText 

    let makePostRequest (client: HttpClient) (url : string) (requestBody : string) : Async<string> = 
        let contentType = "application/json"

        async {
            use postContent = new StringContent(requestBody, Encoding.UTF8, contentType)
            let! response = Async.AwaitTask(client.PostAsync(url, postContent))
            let! responseText = Async.AwaitTask(response.Content.ReadAsStringAsync())
            if response.IsSuccessStatusCode 
            then return responseText
            elif response.StatusCode = System.Net.HttpStatusCode.InternalServerError 
            then return raise ( ProxyRequestException(response, sprintf "Internal server error (500) while making request to %s" url, responseText))
            elif response.StatusCode = System.Net.HttpStatusCode.Unauthorized 
            then return raise ( ProxyRequestException(response, sprintf "Unauthorized error from the server (401) while making request to %s" url, responseText))          
            elif response.StatusCode = System.Net.HttpStatusCode.Forbidden
            then return raise ( ProxyRequestException(response, sprintf "Forbidden error from the server (403) while making request to %s" url, responseText))
            else return raise ( ProxyRequestException(response, sprintf "Http error from server occured while making request to %s" url, responseText))
        }

    let makePostRequestBinaryResponse (client: HttpClient) (url : string) (requestBody : string) : Async<byte[]> = 
        let contentType = "application/json"

        async {
            use postContent = new StringContent(requestBody, Encoding.UTF8, contentType)
            let! response = Async.AwaitTask(client.PostAsync(url, postContent))
            let! responseData = Async.AwaitTask(response.Content.ReadAsByteArrayAsync())
            if response.IsSuccessStatusCode 
            then return responseData
            elif response.StatusCode = System.Net.HttpStatusCode.InternalServerError 
            then return raise ( ProxyRequestException(response, sprintf "Internal server error (500) while making request to %s" url, "<BINARY DATA>"))
            elif response.StatusCode = System.Net.HttpStatusCode.Unauthorized 
            then return raise ( ProxyRequestException(response, sprintf "Unauthorized error from the server (401) while making request to %s" url, "<BINARY DATA>"))          
            elif response.StatusCode = System.Net.HttpStatusCode.Forbidden
            then return raise ( ProxyRequestException(response, sprintf "Forbidden error from the server (403) while making request to %s" url, "<BINARY DATA>"))
            else return raise ( ProxyRequestException(response, sprintf "Http error from server occured while making request to %s" url, "<BINARY DATA>"))
        }