# Full Authentication Example

This page contains full example of Fable app using Fable.Remoting with Giraffe on backed with 2 APIs - one anonymous for login, one secured by JWT Bearer token returning currently logged user.

Before we move to the full authentication example, there are few things to mention:

* Exceptions are used for errors, but with well-defined union type inside
* Remoting uses custom error handler to wrap such errors and set 4xx status code for HTTP response
* API definition does not use `Result` type directly
* Registration neither token refresh is not part of this example, but can be easily added
* Database for users and its functions are not implemented, only used "to implement" functions
* Authentication is hard - don't do it manually. Use some existing service like Auth0, Azure AD, Identity Server or so. 

## Setup errors in shared project

First of all we need Fable and server to understand what kind of errors can happen in whole solution and add a few functions:

Create `Errors.fs` in your `MyAwesomeApp.Shared` project.

```fs
type ServerError =
    | Exception of string
    | Authentication of string
    // add here any other you want to use

exception ServerException of ServerError

module ServerError =
    let failwith (er:ServerError) = raise (ServerException er)

    let ofOption err (v:Option<_>) =
        v
        |> Option.defaultWith (fun _ -> err |> failwith)
```

Now we need to define API that both parts (frontend & backend) can understand:

## Setup API contract in shared project

Create `API.fs` in your `MyAwesomeApp.Shared` project.

```fs
[<RequireQualifiedAccess>]
module Request =
    type Login = {
        Email : string
        Password : string
    }

[<RequireQualifiedAccess>]
module Response =
    type JwtToken = {
        Token : string
    }
    
    type UserInfo = {
        Name : string
        Email : string
    }

type AnonymousAPI = {
    Login : Request.Login -> Async<Response.JwtToken> // note no Result here!
}
with
    static member RouteBuilder _ m = sprintf "/api/anonymous/%s" m

type SecuredAPI = {
    GetUserInfo : unit -> Async<Response.UserInfo> // note no Result here!
}
with
    static member RouteBuilder _ m = sprintf "/api/secured/%s" m

```

Great! We have a contract! Good job! Let's fulfil it from the Giraffe backend.

## Implement API contract on backend

Let's start with a tiny extension to `Fable.Remoting` which will handle our defined errors and set correct HTTP code.

Create `Remoting.fs` file

```fs
namespace Fable.Remoting.Server

open System
open Fable.Remoting.Server
open Microsoft.AspNetCore.Http
open MyAwesomeApp.Shared.Errors

[<RequireQualifiedAccess>]
module Remoting =
    let private statusCode = function
        | Exception _ -> 500
        | Authentication _ -> 401
        | _ -> 400

    let rec errorHandler (ex: Exception) (routeInfo: RouteInfo<HttpContext>) =
        match ex with
        | ServerException err ->
            routeInfo.httpContext.Response.StatusCode <- err |> statusCode
            Propagate err
        | e when e.InnerException |> isNull |> not -> errorHandler e.InnerException routeInfo
        | _ -> Propagate (ServerError.Exception(ex.Message))
```

To generate JWT tokens we need few simple fuctions. Create `JWT.fs` and copy paste this code:

```fs
open System
open System.IdentityModel.Tokens.Jwt
open Microsoft.IdentityModel.Tokens

type Token = {
    Token : string
    ExpiresOn : DateTimeOffset
}

type JwtConfiguration = {
    Audience : string
    Issuer : string
    Secret : string
    AccessTokenLifetime : TimeSpan
}

let private isBeforeValid (before:Nullable<DateTime>) =
    if before.HasValue && before.Value > DateTime.UtcNow then false else true

let private isExpirationValid (expires:Nullable<DateTime>) =
    if expires.HasValue && DateTime.UtcNow > expires.Value then false else true

let private getKey (secret:string) = SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(secret))

let createToken (config:JwtConfiguration) claims =
    let credentials = SigningCredentials(getKey config.Secret, SecurityAlgorithms.HmacSha256)
    let issuedOn = DateTimeOffset.UtcNow
    let expiresOn = issuedOn.Add(config.AccessTokenLifetime)
    let jwtToken = JwtSecurityToken(config.Issuer, config.Audience, claims, (issuedOn.UtcDateTime |> Nullable), (expiresOn.UtcDateTime |> Nullable), credentials)
    let handler = JwtSecurityTokenHandler()
    { Token = handler.WriteToken(jwtToken); ExpiresOn = expiresOn }

let getParameters (config:JwtConfiguration) =
    let validationParams = TokenValidationParameters()
    validationParams.RequireExpirationTime <- true
    validationParams.ValidAudience <- config.Audience
    validationParams.ValidIssuer <- config.Issuer
    validationParams.ValidateLifetime <- true
    validationParams.LifetimeValidator <- (fun before expires _ _  -> isBeforeValid before && isExpirationValid expires)
    validationParams.ValidateIssuerSigningKey <- true
    validationParams.IssuerSigningKey <- config.Secret |> getKey
    validationParams

let validateToken (validationParams:TokenValidationParameters) (token:string) =
    try
        let handler = JwtSecurityTokenHandler()
        let principal = handler.ValidateToken(token, validationParams, ref null)
        principal.Claims |> Some
    with _ -> None
```

