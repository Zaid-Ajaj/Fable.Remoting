# Fable.Remoting for .NET Clients

Although Fable.Remoting is initially implemented for communication between a .NET backend and a Fable frontend, the RPC story wouldn't be complete without a strongly typed dotnet client that can talk to the same backend using the protocol definition, that's why we have built one.

In fact, you can use the dotnet client with a dotnet server without a Fable project involved, think client-server interactions purely in F#. This has proven to make [integration testing](dotnet-integration-tests.md) extremely simple through this client.

## Installation
Install the library from [Nuget](https://www.nuget.org/packages/Fable.Remoting.DotnetClient/):
```bash
paket add Fable.Remoting.DotnetClient --project /path/to/App.fsproj
# or
dotnet add package Fable.Remoting.DotnetClient
```

## Using the library

### The new way

As you would expect, you need to reference the shared types and protocols in your client project:
```xml
<Compile Include="..\Shared\SharedTypes.fs" />
```
With the new `Remoting` API, the code is almost completely similar to the Fable client API, with only minor differences in proxy setup:
```fs
open Fable.Remoting.DotnetClient
open SharedTypes

let server =
    Remoting.createApi "http://backend.api.io/v1" // Also note the base URI is no longer optional.
    |> Remoting.buildProxy<IServer>

async {
    let! length = server.getLength "hello"
    return length
}
```

To make the proxy generation logic work, your protocol record must be immutable, i.e. not marked as `[<CLIMutable>]`.

### The old way using proxy quotations (still supported)

As you would expect, you need to reference the shared types and protocols in your client project:
```xml
<Compile Include="..\Shared\SharedTypes.fs" />
```
Now the code is similar to the Fable client API with a couple of differences:
```fs
open Fable.Remoting.DotnetClient
open SharedTypes

// specifies how the routes should be generated
let routes = sprintf "http://backend.api.io/v1/%s/%s"

// proxy: Proxy<IServer>
let proxy = Proxy.create<IServer> routes

async {
    // length : int
    let! length = proxy.call <@ fun server -> server.getLength "hello" @>
    // 5
    return length
}
```
The major difference is the use of quotations, which simplified the implementation process greatly and keeps the solution entirely type-safe without [fighting with the run-time](https://stackoverflow.com/questions/50131906/f-how-to-create-an-async-function-dynamically-based-on-return-type/50135445) with boxing/unboxing hacks to get types right.

### Proxy.callSafely
Alongside the `proxy.call` approach, there is also `proxy.callSafely` which is the same but will return `Async<Result<'t, Exception>>` to catch any exception that occurs along the request:
```fs
async {
    let! result = proxy.callSafely <@ fun server -> server.throwError() @>
    match result with
    | Ok value -> (* will not match *)
    | Error ex ->
        | match ex with
        | :? Http.ProxyRequestException as requestException ->
            let statusCode = requestException.StatusCode
            let response = requestException.Response
            let responseText = requestException.ResponseText
            (* do stuff with the exception information *)
        | otherException ->
            (* Usually network errors happen here *)
}
```