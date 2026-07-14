# Setup Falco
On your Falco project, you reference the shared API types:
```xml
<Compile Include="../Shared/SharedModels.fs" />
<Compile Include="Program.fs" />
```
Now you need to install the Falco-specific package: [Fable.Remoting.Falco](https://www.nuget.org/packages/Fable.Remoting.Falco/):
```
dotnet add package Fable.Remoting.Falco
```
### Expose the API
You can now plug the API implementation into the Falco endpoint routing. Other apps and endpoints can be combined with Seq.concat before passing to `UseFalco`
```fs
open Falco
open Falco.Routing
open Fable.Remoting.Server
open Fable.Remoting.Falco
open Microsoft.AspNetCore.Builder
open SharedModels

let musicStore : IMusicStore = {
    (* Your implementation here *)
}

let webApp : HttpHandler =
    Remoting.createApi()
    |> Remoting.fromValue musicStore
    |> Remoting.buildHttpEndpoints

let wapp = WebApplication.Create()

wapp.UseRouting()
  .UseFalco(webApp)
  .Run(Response.ofPlainText "Not found")
```
