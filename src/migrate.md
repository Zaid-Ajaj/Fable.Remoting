# Migrations

## Moving from 2.x to 3.x 
Along with the infrastructure rewrite of the version 3.x, the public API also had some breaking changes, mainly the use of module function chaining intead of CE-based API. Migrating to the new API should be straight-forward, here follows a couple of snippets for how existing code from 2.x should look like in 3.x: 

`Suave`: Before
```fs
let musicStore : IMusicStore = (* implementation *) 

// webApp : WebPart
let webApp = remoting musicStore {
    use_route_builder routeBuilder 
    use_error_handler errorHandler
    use_logger (printfn "%A")
}
```
`Suave`: After
```fs
let musicStore : IMusicStore = (* implementation *) 

let webApp = 
    Remoting.createApi()
    |> Remoting.fromValue musicStore
    |> Remoting.withRouteBuilder routeBuilder
    |> Remoting.withErrorHandler errorHandler 
    |> Remoting.withDiagnosticsLogger (printfn "%A")
    |> Remoting.buildWebPart 
```
`Giraffe` Before
```fs
let musicStore : IMusicStore = (* implementation *) 

// webApp : WebPart
let webApp = remoting musicStore {
    use_route_builder routeBuilder 
    use_error_handler errorHandler
    use_logger (printfn "%A")
}
```
`Giraffe` After
```fs
let musicStore : IMusicStore = (* implementation *) 

let webApp = 
    Remoting.createApi()
    |> Remoting.fromValue musicStore
    |> Remoting.withRouteBuilder routeBuilder
    |> Remoting.withErrorHandler errorHandler 
    |> Remoting.withDiagnosticsLogger (printfn "%A")
    |> Remoting.buildHttpHandler 
```
## No more contexual handlers 
Using generic record types `type IMusicStore<'t> { }` as means to access the request context is no longer possible, the http context will be provided from *outside* the the implementation, see [Accessing Request Context](request-context.md). 