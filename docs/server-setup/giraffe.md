# Setup Giraffe
On your Giraffe project, you reference the shared API types:
```xml
<Compile Include="../Shared/SharedModels.fs" />
<Compile Include="Program.fs" />
```
Now you need to install the Giraffe-specific package: [Fable.Remoting.Giraffe](https://www.nuget.org/packages/Fable.Remoting.Giraffe/):
```
dotnet add package Fable.Remoting.Giraffe
```
### Expose the API as a HttpHandler:
```fsharp
// Program.fs

open Giraffe
open SharedModels
open Fable.Remoting.Server
open Fable.Remoting.Giraffe

let musicStore : IMusicStore = {
    (* Your implementation here *)
} 

// create the HttpHandler from the musicStore value
let webApp : HttpHandler = 
    Remoting.createApi()
    |> Remoting.fromValue musicStore
    |> Remoting.buildHttpHandler

let configureApp (app : IApplicationBuilder) =
    // Add Giraffe to the ASP.NET Core pipeline
    app.UseGiraffe webApp

let configureServices (services : IServiceCollection) =
    // Add Giraffe dependencies
    services.AddGiraffe() |> ignore

[<EntryPoint>]
let main _ =
    WebHostBuilder()
        .UseKestrel()
        .Configure(Action<IApplicationBuilder> configureApp)
        .ConfigureServices(configureServices)
        .Build()
        .Run()
    0
```
