# Troubleshooting

The library tries to make the data exchange between the client and server as seemless as possible. We have many unit and integration tests to make sure the data types are well serialized and deserialized both on client and server. 

In the rare case that you encounter a type that doesn't seem to be serialized correctly, you can enable the *internal* logger of `Fable.Remoting` to see what is happening under the hood. You will see what the library is trying to deserialize and which method it will try to invoke.

```fs
let webApp = 
    Remoting.createApi()
    |> Remoting.fromValue musicStore
    |> Remoting.withDiagnosticsLogger (printfn "%s")
    |> Remoting.buildWebPart
}
```