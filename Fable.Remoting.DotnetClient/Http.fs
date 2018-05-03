namespace Fable.Remoting.DotnetClient 

module Http = 

    open System.Net
    open System.IO

    // From http://www.fssnip.net/7PK/title/Send-async-HTTP-POST-request
    let makePostRequest (url : string) (requestBody : string) = 
        let req = WebRequest.CreateHttp url
        req.CookieContainer <- new CookieContainer()
        req.Method <- "POST"
        req.ProtocolVersion <- HttpVersion.Version10
        let postBytes = System.Text.Encoding.UTF8.GetBytes(requestBody)
        req.ContentLength <- postBytes.LongLength
        req.ContentType <- "application/json; charset=utf-8"
        async {
            use! reqStream = req.GetRequestStreamAsync() |> Async.AwaitTask
            do! reqStream.WriteAsync(postBytes, 0, postBytes.Length) |> Async.AwaitIAsyncResult |> Async.Ignore
            reqStream.Close()
            use! res = req.AsyncGetResponse() 
            use stream = res.GetResponseStream()
            use reader = new StreamReader(stream)
            let! rdata = reader.ReadToEndAsync() |> Async.AwaitTask        
            return rdata
        }