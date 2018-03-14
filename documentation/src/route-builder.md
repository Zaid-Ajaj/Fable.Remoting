# Custom Route Paths 

By default, the path of the generated routes are of the form `/{typeName}/{methodName}`. For our `IMusicStore` type, the method `createAlbum` will have the route `/IMusicStore/createAlbum`. You can override this behaviour by providing your own route builder. For example, to prefix your route with `/api` you can use the following:

```fs
let routeBuilder (typeName: string) (methodName: string) = 
    sprintf "/api/%s/%s" typeName methodName

// now use it with remoting builder
let webApp = remoting musicStore {
    with_builder routeBuilder
}
```
Ofcourse, the routes must match both on client and server, so we override the behaviour on the client too:
```fs
// Assuming the function routeBuilder is shared 
// between the client and server
let musicStore = Proxy.remoting<IMusicStore> {
    with_builder routeBuilder
}
```
On last thing you need to consider if you are using `webpack-dev-server` is that you need to delegate the requests that start with `/api` from the developement server to the actual F# backend using the following configuration:
```js
devServer: {
  contentBase: resolve('./public'),
  port: 8080,
  proxy: {
    // delegate the requests prefixed with /api/
    '/api/*': {
      target: "http://localhost:8083",
      changeOrigin: true
    }
}
```