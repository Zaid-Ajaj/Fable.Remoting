# Fable.Remoting [In-depth Introduction](https://medium.com/@zaid.naom/introducing-fable-remoting-automated-type-safe-client-server-communication-for-fable-apps-e567454d594c)

[![Build Status](https://travis-ci.org/Zaid-Ajaj/Fable.Remoting.svg?branch=master)](https://travis-ci.org/Zaid-Ajaj/Fable.Remoting)

## About
Automated and type-safe client-server communication (RPC) for Fable Apps. This is a library that abstracts http and lets you think of your client-server interactions only in terms of pure functions and being only a part of the webserver. The library supports Suave and Giraffe on the server and Fable on the client. 


## Quick Start
Use the [SAFE Template](https://github.com/SAFE-Stack/SAFE-template) where Fable.Remoting is a scaffolding option:

```sh
# install the template
dotnet new -i SAFE.Template

# scaffold a new Fable/Suave project with Fable.Remoting
dotnet new SAFE --Remoting

# Or use Giraffe as your server
dotnet new SAFE --Server giraffe --Remoting
```


Feedback and suggestions are very much welcome.

## Available Packages:

| Library  | Version |
| ------------- | ------------- |
| Fable.Remoting.Client  | [![Nuget](https://img.shields.io/nuget/v/Fable.Remoting.Client.svg?colorB=green)](https://www.nuget.org/packages/Fable.Remoting.Client) |
| Fable.Remoting.Suave  | [![Nuget](https://img.shields.io/nuget/v/Fable.Remoting.Suave.svg?colorB=green)](https://www.nuget.org/packages/Fable.Remoting.Suave)  |
| Fable.Remoting.Giraffe  | [![Nuget](https://img.shields.io/nuget/v/Fable.Remoting.Giraffe.svg?colorB=green)](https://www.nuget.org/packages/Fable.Remoting.Giraffe)  |
| Fable.Remoting.Saturn  | [![Nuget](https://img.shields.io/nuget/v/Fable.Remoting.Saturn.svg?colorB=green)](https://www.nuget.org/packages/Fable.Remoting.Saturn)  |

 
## Scaffold from scratch - Suave
Create a new F# console app:
```
dotnet new console -lang F#
``` 
Define the types you want to share between client and server:
```fs
// SharedTypes.fs
module SharedTypes

type Student = {
    Name : string
    Age : int
}

// Shared specs between Server and Client
type IServer = {
    studentByName : string -> Async<Student option>
    allStudents : unit -> Async<seq<Student>>
}
```
The type `IServer` is very important, this is the specification of what your server shares with the client. `Fable.Remoting` expects such type to only have functions of shape:
```
A -> Async<B>
```
Try to put such types in seperate files to reference these files later from the Client

Then provide an implementation for `IServer` on the server: 
```fs
open SharedTypes

let getStudents() = [
        { Name = "Mike";  Age = 23; }
        { Name = "John";  Age = 22; }
        { Name = "Diana"; Age = 22; }
    ]

let pure x = async { return x }

// An implementation of the `IServer` protocol
let server : IServer = {

    studentByName = fun name -> 
        getStudents()
        |> List.tryFind (fun student -> student.Name = name)
        |> pure

    allStudents = fun () -> 
        getStudents() 
        |> Seq.ofList
        |> pure
}

```
Install the library from Nuget using Paket:

```
paket add Fable.Remoting.Suave --project /path/to/Project.fsproj
```
Create a [WebPart](https://suave.io/composing.html) from the value `server` using `FableSuaveAdapter.webPartFor` and start your Suave server:
```fs
open Suave
open Fable.Remoting.Suave

[<EntryPoint>]
let main argv = 
    // create the WebPart
    let webApp = FableSuaveAdapter.webPartFor server  
    // start the web server
    startWebServer defaultConfig webApp
    // wait for a key press to exit
    Console.ReadKey() |> ignore
    0 
```
Yes. it is that simple. 
You can think of the `webApp` value as if it was the following in psuedo-code:
```fs
let webApp = 
 choose [ 
  POST 
   >=> path "/IServer/studentByName" 
   >=> (* deserialize request body (from json) *) 
   >=> (* invoke server.getStudentByName with the deserialized input *) 
   >=> (* give client the output back serialized (to json) *)

 // other routes
 ]
```
You can enable logging from Fable.Remoting.Suave (recommended) to see how the library is doing it's magic behind the scenes :)
```fs
FableSuaveAdapter.logger <- Some (printfn "%s")
```
## Fable Client
Install `Fable.Remoting.Client` from nuget using Paket:
```
paket add Fable.Remoting.Client --project /path/to/Project.fsproj
```
Reference the shared types to your client project 
```
<Compile Include="path/to/SharedTypes.fs" />
```
Start using the library:
```fs
open Fable.Remoting.Client
open SharedTypes

// server : IServer
let server = Proxy.create<IServer>


async {
  // students : Student[]
  let! students = server.allStudents()
  for student in students do
    // student : Student
    printfn "Student %s is %d years old" student.Name student.Age
}
|> Async.StartImmediate
```
Finally, when you are using `webpack-dev-server`, you have to change the config from this:
```js
devServer: {
  contentBase: resolve('./public'),
  port: 8080
}
```
to this:
```js
devServer: {
  contentBase: resolve('./public'),
  port: 8080,
  proxy: {
    '/*': { // tell webpack-dev-server to re-route all requests from client to the server
      target: "http://localhost:8083",// assuming the suave server is hosted op port 8083
      changeOrigin: true
    }
}
```
That's it!
## Error handling
What happens when an error is thrown by one of the RPC methods? 

Well, good question! Fable.Remoting will catch unhandled exceptions on the server and the sends them off to the `onError` handler. This handler can choose to `Ignore` or `Propagate msg` back to the client:

```fs
/// === Propagating custom errors or ignoring them on the server ======
type CustomError = { errorMsg: string }

FableSuaveAdapter.onError <| fun ex routeInfo ->
    // do some logging
    printfn "Error at: %A" routeInfo
    logException ex
    match ex with
    | :? System.IOException as x -> 
        // propagate custom error, this is intercepted by the client
        let customError = { errorMsg = "Something terrible happend" }
        Propagate customError
    | :? System.Exception as x ->
        // ignore error
        Ignore
``` 
On the client side, an exception is thrown locally on the call site. However, when a message is propagated from the server, is it also intercepted by the `onError` handler on the client side using this global `onError` handler:
```fs
Proxy.onError <| fun errorInfo ->
    let customError = ofJson<CustomError> errorInfo.error
    printfn "Oh noo: %s" custromError.errorMsg
```

## Adding a new route
 - Add another record field function to `IServer`
 - Implement that function
 - Restart server
 
Done! You can now use that function from the client too. 

## Customizations
You can generate different paths for your POST routes on the server very easily using  a route builder, for example to generate routes with paths prefixed with `/api/`:
```fs
let routeBuilder typeName methodName = 
 sprintf "/api/%s/%s" typeName methodName
 
let webApp = FableSuaveAdapter.webPartWithBuilderFor server routeBuilder
```
Ofcourse, the proxy generated on the client has to match the routes created on the server:
```fs 
let routeBuilder typeName methodName = 
 sprintf "/api/%s/%s" typeName methodName
 
let server = Proxy.createWithBuilder<IServer> routeBuilder
```
And webpack-dev-server config:
```js
devServer: {
  contentBase: resolve('./public'),
  port: 8080,
  proxy: {
    '/api/*': { // tell webpack-dev-server to re-route requests that start with /api/
      target: "http://localhost:8083",// assuming the suave server is hosted op port 8083
      changeOrigin: true
    }
}
```
## Testing

This library is very well tested and includes unit tests for each server type and their internal components using Expecto. Moreover, the repo includes an integration-tests projects where the client uses the awesome QUnit testing framework to make server calls on many different types to check that serialization and deserialization work as expected. 

Server side unit-tests look like this
```fs
testCase "Map<string, int> roundtrip" <| fun () ->
    ["one",1; "two",2]
    |> Map.ofList
    |> toJson
    |> request "/IProtocol/echoMap"
    |> ofJson<Map<string, int>>
    |> Map.toList
    |> function
        | ["one",1; "two",2] -> pass()
        | otherwise -> fail()
```
Client-side integration tests
```fs
QUnit.testCaseAsync "IServer.echoResult for Result<int, string>" <| fun test ->
    async {
        let! outputOk = server.echoResult (Ok 15)
        match outputOk with
        | Ok 15 -> test.pass()
        | otherwise -> test.fail()

        let! outputError = server.echoResult (Error "hello")
        match outputError with
        | Error "hello" -> test.pass()
        | otherwise -> test.fail()
    } 
```
See the following article if you are interested in how this library is implemented (a bit outdated but gives you an overview of the mechanism)
[Statically Typed Client-Server Communication with F#: Proof of Concept](https://medium.com/@zaid.naom/statically-typed-client-server-communication-with-f-proof-of-concept-7e52cff4a625#.2ltqlajm4)
