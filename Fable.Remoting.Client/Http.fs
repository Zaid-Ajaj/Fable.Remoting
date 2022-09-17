namespace Fable.Remoting.Client

open System.Threading
open Browser
open Browser.Types

module Http =

    /// Constructs default values for HttpRequest
    let private defaultRequestConfig : HttpRequest = {
        HttpMethod = GET
        Url = "/"
        Headers = [ ]
        WithCredentials = false
        RequestBody = Empty
    }

    /// Creates a GET request to the specified url
    let get (url: string) : HttpRequest =
        { defaultRequestConfig
            with Url = url
                 HttpMethod = GET }

    /// Creates a POST request to the specified url
    let post (url: string) : HttpRequest =
        { defaultRequestConfig
            with Url = url
                 HttpMethod = POST }

    /// Creates a request using the given method and url
    let request method url =
        { defaultRequestConfig
            with Url = url
                 HttpMethod = method }

    /// Appends a request with headers as key-value pairs
    let withHeaders headers (req: HttpRequest) = { req with Headers = headers  }

    /// Sets the withCredentials option on the XHR request, useful for CORS requests
    let withCredentials withCredentials (req: HttpRequest) =
        { req with WithCredentials = withCredentials }

    /// Appends a request with string body content
    let withBody body (req: HttpRequest) = { req with RequestBody = body }

    let private sendAndRead (preparation:(XMLHttpRequest -> unit) option) resultMapper (req: HttpRequest) = async {
        let! token = Async.CancellationToken
        let request = Async.FromContinuations <| fun (resolve, _, cancel) ->
            let xhr = XMLHttpRequest.Create()

            match req.HttpMethod with
            | GET -> xhr.``open``("GET", req.Url)
            | POST -> xhr.``open``("POST", req.Url)

            match preparation with
            | Some f ->  f xhr
            | _ -> ignore()

            token.Register(fun _ ->
                xhr.abort()
                cancel(System.OperationCanceledException(token))
            ) |> ignore

            // set the headers, must be after opening the request
            for (key, value) in req.Headers do
                xhr.setRequestHeader(key, value)

            xhr.withCredentials <- req.WithCredentials

            xhr.onreadystatechange <- fun _ ->
                match xhr.readyState with
                | ReadyState.Done when not token.IsCancellationRequested ->
                    xhr |> resultMapper |> resolve
                | _ -> ignore()

            match req.RequestBody with
            | Empty -> xhr.send()
            | RequestBody.Json content -> xhr.send(content)
            | Binary content -> xhr.send(InternalUtilities.toUInt8Array content)

        return! request
    }

    /// Sends the request to the server and asynchronously returns a response
    let send = sendAndRead None (fun xhr  -> { StatusCode = unbox xhr.status; ResponseBody = xhr.responseText })

    /// Sends the request to the server and asynchronously returns the response as byte array
    let sendAndReadBinary =
        sendAndRead
            (Some (fun xhr -> xhr.responseType <- "arraybuffer" )) // read response as byte array
            (fun xhr ->
                let bytes = InternalUtilities.createUInt8Array xhr.response
                (bytes, xhr.status))

