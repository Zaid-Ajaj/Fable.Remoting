# Refreshing Access Tokens 

In the previous section on [Implicit Authentication](implicit-authentication.md), I have shown you how to include an authorization header for requests made by the proxy to reach a secured API end point. The problem with this approach is that the access token you use in the authorization header is *constant*: every request made will use that same token for authorization. 

This behavior might not always be preferrable because these tokens are short-lived and expire after a couple of hours, in which case the user might have to logout and login again to obtain a fresh token. Another common use-case is when you are using a third-party authorization server, you will need to ask the authorization server first to obtain new access tokens.

Dependending on your type of application, you might want to use a *refresh token*: a token that is like the usual access token but this one is typically long-lived and it is used to *obtain* a newly generated access token every once in a while without the user having to logout after a certain time span of inactivity. 

Refresh tokens complicate the scenario, typically because the developer has to persist such tokens on the client side of things (will not be a concern for the remoting library), but also because secure requests have to obtain a new access token using this refresh token in order to be able to make secure requests to the server.

> Of course, depending on your requirements, you might want to decide when exactly you want to refresh an access token, in this example we will aquire a fresh access token on every request.

Assuming you have implemented two functions, let us call them `refreshTokenFromStorage` and `obtainAccessToken`, you will use them to get an access token from the authorization server. The functions might have the following signature:
```fs
type AccessToken = AccessToken of string 
type RefreshToken = RefreshToken of string 

// get the refresh token stored somewhere on the client
let refreshTokenFromStorage : unit -> Async<RefreshToken> = (* ... *)

// use the refresh token to obtain an access token from the authorization server
let obtainAccessToken : RefreshToken -> Async<AccessToken> = (* ... *)
```
You will create a new proxy every time you want to make a secure request using the access token:
```fs
// Create a MusicStore proxy from an access token
let createSecureMusicStore (AccessToken(accessToken)) : IMusicStore = 
    Remoting.createApi()
    |> Remoting.withAuthorizationHeader (sprintf "Bearer %s" accessToken)
    |> Remoting.buildProxy<IMusicStore>


// Create a new proxy every time you need to use API
let musicStore (f: IMusicStore -> Async<'t>) : Async<'t> = 
    async {
        let! refreshToken = refreshTokenFromStorage()
        let! accessToken = obtainAccessToken refreshToken
        let musicStoreApi =  createSecureMusicStore accessToken
        return! f musicStoreApi
    }
```
Now from the application code consuming the `IMusicStore` it will look like this:
```fs
let getPopularAlbums() = 
    async {
        let! albums = musicStore (fun api -> api.getPopularAlbums())
        return albums
    }
```
In case you don't like callbacks, here is another way of writing the `musicStore` function:
```fs
let getMusicStore() : Async<IMusicStore> = 
    async {
        let! refreshToken = refreshTokenFromStorage()
        let! accessToken = obtainAccessToken refreshToken
        let musicStoreApi =  createSecureMusicStore accessToken
        return musicStoreApi
    }

let getPopularAlbums() = 
  async {
      let! musicStore = getMusicStore()
      let! albums = musicStore.getPopularAlbums()
      return albums
  }  
``` 
Same idea, similar results and still a simple API. So at the end of the day, the added complexity is simply an extra argument or an extra function call to use the proxy instead of using a static instance. Finally, don't worry about performance regarding the creation of proxies because it is a very cheap operation that only occurs every once in a while.
