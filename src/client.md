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
let musicStore : IMusicStore = 
  Remoting.createApi()
  |> Remoting.buildProxy<IMusicStore>() 

async {
    let! albums = musicStore.allAlbums() 
    for album in albums do
        printfn "%s (%s)" album.Title album.Genre
}
|> Async.StartImmediate
```
## Webpack dev server configuration
When you are working with `webpack-dev-server` in developement mode, you want to re-route the HTTP requests from your developement server to ypur actual backend, for that you must use the following configuration for webpack, assuming you are running `webpack-dev-server` on port 8080 and your backend is running on port 8083. You would change this block:

```js
devServer: {
  contentBase: resolve('./public'),
  port: 8080
}
```
to this:
```js
devServer: {
  contentBase: resolve('./public'),
  port: 8080,
  // tell webpack-dev-server to re-route all requests 
  // from dev-server to the actual server
  proxy: {
    '/*': { 
      // assuming the suave server is running on port 8083
      target: "http://localhost:8083",
      changeOrigin: true
    }
}
```