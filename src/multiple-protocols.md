# Using Multiple Procotols

It is possible to use multiple protocols for communication with remoting, this is because the result of the `Remoting` module is either a `WebPart` in case of Suave or a `HttpHandler` in case of Giraffe or Saturn. In all cases, you can compose multiple resuling API's the same way you do with normal `WebPart`s or `HttpHandler`s using the `choose` combinator:

### Suave
```fs
let firstApi = 
    Remoting.createApi()
    |> Remoting.fromValue firstProtocol
    |> Remoting.buildWebpart 

let secondApi = 
    Remoting.createApi()
    |> Remoting.fromValue secondProtocol
    |> Remoting.buildWebpart 

let combinedApi = choose [firstApi; secondApi]
```
### Giraffe or Saturn 
```fs
let firstApi = 
    Remoting.createApi()
    |> Remoting.fromValue firstProtocol
    |> Remoting.buildHttpHandler 

let secondApi = 
    Remoting.createApi()
    |> Remoting.fromValue secondProtocol
    |> Remoting.buildHttpHandler 

let combinedApi = choose [firstApi; secondApi]
```