namespace Fable.Remoting.DotnetClient 

open System.Net.Http
open System.Text

[<RequireQualifiedAccess>]
module Http = 


    type Authorisation = 
        | Token of string
        | NoToken 

    type UnauthorisedException(content) = 
        inherit System.Exception(content)

    type InternalServerErrorException(content) = 
        inherit System.Exception(content)

    type ForbiddenException(content) = 
        inherit System.Exception(content)

    type NotOkException(response: HttpResponseMessage, content) = 
        inherit System.Exception(content)
        member this.Response = response
        
    let makePostRequest (client: HttpClient) (url : string) (requestBody : string) auth : Async<string> = 
        let contentType = "application/json"
        match auth with 
        | Token authToken -> 
            // Add it to client
            client.DefaultRequestHeaders.Add("Authorization", authToken)
        | NoToken -> () 

        async {
            use postContent = new StringContent(requestBody, Encoding.UTF8, contentType)
            let! response = Async.AwaitTask(client.PostAsync(url, postContent))
            let! responseText = Async.AwaitTask(response.Content.ReadAsStringAsync())
            if response.IsSuccessStatusCode 
            then return responseText
            elif response.StatusCode = System.Net.HttpStatusCode.InternalServerError 
            then return raise (new InternalServerErrorException(sprintf "Internal server error (500) while making request to %s" url))
            elif response.StatusCode = System.Net.HttpStatusCode.Unauthorized 
            then return raise (new UnauthorisedException(sprintf "Unauthorized error from the server (401) while making request to %s" url))          
            elif response.StatusCode = System.Net.HttpStatusCode.Forbidden
            then return raise (new ForbiddenException(sprintf "Forbidden error from the server (403) while making request to %s" url))
            else return raise (new NotOkException(response, sprintf "Http error from server with unknown code white making request to %s" url))
        }