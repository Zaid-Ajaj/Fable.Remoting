# Getting Started

Before installing anything, you can start off by modeling an interface that represents your client-server interactions. The definition of this interface along with types used within it will be shared between the client and the server, later on you will reference this shared file from both projects. Such interface is represented in F# as a record with the fields of the record being functions. 

Suppose you are modelling an API for a music store, then it would look something like this:
 ```fsharp
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
    popularAlbums : Async<list<Album>> 
    allAlbums : Async<list<Album>> 
    albumById : int -> Async<Option<Album>>
    createAlbum : string -> string -> DateTime -> Async<Option<Album>>
}
```
As you can see, our interface is the `IMusicStore` record the fields of such record are functions of the shape:
```fs
Async<'A> 
'A -> Async<'B>
'A -> 'B -> Async<'C>
'A -> 'B -> 'C -> Async<'D>

// etc...
```
### Provide an implementation 
On the server, you would provide an implementation of the above API. 
```fsharp
let musicStore : IMusicStore = {
    popularAlbums = async {
        // getAllAlbums : unit -> Async<list<Album>>
        let! albums =  Database.getAllAlbums() 
        let popularAlbums = albums |> List.filter (fun album -> album.Popular) 
        return popularAlbums 
    }
    
    allAlbums = Database.getAllAlbums() 
   
    albumById = fun id -> async {
        // findAlbumById : int -> Async<Option<Album>>
        let! album = Database.findAlbumById id
        return album
    }

    createAlbum = fun title genre released -> async { (* you get the idea *) }
}
```
Now you are almost ready to expose the API to your client and have these functions being callable directly. Start by setting up the server with your web framework of choice: 

- [Setup Giraffe](#/server-setup/giraffe)
- [Setup Saturn](#/server-setup/saturn)
- [Setup Asp.Net Core](#/server-setup/aspnet-core)
- [Setup Suave](#/server-setup/suave)

Afterwards you can either:
- [Setup Fable client](#/client-setup/fable)
- [Setup dotnet client](#/client-setup/dotnet)