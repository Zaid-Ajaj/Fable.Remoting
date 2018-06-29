# Accessing Request Context

Sometimes you might need to read data from the incoming request, Maybe to validate a request header or to do some kind of logging. In This section, we will demonstrate the techniques of building an implementation from protocol specs that has both per-request and static dependencies where the following holds:
 
 - No change of the protocol specification is required
 - The implementation of the protocol specification is fully unit-testable without involving the Http pipeline 

For the following example, assume that we will be needing the values of the headers as key-value pair (i.e. of type `Map<string, string>`) this will our per-request dependency, as a static dependency we will be needing a database that our implementation can call to get the actual album data.

```fs
type IMusicDb = 
    abstract getAwesomeAlbums : unit -> Album list 
    abstract getBoringAlbums : unit -> Album list 

// shared protocol specification
type IMusicStore = {
    bestAlbums : Async<Album list>
}

// factory function that creates protocol implementation, 
// it depeneds on incoming headers from request and on a static database implementation 
let createMusicStore (db: IMusicDb) (headers: Map<string, string>) : IMusicStore = 
    let musicStore = {
        bestAlbums = async {
            // assuming lower case header keys
            match Map.tryFind "special-header" headers with 
            | Some "Special-Value" -> return db.getAwesomeAlbums()
            | _ -> return db.getBoringAlbums()
        }
    }

    musicStore
```
So far, as you can see, the code doesn't do anything with the `HttpContext` and it is unit-testable. Infact, this implementation doesn't care in what type of server it will be hosted in. 

Now we need to read the headers from the request and pass them to the factory, we do this by using the `context` handler that gives us access to the context of the incoming request, in Suave this `context` combinator is built-in so that you can use it as follows: 
```fs
let createWebApp (db: IMusicDb) : WebPart = context <| fun ctx ->
    // Here is where we access the context to extract the headers
    let headers = Map.ofList ctx.request.headers
    // construct the music store 
    let musicStore = createMusicStore db headers 
    // expose the music store as a WebPart
    remoting musicStore { use_route_builder (sprintf "/api/%s/%s") } 
```
In Giraffe/Saturn, the `context` combinator isn't built-in but it is easy to define it:
```fs
let context (f: HttpContext -> HttpHandler) : HttpHandler =
    fun (next: HttpFunc) (ctx : HttpContext) -> 
        task {
            let handler = f ctx
            return! handler next ctx 
        }

let createWebApp (db: IMusicDb) : HttpHandler = context <| fun ctx ->
    // Here is where we access the context to extract the headers
    let headers = 
        [ for pair in ctx.Request.Headers do 
            let key = pair.Key.ToLower() 
            let value = pair.Value.[0] 
            yield key, value ]
        |> Map.ofList 
    
    // construct the music store 
    let musicStore = createMusicStore db headers 

    // expose the music store as a HttpHandler
    remoting musicStore {()}
``` 
Finally, in order to build the `WebPart` or `HttpHandler`, you will need to provide database implementation for `IMusicDb` :
```fs
let musicDb : IMusicDb = { 
    new IMusicDb with 
       member self.getAwesomeAlbums() = (* ... *) 
       member self.getBoringAlbums() = (* ... *) 
}

// webApp : WebPart / HttpHandler 
let webApp = createWebApp musicDb
```
That is it, we have now exposed our `musicStore` implementation to the world as an Http web service. 
We can now also write some unit tests for the implementation:
```fs
testCaseAsync "Boring albums are returned when there is no special header" <| async {
    let musicDbMock : IMusicDb = {
        new IMusicDb with 
          member self.getAwesomeAlbums() = [ { Id = 1; Name = "Metallica" } ]
          member self.getBoringAlbums() = [ ] 
    }  

    let headers = Map.ofList [ "content-length", "70" ]
    let musicStore = createMusicStore musicDbMock headers
    
    let! albums = musicStore.bestAlbums
    
    albums
    |> List.length 
    |> fun n -> Expect.equal 0 n "List should be empty" 
}
```
### Take it for a spin
First of all, implement an `IMusicDb` like this:
```fs
let musicDb : IMusicDb = { 
    new IMusicDb with 
       member self.getAwesomeAlbums() = [ { Id = 1; Name = "Awesome album" } ]
       member self.getBoringAlbums() = [ { Id = 1; Name = "Boring album" } ] 
}
```
Now send a request that includes the special header with special value. The request body is just `[]` because `bestAlbums` doesn't have any parameters:

![img](imgs/with-special-header.png)

No special header in the request? You will get the boring albums:

![img](imgs/with-special-header.png)
