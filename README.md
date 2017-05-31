# Fable.Remoting [In-depth Introduction]()

Automated and type-safe client-server communacation for Fable Apps. 

Supported server frameworks:
 - [x] Suave
    - [x] .NET Framework 4.5  
    - [ ] .NET Core (WIP)
 - [ ] Nancy
    - [ ] .NET Framework 4.5 (WIP)
    - [ ] .NET Core
 - [ ] Freya
    - [ ] .NET Framework 4.5
    - [ ] .NET Core
 
## Suave
On a Suave server, install the library from Nuget:
```
Install-Package Fable.Remoting.Suave
```
or using Paket
```
paket add nuget Fable.Remoting.Suave 
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
Provide an implementation for `IServer`:
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
Create a WebPart from the value `server` using `FableSuaveAdapter.webPartFor` and start your Suave server:
```fs
open Suave
open Fable.Remoting.Suave

[<EntryPoint>]
let main argv = 
    // create the webpart with route builder
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
  POST >=> 
   path "/IServer/getStudentByName" 
   >=> /* deserialize body (from json) */ 
   >=> /* invoke server.getStudentByName */ 
   >=> /* give client the results serialized (to json) */

 // other routes
 ]
```
You can enable logging from Fable.Remoting.Suave (recommend) to see how the magic is doing it's magic behind the scenes :)
```fs
FableSuaveAdapter.logger <- Some (printfn "%s")
```
## Fable Client
Install `Fable.Remoting.Client` from nuget using Paket:
```
paket add nuget Fable.Remoting.Client
```
Make sure Fable.Core >= 1.0.7
Reference the shared types to youe client project and 
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
  let! allStudents = server.getAllStudents()
  for student in allStudents do
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

======================
### Customizations
Generating different routes on the server using route builder, for example routes prefixed with `/api/`:
```fs
let routeBuilder typeName methodName = 
 sprintf "/api/%s/%s" typeName methodName
 
let webApp = FableSuaveAdapter.webPartWithBuilderFor server routeBuilder
```
Ofcourse, the proxy generated on the client has to match the routes:
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
