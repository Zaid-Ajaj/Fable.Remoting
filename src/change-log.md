# Change Log

### 2 August 2019
- Restructure repository by rewriting the integrations tests using Fable.Mocha and running them using Puppeteer.
- Update repository infrastructure: paket and FAKE to use the latest dotnet cli instead of mono-based paket and FAKE.
- Deprecate legacy Fable.Remoting.Client of Fable 1.x that was maintained for backwards compatiblity with legacy dotnet-fable. Users should update to Fable 2.x and use latest Fable.Remoting.Client v5.6.0

### 31 July 2019
- Added high performance JSON serialization by caching reflection calls from `UnionCaseInfo`. See [this PR](https://github.com/Zaid-Ajaj/Fable.Remoting/pull/128)

### 29 March 2019
- Read raw bytes for routes of `byte[]` inputs, bypassing JSON marshalling. See [this PR](https://github.com/Zaid-Ajaj/Fable.Remoting/pull/119).

### 28 March 2019
- Auto-qoutations support for dotnet client. See [this PR](https://github.com/Zaid-Ajaj/Fable.Remoting/pull/118).

### 27 March 2019
- Implement high performance paths for remote functions returning `Async<byte[]>` by writing and sending the raw data to the client, bypassing the JSON serialization and deserialization altogether. See [this PR](https://github.com/Zaid-Ajaj/Fable.Remoting/pull/117).

### 26 March 2019
- Cancel execution of an async workflow if the underlying request is aborted or cancelled. See [this PR](https://github.com/Zaid-Ajaj/Fable.Remoting/pull/115).

### 27 January 2019
 - Fixes `DateTimeOffset` deserialization issue [#109](https://github.com/Zaid-Ajaj/Fable.Remoting/issues/109) by [this PR](https://github.com/Zaid-Ajaj/Fable.Remoting/pull/110)

### 21 October 2018
- Remove Fable.PowerPack from the dependencies of remoting client in favor of simple XMLHttpRequest, fixes [#80](https://github.com/Zaid-Ajaj/Fable.Remoting/issues/80) in [this PR](https://github.com/Zaid-Ajaj/Fable.Remoting/pull/86)


### 19 August 2018
- Ported Fable client to Fable 2, using SimpleJson for JSON marshalling (see [PR](https://github.com/Zaid-Ajaj/Fable.Remoting/pull/71))
- Breaking change on the client: `Remoting.buildProxy<'t>()` becomes  `Remoting.buildProxy<'t>` when used, see [Client](client.md) docs

### 10th August 2018
 - Added ability to automatically generate interactive documentation from protocol definition (see [PR](https://github.com/Zaid-Ajaj/Fable.Remoting/pull/68))


### July 2018 (See [PR](https://github.com/Zaid-Ajaj/Fable.Remoting/pull/58))

 - Major infrastructure rewrite and improvements to Reflection code [#55](https://github.com/Zaid-Ajaj/Fable.Remoting/issues/55)
 - Checking whether the protocol definition is valid [#56](https://github.com/Zaid-Ajaj/Fable.Remoting/issues/56)
 - Run everywhere as [Asp.NET Core Middleware](aspnet-core.md) (closes [#19](https://github.com/Zaid-Ajaj/Fable.Remoting/issues/19) and [#20](https://github.com/Zaid-Ajaj/Fable.Remoting/issues/20))
 - Handle `GET` along with `POST` for remote functions that don't require input, i.e. functions of type `Async<'t>` or `unit -> Async<'t>` can be invoked using `GET` requests.
 - Remote functions of a single paramter of type `'a -> Async<'b>` don't require the request body to be an array of arguments, instead, you can send just the JSON object that can be deserialized to `'a`. This is not applicable to functions where `'a` is an array or list, fallback to a list of arguments is used.
 - Removed the `contextual` remoting handler [#50](https://github.com/Zaid-Ajaj/Fable.Remoting/issues/50) Use `fromContext` function that let you [gain access to the HttpContext](request-context.md).
 - Localized error handling for Fable client: no more global error handlers [#59](https://github.com/Zaid-Ajaj/Fable.Remoting/issues/59)
 - Using `ProxyRequestException` for [error handling](error-handling.md) that will give you access to the response when errors occur both for .NET and Fable client
 - Added docs for [Integration Testing](dotnet-integration-tests.md)
 - Removed a lot of the functionality from the Fable client proxy [#61](https://github.com/Zaid-Ajaj/Fable.Remoting/issues/61)
 - Added `reader` monad as shorthand for constructing values using `fromContext` (docs TBA)
 - Initial dependency injection support for Suave (docs TBA)