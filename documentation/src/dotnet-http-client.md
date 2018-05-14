# Use a custom HttpClient 

When constructing the proxy, you can provide your own `HttpClient` that the proxy uses under the hood to communicate with the server. This has many use cases, for example, when you want to use a secure client certificate, configure the headers of the requests or when you want to test the Giraffe/Saturn pipeline directly from the client using Kestrel's test `HttpClient`

```fs
// using ASP.NET Core's test server
let server = new TestServer(WebHostBuilder().UseStartup<Startup>()

// the test server gives you a specialized HttpClient for testing
let client = server.CreateClient()

// route builder that specifies how the routes are built
let builder = ...

// construct the proxy using the test HttpClient
let proxy = Proxy.custom<'T> builder client
``` 