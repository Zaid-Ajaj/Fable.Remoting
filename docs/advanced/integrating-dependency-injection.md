# Integrating Dependency Injection

So far when we have been creating a HTTP service from a protocol implementation, we used the function `Remoting.fromValue`
```fsharp {highlight: [7]}
let musicStore : IMusicStore = {
    (* Your implementation here *)
}

let webApp = 
    Remoting.createApi()
    |> Remoting.fromValue musicStore
```
Now this `fromValue` function expects an intance of the protocol. This instance however, could have dependencies. For example, it is possible that in order to create an instance of the `IMusicStore`, you need to know connnection string to a database which you can use in the implementation of the instance
```fsharp
let musicStore (connectionString: string): IMusicStore = {
    (* Your implementation here *)
}
```
Here we say that the parameter _connectionString_ is a dependency of _musicStore_ which is a fancier way of saying that _musicStore_ is parameterized. Now to create an instance of `IMusicStore`, we would need the connection string and then we can feed this into `Remoting.fromValue`
```fsharp {highlight: [11]}
open System

let musicStore (connectionString: string): IMusicStore = {
    (* Your implementation here *)
}

let connectionString = Environment.GetEnvironmentVariable "DATABASE"

let webApp = 
    Remoting.createApi()
    |> Remoting.fromValue (musicStore connectionString)
```
Here we get the connection string from the environment variables and pass it to _musicStore_ which creates our protocol instance. Then the result of the function is passed to `Remoting.fromValue` that eventually creates the HTTP service from that protocol.

In a more realistic scenario, your protocol would have many more dependencies such as a logger (e.g. `ILogger<'T>`), maybe an object that lets you retrieve configuration (i.e. `IConfiguration`) or some service like `IHttpClientFactory`. 

```fsharp
let musicStore (logger: ILogger<IMusicStore>, config: IConfiguration) = {
    (* Your implementation here *)
}
```

These dependencies cannot be easily acquired for the `musicStore` function because these are available only from the _request HTTP context_.

> NOTE: this applies to all server-side components except for Suave since it doesn't have built-in dependency injection

### Creating the protocol from the HTTP context

The dependencies mentioned above are called [_framework provided services_](https://docs.microsoft.com/en-us/aspnet/core/fundamentals/dependency-injection?view=aspnetcore-6.0#framework-provided-services) from ASP.NET Core. Meaning that these can be obtained when you have access to the request context at a bare-minimum. Accessing the request context in Fable.Remoting is possible through the function `Remoting.fromContext` which gives you the HTTP request context on a per-request basis. You can use it follows:

```fsharp {highlight: [7]}
let musicStore (logger: ILogger<IMusicStore>, config: IConfiguration) = {
    (* Your implementation here *)
}

let webApi = 
  Remoting.createApi()
  |> Remoting.fromContext (fun ctx -> 
      let logger = (* obtain logger *) 
      let config = (* obtain config *) 
      // create a music store from the dependencies
      musicStore(logger, config)
    )
```
The final part here is that we use the context to require the dependencies and here is where the function `GetService<'T>` comes into play
```fsharp {highlight:[9, 10]}
let musicStore (logger: ILogger<IMusicStore>, config: IConfiguration) = {
    (* Your implementation here *)
}

let webApi = 
    Remoting.createApi()
    |> Remoting.fromContext (fun ctx -> 
        // require dependencies 
        let logger = ctx.GetService<ILogger<IMusicStore>>()
        let config = ctx.GetService<IConfiguration>()
        // create a music store from the dependencies
        musicStore(logger, config)
    )
```

The function `GetService<'T>` requires the service of type `'T` from the built-in dpendency injection framework of ASP.NET Core. We then use the services to contruct the protocol implementation and create the final HTTP service from it. 