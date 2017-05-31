# Fable.Remoting [In-depth Introduction]()

Automated and type-safe client-server communacation for Fable Apps. 

Supported server frameworks:
 - [x] Suave
 - [ ] Nancy
 - [ ] Freya
 
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
Define some types you want to share between client and server:
```fs
module SharedTypes

type Student = {
    Name : string
    Age : int
    Birthday : System.DateTime
    Subjects : string array
}

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


## Fable Client
Install `Fable.Remoting.Client from nuget using Paket:
```
paket add nuget Fable.Remoting.Client
```
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
That's it!

See the following article if you are interested in how this library is implemented (outdated now)
[Statically Typed Client-Server Communication with F#: Proof of Concept](https://medium.com/@zaid.naom/statically-typed-client-server-communication-with-f-proof-of-concept-7e52cff4a625#.2ltqlajm4)

Suggestions and/or contributions are appreciated :smile:  
