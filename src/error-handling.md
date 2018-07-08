# Error Handling

You might ask: What happens when an exception is thrown on the server by one of the RPC methods? 

Fable.Remoting provides fine-grained way of dealing with errors. Unhandled exceptions are catched on the server and are passed off to a global exception handler of the type 
```fs
Exception -> RouteInfo<HttpContext> -> ErrorResult
``` 
where `ErrorResult` is defined as:
```fs
type ErrorResult = 
    | Ignore
    | Propagate of obj
```
With `ErrorResult` you choose either to propagate a custom message back to the client or just ignore the error. You don't want the exception data (message or stacktrace) to be returned to the client. Either way, an exception will be thrown on the call-site from the client with a generic error message. If you chose to propagate a custom error message, it will be passed off to a global handler on the client, here is a full example:
```fs
open System

// Custom error will be propagated back to client
type CustomError = { errorMsg: string }

let errorHandler (ex: Exception) (routeInfo: RouteInfo<HttpContext>) = 
    // do some logging
    printfn "Error at %s on method %s" routeInfo.path routeInfo.methodName
    // decide whether or not you want to propagate the error to the client
    match ex with
    | :? System.IOException as x ->
        // propagate custom error, this is intercepted by the client
        let customError = { errorMsg = "Something terrible happend" }
        Propagate customError
    | :? System.Exception as x ->
        // ignore error
        Ignore
```
Use the error handler using the `remoting` builder:
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
    |> Remoting.buildproxy<IMusicStore>()

async {
    try 
      let! result = musicStore.throwError() 
    with 
     | :? ProxyRequestException as ex -> (* do stuff *) 
     | otherException -> (* do stuff *) 
}
```
The `ProxyRequestException` is special, it has all information about the response:
```fs
type ProxyRequestException(response: Response, errorMsg, reponseText: string) = 
    inherit System.Exception(errorMsg)
    member this.Response = response 
    member this.StatusCode = response.Status
    member this.ResponseText = reponseText 
```
When an error is unhanlded (i.e. there was no error handler on the server) the `ResponseText` becomes:
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
        "errorMsg": "Something terrible happend"
    },
    "ignored": true, 
    "handled": true 
}  
```
Parsing the response text if needed becomes the responsibility of the consuming application 