And one more library for password hashes. Create `Password.fs`:

```fs
open System
open System.Runtime.CompilerServices
open System.Security.Cryptography
open Microsoft.AspNetCore.Cryptography.KeyDerivation

[<Literal>]
let private saltSize = 16

[<Literal>]
let private numBytesRequested = 32
[<Literal>]
let private iterationCount = 10000

[<MethodImpl(MethodImplOptions.NoInlining ||| MethodImplOptions.NoOptimization)>]
let private bytesAreEqual (a:byte []) (b:byte []) =
    if a = null && b = null then
        true
    elif a = null || b = null || a.Length <> b.Length then
        false
    else
        let mutable areEqual = true
        for i = 0 to a.Length - 1 do
            areEqual <- areEqual && (a.[i] = b.[i])
        areEqual

let private writeNetworkByteOrder (buffer:byte []) (offset:int) (value:uint32) =
    buffer.[(offset + 0)] <- (value >>> 24) |> byte
    buffer.[(offset + 1)] <- (value >>> 16) |> byte
    buffer.[(offset + 2)] <- (value >>> 8) |> byte
    buffer.[(offset + 3)] <- (value >>> 0) |> byte
    ()

let private readNetworkByteOrder (buffer:byte []) offset =
    (buffer.[offset + 0] |> uint32 <<< 24)
    ||| (buffer.[offset + 1] |> uint32 <<< 16)
    ||| (buffer.[offset + 2] |> uint32 <<< 8)
    ||| (buffer.[offset + 3] |> uint32)

let createHash password =
    let rng = RandomNumberGenerator.Create()
    let salt = Array.zeroCreate<Byte> saltSize
    rng.GetBytes(salt)
    let subKey = KeyDerivation.Pbkdf2(password, salt, KeyDerivationPrf.HMACSHA256, iterationCount, numBytesRequested)

    let outputBytes = Array.zeroCreate<Byte> (13 + salt.Length + subKey.Length)
    outputBytes.[0] <- 0x01 |> byte
    writeNetworkByteOrder outputBytes 1 (KeyDerivationPrf.HMACSHA256 |> uint32)
    writeNetworkByteOrder outputBytes 5 (iterationCount |> uint32)
    writeNetworkByteOrder outputBytes 9 (saltSize |> uint32)
    Buffer.BlockCopy(salt, 0, outputBytes, 13, salt.Length)
    Buffer.BlockCopy(subKey, 0, outputBytes, 13 + salt.Length, subKey.Length)

    outputBytes
    |> Convert.ToBase64String

let verifyPassword hash password =
    let hashedPassword = hash |> Convert.FromBase64String
    let prf : KeyDerivationPrf = readNetworkByteOrder hashedPassword 1 |> int |> enum
    let iterCount = readNetworkByteOrder hashedPassword 5 |> int
    let saltLength = readNetworkByteOrder hashedPassword 9 |> int

    let currentSalt = Array.zeroCreate<Byte> saltLength
    Buffer.BlockCopy(hashedPassword, 13, currentSalt, 0, currentSalt.Length)

    let subKeyLength = hashedPassword.Length - 13 - currentSalt.Length
    let expectedSubKey = Array.zeroCreate<Byte> subKeyLength
    Buffer.BlockCopy(hashedPassword, 13 + currentSalt.Length, expectedSubKey, 0, expectedSubKey.Length)
    let actualSubKey = KeyDerivation.Pbkdf2(password, currentSalt, prf, iterCount, subKeyLength)
    bytesAreEqual actualSubKey expectedSubKey
```


