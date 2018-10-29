namespace Fable.Remoting.Client 

open Fable.Import.Browser 

module Http = 

    let private defaultRequestConfig : HttpRequest = {
        HttpMethod = HttpMethod.GET 
        Url = "" 
        Headers = [ ]
        RequestBody = None
    }

    let get source = 
        { defaultRequestConfig 
            with Url = source
                 HttpMethod = HttpMethod.GET }
    
    let post source = 
        { defaultRequestConfig 
            with Url = source
                 HttpMethod = HttpMethod.POST }

    let request method url = 
        { defaultRequestConfig
            with Url = url
                 HttpMethod = method }
    let withHeaders headers (req: HttpRequest) = { req with Headers = headers  }
    let withBody body (req: HttpRequest) = { req with RequestBody = Some body }

    let send (req: HttpRequest) =
        Async.FromContinuations <| fun (resolve, _, _) -> 
            let xhr = XMLHttpRequest.Create()
            match req.HttpMethod with 
            | HttpMethod.GET -> xhr.``open``("GET", req.Url)
            | HttpMethod.POST -> xhr.``open``("POST", req.Url)
                
            // set the headers, must be after opening the request
            for (key, value) in req.Headers do 
                xhr.setRequestHeader(key, value)

            xhr.onreadystatechange <- fun _ ->
                match xhr.readyState with
                | 4.0 (* DONE *) -> resolve { StatusCode = unbox xhr.status; ResponseBody = xhr.responseText }
                | otherwise -> ignore() 
                    
            xhr.send(defaultArg req.RequestBody null)