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
```fs
// Program.fs

open SharedModels
open Fable.Remoting.Server
open Fable.Remoting.AspNetCore

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