Now we can add the first API. Create `Anonymous.fs`

```fs
type UserFromDb = {
    Id : Guid
    Name : string
    Email : string
    PwdHash : string
}

let private getUserByEmail (email:string) : UserFromDb option = failwith "TODO"

let private userToToken (cfg:JwtConfiguration) (user:UserFromDb) : Token =
    [ Claim("id", user.Id.ToString()) ] 
    |> List.toSeq
    |> JWT.createToken cfg

let private tokenToResponse (t:Token) : Response.JwtToken =
    { Token = t.Token }

let private login (cfg:JwtConfiguration (req:Request.Login) =
    task {
        let! maybeUser = req.Email |> getUserByEmail // implement such function
        return
            maybeUser
            |> Option.bind (fun x -> 
                if Password.verifyPassword x.PwdHash req.Password then Some x else None
            )
            |> Option.map (userToToken cfg >> tokenToResponse)
            |> ServerError.ofOption (Authentication "Bad login or password")
    }

let private getAnonymousService (cfg:JwtConfiguration) =
    {
        Login = login cfg >> Async.AwaitTask
    }

let anonymousAPI (cfg:JwtConfiguration) =
    Remoting.createApi()
    |> Remoting.withRouteBuilder AnonymousAPI.RouteBuilder
    |> Remoting.fromValue (getAnonymousService cfg)
    |> Remoting.withErrorHandler Remoting.errorHandler // see? we use our error handler here!
    |> Remoting.buildHttpHandler
```

And now add one that is secured. Add `Secured.fs`

```fs
let private getUserById (i:Guid) : UserFromDb option = failwith "TODO"

let private userToResponse (user:UserFromDb) : Response.UserInfo =
    {
        Name = user.Name
        Email = user.Email
    }

let private getUserInfo userId () =
    task {
        let! maybeUser = userId |> getUserById
        return
            maybeUser
            |> Option.map userToResponse
            |> ServerError.ofOption (Authentication "User account not found")
    }

let private getSecuredService (ctx:HttpContext) =
    let userId = ctx.User.Claims |> Seq.find (fun x -> x.Type = "id") |> (fun x -> Guid x.Value)
    {
        GetUserInfo = getUserInfo userId >> Async.AwaitTask
    }

let securedAPI =
    Remoting.createApi()
    |> Remoting.withRouteBuilder SecuredAPI.RouteBuilder
    |> Remoting.fromContext getSecuredService // <-- we need context here
    |> Remoting.withErrorHandler Remoting.errorHandler // see? we use our error handler here!
    |> Remoting.buildHttpHandler
```

And now just plug everything into Giraffe handlers. Create `WebApp.fs`

```
open Giraffe
open Microsoft.AspNetCore.Authentication.JwtBearer

let private mustBeLoggedIn : HttpHandler =
    requiresAuthentication (RequestErrors.UNAUTHORIZED JwtBearerDefaults.AuthenticationScheme "" "User not logged in")

let webApp (cfg:JwtConfiguration) : HttpHandler =
    choose [
        anonymousAPI cfg
        mustBeLoggedIn >=> choose [
            securedAPI
        ]
        htmlFile "public/index.html"
    ]
```

Hold on, server part is nearly done!

Add the `Startup.fs` file

```fs
open System
open Microsoft.AspNetCore.Authentication.JwtBearer
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.DependencyInjection
open Giraffe

type Startup(cfg:IConfiguration, evn:IWebHostEnvironment) =
    // read values from config or ENV vars
    let cfg = {
        Audience = cfg.["JwtAudience"]
        Issuer = cfg.["JwtIssuer"]
        Secret = cfg.["JwtSecret"]
        AccessTokenLifetime = TimeSpan.FromMinutes 10.
    }

    member _.ConfigureServices (services:IServiceCollection) =
        services
            .AddAuthorization(fun auth ->
                auth.DefaultPolicy <-
                    Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
                        .AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme)
                        .RequireAuthenticatedUser()
                        .Build()
            )
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(Action<JwtBearerOptions>(fun opts ->
                    opts.TokenValidationParameters <- JWT.getParameters cfg
                )
            )
            |> ignore
        services.AddGiraffe() |> ignore
    member _.Configure(app:IApplicationBuilder) =
        app
            .UseStaticFiles()
            .UseAuthentication()
            .UseGiraffe (WebApp.webApp cfg)
```
And just add `Program.fs` file

