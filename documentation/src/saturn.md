# Setup Saturn
On your Saturn project, you reference the the shared API types:
```xml
<Compile Include="../Shared/SharedModels.fs" />
<Compile Include="Program.fs" />
```
For Saturn, you actually don't need a seperate package other than the Giraffe package. Install [Fable.Remoting.Giraffe](https://www.nuget.org/packages/Fable.Remoting.Giraffe/):
```
paket add Fable.Remoting.Giraffe --project path/to/Server.fsproj
```
## Expose the API
You can now plug the API implementation into the `application` pipeline of Saturn
```fs
// Program.fs

open Saturn
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

let app = application {
    url "http://127.0.0.1:8083/"
    router webApp
}

run app
```