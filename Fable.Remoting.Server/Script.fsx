#r "bin/Debug/Fable.Remoting.Reflection.dll"
#r "bin/Debug/Fable.Remoting.Server.dll"

open Fable.Remoting.Reflection
open Fable.Remoting.Server

type Server = { 
    getLength : string -> Async<int>
}

    
let serverImpl = {
    getLength = fun input -> async { return input.Length }
}


async {
    let! asyncResult  = 
        Server.dynamicallyInvoke "getLength" serverImpl "hello-there" 
    return asyncResult
}
|> Async.RunSynchronously

open FSharp.Reflection

let generateUrlsFrom implementation = 
    let typeName = implementation.GetType().Name
    implementation.GetType()
    |> FSharpType.GetRecordFields 
    |> Seq.map (fun propInfo -> propInfo.Name)
    |> Seq.map (fun methodName -> sprintf "/%s/%s" typeName methodName)

// Define your library scripting code here
generateUrlsFrom serverImpl
