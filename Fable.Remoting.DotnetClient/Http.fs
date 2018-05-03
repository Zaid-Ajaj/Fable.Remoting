namespace Fable.Remoting.DotnetClient 

module Http = 

    open System.Net
    open System.IO

    type Authorisation = 
        | Token of string
        | NoToken 

    // From http://www.fssnip.net/7PK/title/Send-async-HTTP-POST-request
    let makePostRequest (url : string) (requestBody : string) auth = 
        let req = WebRequest.CreateHttp url
        req.CookieContainer <- new CookieContainer()
        req.Method <- "POST"
        req.ProtocolVersion <- HttpVersion.Version10
        let postBytes = System.Text.Encoding.UTF8.GetBytes(requestBody)
        req.ContentLength <- postBytes.LongLength
        req.ContentType <- "application/json; charset=utf-8"
        match auth with 
        | Token authToken -> req.Headers.["Authorization"] <- authToken 
        | NoToken -> () 

        async {
            use reqStream = req.GetRequestStream()
            do reqStream.Write(postBytes, 0, postBytes.Length)
            reqStream.Close()
            use! res = req.AsyncGetResponse() 
            use stream = res.GetResponseStream()
            use reader = new StreamReader(stream)
            let responseText = reader.ReadToEnd()       
            return responseText
        }