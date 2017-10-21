# Fable.Remoting [In-depth Introduction](https://medium.com/@zaid.naom/introducing-fable-remoting-automated-type-safe-client-server-communication-for-fable-apps-e567454d594c)

[![Build Status](https://travis-ci.org/Zaid-Ajaj/Fable.Remoting.svg?branch=master)](https://travis-ci.org/Zaid-Ajaj/Fable.Remoting)

Automated and type-safe client-server communication for Fable Apps. 

Available Packages:

| Library  | Verion |
| ------------- | ------------- |
| Fable.Remoting.Client  | [![Nuget](https://img.shields.io/nuget/v/Fable.Remoting.Client.svg?colorB=green)](https://www.nuget.org/packages/Fable.Remoting.Client) |
| Fable.Remoting.Suave  | [![Nuget](https://img.shields.io/nuget/v/Fable.Remoting.Suave.svg?colorB=green)](https://www.nuget.org/packages/Fable.Remoting.Suave)  |
| Fable.Remoting.Giraffe  | [![Nuget](https://img.shields.io/nuget/v/Fable.Remoting.Giraffe.svg?colorB=green)](https://www.nuget.org/packages/Fable.Remoting.Giraffe)  |
 
## Suave
On a Suave server, install the library from Nuget using Paket:

```
paket add Fable.Remoting.Suave --project /path/to/Project.fsproj
```

## Shared code
Define the types you want to share between client and server:
```fs
// SharedTypes.fs
module SharedTypes

type Student = {
    Name : string
    Age : int
    Birthday : System.DateTime
    Subjects : string array
}

// Shared specs between Server and Client
type IServer = {
    getStudentByName : string -> Async<Student option>
    getAllStudents : unit -> Async<seq<Student>>
    getStudentSubjects : Student -> Async<string[]>
}
```
The type `IServer` is very important, this is the specification of what your server shares with the client. `Fable.Remoting` expects such type to only have functions of shape:
```
A' -> Async<B'>
```
Try to put such types in seperate files to reference these files later from the Client

Then provide an implementation for `IServer` on the server: 
```fs
open SharedTypes

let getStudents() = [
        { Name = "Mike";  Age = 23; Birthday = DateTime(1990, 11, 4); Subjects = [| "Math"; "CS" |] }
        { Name = "John";  Age = 22; Birthday = DateTime(1991, 10, 2); Subjects = [| "Math"; "English" |] }
        { Name = "Diana"; Age = 22; Birthday = DateTime(1991, 10, 2); Subjects = [| "Math"; "Phycology" |] }
    ]

let server : IServer = {
    getStudentByName = 
        fun name -> async {
            return getStudents() |> List.tryFind (fun student -> student.Name = name)
        }
    getStudentSubjects = fun student -> async { return student.Subjects }
    getAllStudents = fun () -> async { return getStudents() |> Seq.ofList }
}

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
You can think of the `webApp` value as if it was the following in psuedo-code:
```fs
let webApp = 
 choose [ 
  POST 
   >=> path "/IServer/getStudentByName" 
   >=> (* deserialize request body (from json) *) 
   >=> (* invoke server.getStudentByName with the deserialized input *) 
   >=> (* give client the output back serialized (to json) *)

 // other routes
 ]
```
You can enable logging from Fable.Remoting.Suave (recommended) to see how the magic is doing it's magic behind the scenes :)
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
  // allStudents : Student[]
  let! allStudents = server.getAllStudents()
  for student in allStudents do
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

See the following article if you are interested in how this library is implemented (a bit outdated but gives you an overview of the mechanism)
[Statically Typed Client-Server Communication with F#: Proof of Concept](https://medium.com/@zaid.naom/statically-typed-client-server-communication-with-f-proof-of-concept-7e52cff4a625#.2ltqlajm4)
