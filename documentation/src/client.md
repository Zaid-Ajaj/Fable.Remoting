# Client

On your client Fable project, you reference the the shared API types:
```xml
<Compile Include="../Shared/SharedModels.fs" />
<Compile Include="Program.fs" />
```
Now you need to install the client package: [Fable.Remoting.Client](https://www.nuget.org/packages/Fable.Remoting.Client/):
```
paket add Fable.Remoting.Client --project path/to/Client.fsproj
```
## Proxy creation
Now that you have installed the package, you can use it to create a proxy: An object with which you are able to call the server in a type-safe manner: 
```fs
open ShareModels
open Fable.Remoting.Client

// musicStore : IMusicStore
let musicStore = IProxy.remoting<IMusicStore> {()}

async {
    let! albums = musicStore.allAlbums() 
    for album in albums do
        printfn "%s (%s)" album.Title album.Genre
}
|> Async.StartImmediate
```
