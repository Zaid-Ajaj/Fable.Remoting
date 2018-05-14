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
With `ErrorResult` you choose either to propagate a custom message back to the client or just ignore the error. You don't want the exception data (message or stacktrace) to be returned to the client. Either way, an exception will be thrown on the call-site from the client with a generic error message. If you chose to propagate a custom error message, it will be passed off to a global handler on the client, here is a full example on the server:
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
let webApp = remoting musicStore {
    use_error_handler errorHandler
}
```
On the client, you can intercept the propagated custom error messages, also using a global handler:
```fs
// Assuming the type CustomError is shared with the client too

let errorHandler (info: ErrorInfo) = 
    let customError = ofJson<CustomError> errorInfo.error
    printfn "Oh noo: %s" custromError.errorMsg)

let musicStore = Proxy.remoting<IMusicStore> {
    use_error_handler errorHandler
}
```