```fs
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
open Microsoft.AspNetCore.Hosting

[<EntryPoint>]
let main _ =
    Host.CreateDefaultBuilder()
        .ConfigureWebHostDefaults(
            fun webHostBuilder ->
                webHostBuilder
                    .UseStartup(typeof<Startup.Startup>)
                    .UseUrls([|"http://0.0.0.0:5000"|])
                    .UseWebRoot("public")
                    |> ignore)
        .Build()
        .Run()
    0
```

Nicely done! The hardest part is done. Now just move to frontend

## Consume API from frontend

Let's start with creating `Server.fs` file

```fs
let exnToError (e:exn) : ServerError =
    match e with
    | :? ProxyRequestException as ex ->
        try
            let serverError = Json.parseAs<{| error: ServerError |}>(ex.Response.ResponseBody)
            serverError.error
        with _ ->
            if ex.StatusCode = 401 then ex.Response.ResponseBody |> ServerError.Authentication
            else (ServerError.Exception(e.Message))
    | _ -> (ServerError.Exception(e.Message))
    
let anonymousAPI =
    Remoting.createApi()
    |> Remoting.withRouteBuilder AnonymousAPI.RouteBuilder
    |> Remoting.buildProxy<AnonymousAPI>

let onSecuredAPI (fn:SecuredAPI -> Async<'a>) =
    let token = Browser.WebStorage.localStorage.getItem "token"
    Remoting.createApi()
    |> Remoting.withRouteBuilder SecuredAPI.RouteBuilder
    |> Remoting.withAuthorizationHeader (sprintf "Bearer %s" token)
    |> Remoting.buildProxy<SecuredAPI>
    |> fn
```

Now we can call login from `Login.fs` file

```
let displayStronglyTypedError = function
    // choose how to display errors based on your needs
    | ServerError.Exception x -> Html.text x
    | ServerError.Authentication x -> Html.div x

[<ReactComponent>]
let LoginView () =
    let loginForm,setLoginForm = React.useState({ Email = ""; Password = "" })
    let loginReq, setLoginReq = React.useState(Deferred.HasNotStartedYet)
    let login = React.useDeferredCallback ((fun _ -> Server.anonymousAPI.Login loginForm), setLoginReq)

    let result =
        match loginReq with
        | Deferred.HasNotStartedYet
        | Deferred.InProgress -> Html.none
        | Deferred.Failed ex -> ex |> Server.exnToError |> displayStronglyTypedError
        | Deferred.Resolved resp ->
            Browser.WebStorage.localStorage.setItem("token", resp.Token) // store for later usage
            Html.text "YOU ARE IN!"

    Html.div [
        Html.input [
            prop.type'.text
            prop.onTextChange (fun x -> { loginForm with Email = x } |> setLoginForm)
        ]
        Html.input [
            prop.type'.password
            prop.onTextChange (fun x -> { loginForm with Password = x } |> setLoginForm)
        ]
        Html.button [
            prop.text "LOGIN"
            if Deferred.inProgress loginReq then prop.disabled true
            prop.onClick login
        ]
    ]
```

And now the part where we use stored token. `MyProfile.fs`

```fs
[<ReactComponent>]
let MyProfileView () =
    let profileReq, setProfileReq = React.useState(Deferred.HasNotStartedYet)
    let getProfile = React.useDeferredCallback ((fun _ -> Server.onSecuredAPI.GetUserInfo()), setProfileReq)

    let info =
        match profileReq with
        | Deferred.HasNotStartedYet
        | Deferred.InProgress -> Html.none
        | Deferred.Failed ex -> ex |> Server.exnToError |> displayStronglyTypedError
        | Deferred.Resolved resp -> Html.div $"You are {resp.Name} with email {resp.Email}"

    Html.div [
        info
        Html.button [
            prop.text "WHO AM I?!"
            if Deferred.inProgress profileReq then prop.disabled true
            prop.onClick getProfile
        ]
    ]
```

Yup, that's all. Easy, right?!