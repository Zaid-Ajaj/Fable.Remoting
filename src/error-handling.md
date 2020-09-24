# Error Handling

You might ask: What happens when an exception is thrown on the server by one of the RPC methods? 

Fable.Remoting provides fine-grained way of dealing with errors. Unhandled exceptions are catched on the server and are passed off to the exception handler on the server of the type 
```fs
Exception -> RouteInfo<HttpContext> -> ErrorResult
``` 
where `ErrorResult` is defined as:
```fs
type ErrorResult = 
    | Ignore
    | Propagate of obj
```
With `ErrorResult` you choose either to propagate a custom message back to the client or just ignore the error. You don't want the exception data (message or stacktrace) to be returned to the client. When an error object is propagated, the exception of type `ProxyRequestException` (see below) will contain the error object serialized to JSON in the `ResponseText` field
```fs
open System

// Custom error will be propagated back to client
type CustomError = { errorMsg: string }

let errorHandler (ex: Exception) (routeInfo: RouteInfo<HttpContext>) = 
    // do some logging
    printfn "Error at %s on method %s" routeInfo.path routeInfo.methodName
    // decide whether or not you want to propagate the error to the client
    match ex with
    | :? System.IO.IOException as x ->
        let customError = { errorMsg = "Something terrible happened" }
        Propagate customError
    | :? System.Exception as x ->
        // ignore error
        Ignore
```
Use the error handler as follows:
```fs
let webApp = 
    Remoting.createApi()
    |> Remoting.withErrorHandler errorHandler
    |> Remoting.fromValue musicStore   
```
On the client, you can intercept both propagated custom error messages or ignored ones. Either way, an exception is thrown on call-site: if the exception is ignored (or unhandled when there isn't an error handler) you will get a generic error message along with other information. If an error is propagated, it is serialized:
```fs
// Assuming the type CustomError is shared with the client too
let musicStore = 
    Remoting.createApi()
    |> Remoting.buildproxy<IMusicStore>

async {
    let! result = Async.Catch (musicStore.throwError()) 
    match result with 
    | Choice1Of2 output -> (* won't happen *)
    | Choice2Of2 ex ->
        match ex with  
        | :? ProxyRequestException as ex -> 
            let response : HttpResponse = ex.Response 
            let responseText : string = ex.ResponseText
            let statusCode : int = ex.StatusCode 
            (* do stuff with error information*) 
        
        | otherException -> (* do other stuff *)    
}
```
The `ProxyRequestException` is special, it has all information about the response:
```fs
type ProxyRequestException(response: HttpResponse, errorMsg, reponseText: string) = 
    inherit System.Exception(errorMsg)
    member this.Response = response 
    member this.StatusCode = response.StatusCode
    member this.ResponseText = reponseText 
```
When an error is unhandled by the application (i.e. there was no error handler on the server) the `ResponseText` gives a generic error message to the client:
```json
{ 
    "error": "Error occured while running the function 'throwError'", 
    "ignored": true, 
    "handled": false 
}  
```
When there is an error handler but the exception got ignored:
```json
{ 
    "error": "Error occured while running the function 'throwError'", 
    "ignored": true, 
    "handled": true
}     
```
Finally when a custom error like the `CustomError` shown above gets propagated, the result becomes:
```json
{ 
    "error":  {
        "errorMsg": "Something terrible happened"
    },
    "ignored": false, 
    "handled": true 
}  
```
Parsing the response text if needed becomes the responsibility of the consuming application 
