# Binary Serialization

If you have the need to transfer very large and complex objects from the API, you might experience performance bottlenecks caused by JSON serialization. To address this, or if you simply want to squeeze out some extra performance, you can try switching away from JSON to binary serialization using the [MessagePack](https://msgpack.org/index.html) format.

Using MessagePack over JSON generally results in faster serialization and deserialization as well as smaller payloads. However, please note that you will no longer be easily able to visually inspect server responses, for instance on the Network tab in DevTools.

Switching to binary serialization is very simple. All you have to do is instruct the API handler and the client proxy to use this format, like so:

```fs
// on the server
let webApp : HttpHandler = 
    Remoting.createApi()
    |> Remoting.fromValue musicStore
    |> Remoting.withBinarySerialization
    |> Remoting.buildHttpHandler
```

```fs
// on the client
let musicStore : IMusicStore = 
    Remoting.createApi()
    |> Remoting.withBinarySerialization
    |> Remoting.buildProxy<IMusicStore>
```

In order to further reduce message size, you may want to consider enabling response compression for the `application/msgpack` MIME type.
