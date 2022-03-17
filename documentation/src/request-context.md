# Accessing Request Context

Sometimes you might need to read data from the incoming request. Maybe to validate a request header or to do some kind of logging. In this section, we will demonstrate the techniques of building an implementation from protocol specs that has both per-request and static dependencies where the following holds:
 
 - No change of the protocol specification is required
 - The implementation of the protocol specification is fully unit-testable without involving the Http pipeline 

For the following example, assume that we will be needing the values of the headers as key-value pairs (i.e. of type `Map<string, string>`), this will be our per-request dependency, as a static dependency we will be needing a database that our implementation can call to get the actual album data.

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
So far, as you can see, the code doesn't do anything with the `HttpContext` and it is unit-testable. Infact, this implementation doesn't care in what type of server it will be hosted in. Now we need a `HttpContext` to read the headers from:

```fs
let musicStore (db: IMusicDb) (context: HttpContext) : IMusicStore  = 
    // Here is where we access the context to extract the headers
    let headers = Map.ofList context.request.headers
    // construct the music store 
    createMusicStore db headers 
```
As you can see, the `musicStore` function takes in a dependency of `IMusicDb` and `HttpContext`. You will have to provide the `IMusicDb` yourself (for now) and you will end up with a function of type `HttpContext -> IMusicStore`. This signature is exactly what `Remoting.fromContext` is expecting and can be used like this:
```fs
// first, create your musicDb
let musicDb : IMusicDb = { 
    new IMusicDb with 
       member self.getAwesomeAlbums() = (* ... *) 
       member self.getBoringAlbums() = (* ... *) 
}

// now build the WebPart
let webApp : WebPart = 
    Remoting.createApi()
    |> Remoting.withRouteBuilder (sprintf "/api/%s/%s)")
    |> Remoting.fromContext (musicStore musicDb) 
    |> Remoting.buildWebPart

startWebServer defaultConfig webApp 
```
In Giraffe/Saturn, the story is the same
```fs
let musicStore (db: IMusicDb) (ctx: HttpContext) : IMusicStore =  
    // Here is where we access the context to extract the headers
    let headers = 
        [ for pair in ctx.Request.Headers do 
            let key = pair.Key.ToLower() 
            let value = pair.Value.[0] 
            yield key, value ]
        |> Map.ofList 
    
    // construct the music store 
    createMusicStore db headers 

let webApp : HttpHandler = 
    Remoting.createApi()
    |> Remoting.withRouteBuilder (sprintf "/api/%s/%s)")
    |> Remoting.fromContext (musicStore musicDb) 
    |> Remoting.buildHttpHandler 
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

![img](imgs/without-special-header.png)
