# Fable.Remoting for .NET Clients

Although Fable.Remoting is initially implemented to allow type-safe communication between a .NET backend and a Fable frontend, it will become a slight inconvenience when you start building a Xamarin or WPF application that needs to talk to the same backend because you need to use raw http for communication. 

However, this is not case anymore because as of now, we implemented a dotnet client that is compatible with web servers that are using Fable.Remoting. This library allows you to re-use the shared protocols/contracts for type-safe communication with your backend the same you did from your Fable frontend.

In fact, you can use the dotnet client with a dotnet server without a Fable project involved, think client-server interactions purely in F#.

### Installation 
Install the library from [Nuget](https://www.nuget.org/packages/Fable.Remoting.DotnetClient/): 
```bash
paket add Fable.Remoting.DotnetClient --project /path/to/App.fsproj
# or 
dotnet add package Fable.Remoting.DotnetClient
```  

### Using the library 

As you would expect, you need to reference the shared types and protocols to your client project: 
```xml
<Compile Include="..\Shared\SharedTypes.fs" />
```
Now the code is similar the Fable client API with a couple of differences: 
```fs
open Fable.Remoting.DotnetClient
open SharedTypes

// specifies how the routes should be generated
let routes = sprintf "http://backend.api.io/v1/%s/%s"

// proxy: Proxy<IServer> 
let proxy = Proxy.create<IServer> routes 

// optionally add an authorization header
proxy.authorisationHeader "Bearer ..." 

async { 
    // length : int
    let! length = proxy.call <@ fun server -> server.getLength "hello" @>
    // 5 
    return length 
} 
```
The major difference is the use of quotations, which simplified and implementation process greatly and keeps the solution entirely type-safe without [fighting with the run-time](https://stackoverflow.com/questions/50131906/f-how-to-create-an-async-function-dynamically-based-on-return-type/50135445) with boxing/unboxing hacks to get types right. 

The `proxy.call` approach allows for more control around the call to the server and can be easily extended, for example, you can *safely* call the server using a different proxy method `proxy.callSafely` which will catch exceptions thrown by the web request at call-site instead of using global handlers like with Fable client:
```fs
async {
    let! result = proxy.callSafely <@ fun server -> server.throwError() @> 
    match result with 
    | Ok value -> (* will not match *) 
    | Error ex -> 
        | match ex with 
        | :? Http.InternalServerErrorException -> 
            Expect.isTrue true "This is the correct exception" 
        | :? Http.UnauthorisedException -> (* handle authorization *)
        | :? Http.ForbiddenException -> (* handle forbidded *)
        | :? Http.NotOkException as notOk -> 
            // generic http exception for any other status code 
            // that is not 200 (OK) 
            let response = notOk.Response
            (* handle response your self *) 
}
```