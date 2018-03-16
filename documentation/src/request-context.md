# Accessing Request Context

Sometimes you might need to read data from the incoming request, Maybe to validate a request header or to do some kind of logging. We will demonstrate this with Suave, the same logic follows for the other web frameworks. We also want to be able to do this without sacrificing the shape the shared interface. 

The idea is to make the remoting handler *dependent* on the incoming request. 

Here is an example, we create a secure remoting handler by reading the authorization headers from the request and deciding what to return back to the client. 

```fs
type AuthError = 
   | UserTokenExpired
   | UserTokenInvalid
   | UserDoesNotHaveAccess

type IMusicStore = {
    // public function, no token required
    popularAlbums : unit -> Async<Result<Album list, AuthError>>
}

// the IMusicStore is DEPENDENT on the incoming request
// createBookApi : HttpRequest -> IMusicStore
let createMusicStore (req: HttpRequest) =
    // read the value of the authorization header
    let authorizationHeader = 
        req.headers
        |> List.tryFind (fun (key, _) -> key = "authorization")
        |> Option.map snd
    // construct the music store 
    let musicStore = { 
        popularAlbums = fun () -> async {
            match Security.validate authHeader with 
            | None -> return (Error UserTokenInvalid)
            | Some user ->
                let albums = Db.getPopularAlbums user
                return (Ok albums)
        }
    }

    musicStore
```
As you can see, constructing `IMusicStore` depends on an incoming `HttpRequest`, you read the authorization header and decide what to return to the logged in user. 

Now you can create a `WebPart` using the `request` constructor from Suave
```fs
let webApp = 
    choose [ 
        GET >=> path "/" >=> OK "Hello from root"
        request createMusicStore
    ]

startWebServer defaultConfig webApp
```