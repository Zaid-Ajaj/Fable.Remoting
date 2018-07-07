namespace Fable.Remoting.DotnetClient 

open System.Net.Http
open System.Text
open Newtonsoft.Json.Linq
open Fable.Remoting.Json
open Newtonsoft.Json

[<RequireQualifiedAccess>]
module Http = 

    type Authorisation = 
        | Token of string
        | NoToken 

    type ProxyRequestException(response: HttpResponseMessage, errorMsg, reponseText: string) = 
        inherit System.Exception(errorMsg)
        let errorJson = JToken.Parse(reponseText) 
        let serializer = JsonSerializer()
        do serializer.Converters.Add(FableJsonConverter())
        member this.Response = response 
        member this.StatusCode = response.StatusCode
        member this.ResponseText = reponseText 
        /// Returns whether the error originating from the server was handled but got ignored
        member this.Ignored = errorJson.["ignored"].ToObject<bool>() 
        /// Returns whether the error originating from the server was handled by an error handler
        member this.Handled = errorJson.["handled"].ToObject<bool>()
        /// Parses the custom error as the specified type
        member this.ParseErrorAs<'t>() : 't = errorJson.["error"].ToObject(typeof<'t>, serializer) :?> 't 

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
            then return raise (new ProxyRequestException(response, sprintf "Internal server error (500) while making request to %s" url, responseText))
            elif response.StatusCode = System.Net.HttpStatusCode.Unauthorized 
            then return raise (new ProxyRequestException(response, sprintf "Unauthorized error from the server (401) while making request to %s" url, responseText))          
            elif response.StatusCode = System.Net.HttpStatusCode.Forbidden
            then return raise (new ProxyRequestException(response, sprintf "Forbidden error from the server (403) while making request to %s" url, responseText))
            else return raise (new ProxyRequestException(response, sprintf "Http error from server occured while making request to %s" url, responseText))
        }