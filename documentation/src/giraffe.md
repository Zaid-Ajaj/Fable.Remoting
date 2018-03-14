# Setup Giraffe
On your Giraffe project, you reference the the shared API types:
```xml
<Compile Include="../Shared/SharedModels.fs" />
<Compile Include="Program.fs" />
```
Now you need to install the Suave-specific package: [Fable.Remoting.Giraffe](https://www.nuget.org/packages/Fable.Remoting.Giraffe/):
```
paket add Fable.Remoting.Giraffe --project path/to/Server.fsproj
```
## Expose the API as a HttpHandler:
```fs
// Program.fs

open Giraffe
open SharedModels
open Fable.Remoting.Server
open Fable.Remoting.Giraffe

let musicStore : IMusicStore = {
    (* Your implementation here *)
} 

// create the HttpHandler using the remoting CE 
let fableWebApp = remoting musicStore {()} 

let configureApp (app : IApplicationBuilder) =
    // Add Giraffe to the ASP.NET Core pipeline
    app.UseGiraffe fableWebApp

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