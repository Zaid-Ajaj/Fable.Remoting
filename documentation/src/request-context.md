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
            match Map.tryFind "Special-Header" headers with 
            | Some "Special-Value" -> return db.getAwesomeAlbums()
            | None -> return db.getBoringAlbums()
        }
    }

    musicStore
```
So far, as you can see, the code doesn't do anything with the `HttpContext` and it is unit-testable. Now we need to actually read the headers from the request and pass them to the factory, we do this by using the `context` handler from Suave that gives us access to the context of the incoming request:
```fs
let createWebApp (db: IMusicDb) : WebPart = context <| fun ctx ->
    // Here is where we access the context to extract the headers
    let headers = Map.ofList ctx.request.headers
    // construct the music store 
    let musicStore = createMusicStore db headers 
    // expose the music store as a WebPart
    remoting musicStore {()} 
```
Finally to actually build the final `WebPart`, you will need to provide the actual `IMusicDb` database implementation:
```fs
let musicDb : IMusicDb = { 
    new IMusicDb with 
       member self.getAwesomeAlbums() = (* ... *) 
       member self.getBoringAlbums() = (* ... *) 
}

// webApp : WebPart
let webApp = createWebApp musicDb

// start the Suave server
startWebServer defaulConfig webApp
```
That is it, we have now exposed our `musicStore` implementation to the world as an Http web service. 
We can now also write some unit tests for the implementation:
```fs
testCase "Boring albums are returned when there is no special header" <| fun () ->
    let musicDbMock : IMusicDb = {
        new IMusicDb with 
          member self.getAwesomeAlbums() = [ { Id = 1; Name = "Metallica" } ]
          member self.getBoringAlbums() = [ ] 
    }  

    let headers = Map.ofList [ "Content-Length", "70" ]
    let musicStore = createMusicStore musicDbMock headers
    
    musicStore.bestAlbums
    |> Async.RunSynchronously 
    |> List.length 
    |> fun n -> Expect.equal 0 n "List should be empty" 
```