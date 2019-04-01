# Fable.Remoting 

[![Build Status](https://travis-ci.org/Zaid-Ajaj/Fable.Remoting.svg?branch=master)](https://travis-ci.org/Zaid-Ajaj/Fable.Remoting) [![Build status](https://ci.appveyor.com/api/projects/status/euhwktyycm2wvvi4?svg=true)](https://ci.appveyor.com/project/Zaid-Ajaj/fable-remoting)

Fable.Remoting is a [RPC](https://en.wikipedia.org/wiki/Remote_procedure_call) communication layer for Fable and .NET apps, it abstracts away Http and Json and lets you think of your client-server interactions only in terms of pure stateless functions that are statically checked at compile-time:

### Define a shared interface
This interface is a record type where each field is a function that returns `Async<'T>` 

```fs
type IGreetingApi = {
  greet : string -> Async<string>
}
```

### Implement the interface on the *server*

```fs
let greetinApi = {
  greet = fun name ->
    async {
      let greeting = sprintf "Hello, %s" name
      return greeting
    }
}

// Expose the implementation as a HTTP service
let webApp = 
  Remoting.createApi()
  |> Remoting.fromValue greetinApi
```

### Call the functions from the *client*
```fs
// get a typed-proxy for the service
let greetingApi = 
  Remoting.createApi()
  |> Remoting.buildProxy<IGreetingApi>

// Start using the service  
async {
  let! message = greetingApi.greet "World"
  printfn "%s" message // Hello, World
}
```
That's it, no HTTP, no JSON and it is all type-safe.

### Applications using Remoting
- [SAFE-TodoList](https://github.com/Zaid-Ajaj/SAFE-TodoList) A simple full-stack Todo list application (beginner)
- [tabula-rasa](https://github.com/Zaid-Ajaj/tabula-rasa) a real-world-ish blogging platform (intermediate)
- [Yobo](https://github.com/Dzoukr/Yobo) Yoga Class Booking System
implemented with Event Sourcing (advanced)

### [Full Documentation](https://zaid-ajaj.github.io/Fable.Remoting/)

The library runs everywhere on the backend: As Suave `WebPart`, as Giraffe/Saturn `HttpHandler` or any other framework as Asp.NET Core middleware. Clients can be Fable or .NET application.

## Quick Start
Use the [SAFE Template](https://github.com/SAFE-Stack/SAFE-template) where Fable.Remoting is a scaffolding option:

```sh
# install the template
dotnet new -i SAFE.Template

# scaffold a new Fable/Saturn project with Fable.Remoting
dotnet new SAFE --communication remoting

# Or use Giraffe as your server
dotnet new SAFE --server giraffe --communication remoting

# Or use Suave as your server
dotnet new SAFE --server suave --communication remoting
```
## Available Packages:

| Library  | Version |
| ------------- | ------------- |
| Fable.Remoting.Client  | [![Nuget](https://img.shields.io/nuget/v/Fable.Remoting.Client.svg?colorB=green)](https://www.nuget.org/packages/Fable.Remoting.Client) |
| Fable.Remoting.Suave  | [![Nuget](https://img.shields.io/nuget/v/Fable.Remoting.Suave.svg?colorB=green)](https://www.nuget.org/packages/Fable.Remoting.Suave)  |
| Fable.Remoting.Giraffe  | [![Nuget](https://img.shields.io/nuget/v/Fable.Remoting.Giraffe.svg?colorB=green)](https://www.nuget.org/packages/Fable.Remoting.Giraffe)  |
| Fable.Remoting.AspNetCore  | [![Nuget](https://img.shields.io/nuget/v/Fable.Remoting.AspNetCore.svg?colorB=green)](https://www.nuget.org/packages/Fable.Remoting.AspNetCore)  |
| Fable.Remoting.DotnetClient  | [![Nuget](https://img.shields.io/nuget/v/Fable.Remoting.DotnetClient.svg?colorB=green)](https://www.nuget.org/packages/Fable.Remoting.DotnetClient)  |

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
type IStudentApi = {
    studentByName : string -> Async<Student option>
    allStudents : unit -> Async<list<Student>>
}
```
The type `IStudentApi` is very important, this is the specification of the protocol between your server and client. `Fable.Remoting` expects such type to only have functions returning `Async` on the final result:
```fs
Async<A>
A -> Async<B>
A -> B -> Async<C>
// etc...
```
Try to put such types in seperate files to reference these files later from the Client

Then provide an implementation for `IStudentApi` on the server:
```fs
open SharedTypes

let getStudents() = 
  async {
    return [
        { Name = "Mike";  Age = 23; }
        { Name = "John";  Age = 22; }
        { Name = "Diana"; Age = 22; }
    ]
  }

let findStudentByName name = 
  async {
    let! students = getStudents() 
    let student = List.tryFind (fun student -> student.Name = name) students
    return student 
  }

let studentApi : IStudentApi = {
    studentByName = findStudentByName
    allStudents = getStudents 
}
```
Now that we have the implementation `studentApi`, you can expose it as a web service from different web frameworks. We start with [Suave](https://github.com/SuaveIO/suave)  


Install the library from Nuget using Paket:

```
paket add Fable.Remoting.Suave --project /path/to/Project.fsproj
```
Create a [WebPart](https://suave.io/composing.html) from the value `studentApi`
```fs
open Suave
open Fable.Remotion.Server
open Fable.Remoting.Suave

let webApp : WebPart = 
    Remoting.createApi()
    |> Remoting.fromValue studentApi
    |> Remoting.buildWebPart 

// start the web server
startWebServer defaultConfig webApp
```
Yes, it is that simple.
You can think of the `webApp` value as if it was the following in pseudo-code:
```fs
let webApp =
 choose [
  POST
   >=> path "/IStudentApi/studentByName"
   >=> (* deserialize request body (from json) *)
   >=> (* invoke studentApi.getStudentByName with the deserialized input *)
   >=> (* give client the output back serialized (to json) *)

 // other routes
 ]
```
You can enable diagnostic logging from Fable.Remoting.Server (recommended) to see how the library is doing it's magic behind the scenes :)
```fs
let webApp = 
    Remoting.createApi()
    |> Remoting.fromValue studentApi
    |> Remoting.withDiagnosticsLogger (printfn "%s")
    |> Remoting.buildWebPart 
```
### AspNetCore Middleware 
Install the package from Nuget using paket
```
paket add Fable.Remoting.AspNetCore --project /path/to/Project.fsproj
```
Now you can configure your remote handler as AspNetCore middleware 
```fs
let webApp = 
    Remoting.createApi()
    |> Remoting.fromValue studentApi

let configureApp (app : IApplicationBuilder) =
    // Add Remoting handler to the ASP.NET Core pipeline
    app.UseRemoting webApp

[<EntryPoint>]
let main _ =
    WebHostBuilder()
        .UseKestrel()
        .Configure(Action<IApplicationBuilder> configureApp)
        .Build()
        .Run()
    0
```

### Giraffe

You can follow the Suave part up to the library installation, where it will become:
```
paket add Fable.Remoting.Giraffe --project /path/to/Project.fsproj
```

Now instead of a WebPart, by opening the `Fable.Remoting.Giraffe` namespace, you will get a [HttpHandler](https://github.com/giraffe-fsharp/Giraffe/blob/master/DOCUMENTATION.md#httphandler) from the value `server`:
```fs
open Giraffe
open Fable.Remoting.Server
open Fable.Remoting.Giraffe

let webApp : HttpHandler = 
    Remoting.createApi()
    |> Remoting.fromValue studentApi
    |> Remoting.buildHttpHandler 

let configureApp (app : IApplicationBuilder) =
    // Add Giraffe to the ASP.NET Core pipeline
    app.UseGiraffe webApp

let configureServices (services : IServiceCollection) =
    // Add Giraffe dependencies
    services.AddGiraffe() |> ignore

[<EntryPoint>]
let main _ =
    WebHostBuilder()
        .UseKestrel()
        .Configure(Action<IApplicationBuilder> configureApp)
        .ConfigureServices(configureServices)
        .Build()
        .Run()
    0
```

### Saturn

You can use the same webApp generated by the Giraffe library.

```fs
open Saturn
open Fable.Remoting.Server
open Fable.Remoting.Giraffe

let webApp : HttpHandler = 
    Remoting.createApi()
    |> Remoting.fromValue studentApi
    |> Remoting.buildHttpHandler 

let app = application {
    url "http://127.0.0.1:8083/"
    use_router webApp
}

run app
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

// studentApi : IStudentApi
let studentApi =
    Remoting.createApi()
    |> Remoting.buildProxy<IStudentApi>

async {
  // students : Student[]
  let! students = studentApi.allStudents()
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


## Adding a new route
 - Add another record field function to `IStudentApi`
 - Implement that function
 - Restart server

Done! You can now use that function from the client too.


See the following article if you are interested in how this library is implemented (a bit outdated but gives you an overview of the mechanism)
[Statically Typed Client-Server Communication with F#: Proof of Concept](https://medium.com/@zaid.naom/statically-typed-client-server-communication-with-f-proof-of-concept-7e52cff4a625#.2ltqlajm4)

### [In-depth Introduction (Blog)](https://medium.com/@zaid.naom/introducing-fable-remoting-automated-type-safe-client-server-communication-for-fable-apps-e567454d594c)
