# Setting Asp.NET Core Middleware

On your Asp.NET Core project, reference the the shared API types:
```xml
<Compile Include="../Shared/SharedModels.fs" />
<Compile Include="Program.fs" />
```
Now you need to install the Suave-specific package: [Fable.Remoting.AspNetCore](https://www.nuget.org/packages/Fable.Remoting.AspNetCore/):
```
paket add Fable.Remoting.AspNetCore --project path/to/Server.fsproj
```
## Expose the API as Asp.NET Core middleware:

Besides `Fable.Remoting.AspNetCore`, this snippet is using the following packages to run the server: 
- `Microsoft.AspNetCore.Hosting`
- `Microsoft.AspNetCore.Server.Kestrel` 

See this [pure kestrel sample](https://github.com/Zaid-Ajaj/remoting-pure-kestrel) for reference

```fs
// Program.fs
open SharedModels
open Fable.Remoting.Server
open Fable.Remoting.AspNetCore
// server stuff
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting

let musicStore : IMusicStore = {
    (* Your implementation here *)
} 

// Create API from musicStore value
let webApp = 
    Remoting.createApi()
    |> Remoting.fromValue musicStore

// Create an API from different value
let otherApp = 
    Remoting.createApi()
    |> Remoting.fromValue otherValue

let configureApp (app : IApplicationBuilder) =
    // Add the API to the ASP.NET Core pipeline
    app.UseRemoting(webApp)
    // you can have multiple API's 
    app.UseRemoting(otherApp) 

[<EntryPoint>]
let main _ =
    WebHostBuilder()
        .UseKestrel()
        .Configure(Action<IApplicationBuilder> configureApp)
        .Build()
        .Run()
    0
```
## Explicit Signature

Sometimes, the F# compiler can't infer the type of your created remoting api from how it is used, for example, the snippet:
```fs
let webApp = 
    Remoting.createApi()
    |> Remoting.fromValue musicStore
```
Will give you an error if used on it's own before using it inside the `configureApp` function because it is inferred to be of type `RemotingOptions<'t, IMusicStore>` where we actually want it to be of type `RemotingOptions<HttpContext, IMusicStore>`. The workaround is to simply write the signature explicitly like this:
```fs
let webApp : RemotingOptions<HttpContext, IMusicStore> = 
    Remoting.createApi()
    |> Remoting.fromValue musicStore
```