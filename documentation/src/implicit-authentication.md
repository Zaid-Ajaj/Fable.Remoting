# Implicit Authentication 

Using `Fable.Remoting` only as part of your web application is a key feature of the library (and the reason why we call it a library instead of a framework). It should be easy to integrate it with an exisiting web application too, especially when it comes to authentication and authorization, you most likely already have a mechanism to secure your application endpoints. 

### Scenario of an existing application
For example you might have `requiresAuthenticated` WebPart/HttpHandler that you put before your secure endpoints like the following:

```fs
let webApp = 
    choose [ 
        GET >=> path "/" >=> OK "Public api"
        (* Other parts *)
        POST >=> path "/secure"
             >=> requiresAuthenticated
             >=> OK "User Logged in"  
    ]
```
Where we are assuming that `requiresAuthenticated` checks whether or not a user is logged in by checking the Authorization header of the incoming request. Everytime the client sends a secure request, the client also provides a valid authentication header, so far so good. 

Ideally, we want to integrate the remoting handler as follows:
```fs
let fableWebPart : WebPart = remoting musicStore {()}

let webApp = 
    choose [ 
        GET >=> path "/" >=> OK "Public api"
        (* Other parts *)
        POST >=> path "/secure"
             >=> requiresAuthenticated
             >=> OK "User Logged in"  

        requiresAuthenticated >=> fableWebPart
    ]
```
This means, that requests coming from the client proxy cannot reach the `fableWebPart` unless there is a valid `Authorization` header coming from the client as well, so the question becomes: how to do you send the authorization header from the client? Well, out of the box, we support this scenario by letting provide the `Authorization` header value when creating the proxy: 
```fs
// Client 

// value of authorization header
let authorizationToken = "Bearer <rest of token value>"

let musicStore = Proxy.remoting<IMusicStore> {
    use_auth_token authorizationToken
}
```
Now you can use `musicStore` like you would usually do. 

### Cookie Authentication 
You might be asking: what if the `requiresAuthenticated` validates the *cookie* in the request instead of authorization header to check whether or not a user logged in? Then there is nothing special to do on the client side of things because the `Cookie` header is automatically included in client requests with the value `document.cookie` which holds whatever cookie(s) that was cached when the user logged in. 
