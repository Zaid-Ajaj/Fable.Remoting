# Getting Started
Before installing anything, you can start off by modeling an interface that represents your client-server interactions. The definition of this interface will be shared between the client and the server. Such interface is represented in F# as a record with the fields of the record being functions. 

## Model your API
Suppose you are modelling an API for a music store, then it would look something like this:
 ```fs
// SharedModels.fs
module ShareModels

open System 

type Album = {
    Id : int
    Title : string
    Genre : string
    Released : DateTime
}

// The shared interface representing your client-server interaction
type IMusicStore = {
     allAlbums : unit -> Async<list<Album>> 
     albumById : int -> Async<Option<Album>>
     createAlbum : string -> string -> DateTime -> Async<Option<Album>>
 }
```
As you can see, our interface is the `IMusicStore` record the fields of such record are functions of the shape:
```
'A -> Async<'B>
'A -> 'B -> Async<'C>
```
## Provide an implementation 
On the server, you would provide an implementation of the above API. 
```fs
let musicStore : IMusicStore = {
    // Db.getAllAlbums : unit -> Async<list<Album>>
    allAlbums = fun () -> Db.getAllAlbums() 
    albumById = fun id -> async { (* implement here *) }
    createAlbum = fun title genre released -> async { (* you get the idea *) }
}
```
Now you are almost ready to expose the API to your client and have these functions being callable directly. Start by setting up the server with your web framework of choice: 

- [Setup Suave](suave.md)
- Setup Giraffe: TODO
- Setup Saturn: TODO

Afterwards you can [setup Fable Client](client.md) 