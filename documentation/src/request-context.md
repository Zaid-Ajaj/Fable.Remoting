# Accessing Request Context

Sometimes you might need to read data from the incoming request, Maybe to validate a request header or to do some kind of logging. We will demonstrate this with Suave, the same logic follows for the other web frameworks. We also want to be able to do this without sacrificing the shape the shared interface. 

The idea is to make the remoting handler *dependent* on the incoming request context. We do this by parameterizing the definition of the shared interface with a generic context type

```fs
type IMusicStore<'Context> = {
    /// return favorite albums of current logged in user
    favoriteAlbums : 'Context -> Async<Album list>
}
```
Notice the `'Context` generic parameter in the definition, this type is going to be of type `HttpContext`  from Suave when we provide an implementation but we don't hardcode that fact to make the definition applicable to the different web frameworks but also to make the interface callable from a proxy on client, more on this later.  

Now, here is an implementation of the interface:
```fs
let musicStore : IMusicStore<HttpContext> = {
    // return favorite albums of current logged-in user
    favoriteAlbums = fun ctx -> async {
        // get the authorization header, if any
        let authHeader = 
            ctx.request.headers
            |> List.tryFind (fun (key, _) -> key = "authorization")
            |> Option.map snd 

        match Security.extractUser authHeader with
        | None -> return [] // no albums 
        | Some user -> 
            let! favoriteAlbums = Database.favoriteAlbums user
            return favoriteAlbums
    }
}
```
Now that you an implementation, you can just create the `WebPart` like you would usually do in Suave:
```fs
let fableWebPart = remoting musicStore {()}

let webApp = 
    choose [ 
        GET >=> path "/" >=> OK "Hello from root"
        fableWebPart
    ]

startWebServer defaultConfig webApp
```
But now there is a little problem: How do you call the server from the client if the functions require a `'Context` as an input? Well you simply provide a `unit` as the generic parameter and everything just works out:
```fs
// Client

let musicStore = Proxy.remoting<IMusicStore<unit>> {()} 

async {
    let! favoriteAlbums = musicStore.favoriteAlbums() 
    for album in favoriteAlbums do
        printfn "%s" album.Title
}
|> Async.StartImmediate
```