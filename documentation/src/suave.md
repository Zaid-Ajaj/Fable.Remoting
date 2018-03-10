# Setup Suave
On your client Fable project, you reference the the shared API types:
```xml
<Compile Include="../Shared/SharedModels.fs" />
<Compile Include="Program.fs" />
```
Now you need to install the Suave-specific package: [Fable.Remoting.Suave](https://www.nuget.org/packages/Fable.Remoting.Suave/):
```
paket add Fable.Remoting.Suave --project path/to/Server.fsproj
```
# Expose the API as a WebPart
Now that you have installed the Remoting package, you can create a `WebPart` and run it as part of your Suave web server:
```fs
// Program.fs

open Suave
open Suave.Filters
open Suave.Succesfull
open SharedModels
open Fable.Remoting.Server
open Fable.Remoting.Suave

let musicStore : IMusicStore = {
    (* Your implementation here *)
} 

// create the web part using the remoting CE 
let fableWebApp : WebPart = remoting musicStore {()} 

let webApp = 
  choose [ fableWebApp
           GET >=> path "/" 
               >=> OK "<h1>Index</h1>" ]

startWebServer defaultConfig webApp 
```
That's it! As simple as that. You can now setup your [Fable client](client.md) 