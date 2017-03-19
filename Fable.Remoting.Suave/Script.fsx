// Learn more about F# at http://fsharp.org. See the 'F# Tutorial' project
// for more guidance on F# programming.

// Define your library scripting code here

#r "bin/Debug/Fable.Remoting.Reflection.dll"

open Fable.Remoting.Reflection

type IServer = { 
  getLength : string -> int
}

let simpleSyncServer : IServer = { 
  getLength = fun input -> input.Length
}

/// call a method on a record type and pass it an argument
let dynamicallyInvoke methodName implementation methodArg = 
    implementation
        .GetType()
        .GetProperty(methodName)
        .GetValue(implementation, null)
        |> unbox<'A -> 'B>            // treat the property as a function
        |> fun func -> func methodArg // pass the argument to that function

let result : obj = FSharpRecord.Invoke("getLength", simpleSyncServer, "hello")
