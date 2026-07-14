namespace Fable.Remoting.Server

module Remoting =

    /// Cached default System.Text.Json options for the new default serializer
    /// path. Built once at module init — registering the converter set against
    /// a fresh JsonSerializerOptions does ~24 reflection-driven Add calls, so
    /// allocating per createApi() would be wasteful for app patterns that
    /// build multiple APIs in a loop (test harnesses, per-tenant proxies, etc).
    /// Once any serializer call has used these options STJ freezes them, which
    /// is also fine for shared use — every caller gets the same byte-compat
    /// converter registration.
    let private defaultStjOptions =
        Fable.Remoting.Json.SystemTextJson.FableConverters.create()

    let documentation (name: string) (routes: RouteDocs list) : Documentation = Documentation (name, routes)

    /// Starts with the default configuration for building an API.
    ///
    /// **Default JSON serializer is now System.Text.Json** (since the
    /// Newtonsoft-retirement work). The wire format is byte-equal to the
    /// previous Newtonsoft default — verified by 349 byte-pin tests in
    /// `Fable.Remoting.Json.Tests/WireFormatTests.fs` running the same
    /// assertions against both serializers. Existing Fable / DotnetClient
    /// clients see no change in the bytes they read on the wire.
    ///
    /// To opt back into Newtonsoft.Json (e.g. during migration), pipe
    /// through `Remoting.withNewtonsoftJson`. That helper is marked
    /// `[<Obsolete>]` — it will be removed in a future major version when
    /// the legacy Newtonsoft path is deleted from `Fable.Remoting.Json`
    /// entirely.
    let createApi()  =
        { Implementation = Empty
          RouteBuilder = sprintf "/%s/%s"
          ErrorHandler = None
          DiagnosticsLogger = None
          Docs = None, None
          ResponseSerialization = Json
          JsonSerializer = SystemTextJson defaultStjOptions
          RmsManager = None }

    /// Defines how routes are built using the type name and method name. By default, the generated routes are of the form `/typeName/methodName`.
    let withRouteBuilder builder options = 
        { options with RouteBuilder = builder } 

    /// Enables the diagnostics logger that will log what steps the library is taking when a request comes in. This could help troubleshoot serialization issues but it could be also be interesting to see what is going on under the hood.
    let withDiagnosticsLogger logger options = 
        { options with DiagnosticsLogger = Some logger }

    /// Enables the automatic generation of API documentation based on type-metadata 
    let withDocs (url: string) (docs: Documentation) options = 
        { options with Docs = Some url, Some docs }

    /// Enables you to define a custom error handler for unhandled exceptions thrown by your remote functions. It can also be used for logging purposes or if you wanted to propagate errors back to client.
    let withErrorHandler handler options = 
        { options with ErrorHandler = Some handler }

    /// Specifies that the API only uses binary serialization
    let withBinarySerialization (options: RemotingOptions<'t, 'implementation>) =
        { options with ResponseSerialization = MessagePack }

    /// Override the System.Text.Json options used by this API.
    ///
    /// `Remoting.createApi()` already defaults to a cached
    /// `Fable.Remoting.Json.SystemTextJson.FableConverters.create()` instance
    /// — use this helper only when you need a customised `JsonSerializerOptions`
    /// (e.g. additional converters, different `WriteIndented`, a stricter
    /// encoder). The byte-compatible converter set is registered automatically
    /// by `FableConverters.addTo` / `FableConverters.create()`; if you pass an
    /// `options` instance you constructed yourself, call `addTo` first to
    /// ensure the Fable wire shape is preserved.
    ///
    /// ```fsharp
    /// open Fable.Remoting.Server
    /// open Fable.Remoting.Json.SystemTextJson
    ///
    /// let myOptions = System.Text.Json.JsonSerializerOptions(WriteIndented = true)
    /// FableConverters.addTo myOptions
    ///
    /// let api =
    ///     Remoting.createApi()
    ///     |> Remoting.fromValue myImpl
    ///     |> Remoting.withSerializerOptions myOptions
    /// ```
    let withSerializerOptions (jsonOptions: System.Text.Json.JsonSerializerOptions) (options: RemotingOptions<'t, 'implementation>) =
        { options with JsonSerializer = SystemTextJson jsonOptions }

    /// Opt back into the legacy Newtonsoft.Json serializer path. Useful for
    /// migration — pin an API to the old serializer while you verify the
    /// STJ path is byte-equal in your specific deployment. Will be removed
    /// in a future major version along with the Newtonsoft converter and
    /// the transitive Newtonsoft package reference.
    [<System.Obsolete "The Newtonsoft.Json path is deprecated and will be removed in a future major version. The new default (System.Text.Json with Fable.Remoting.Json.SystemTextJson.FableConverters.create()) produces byte-equal wire output. See MIGRATION.md for the migration path.">]
    let withNewtonsoftJson (options: RemotingOptions<'t, 'implementation>) =
        { options with JsonSerializer = NewtonsoftJson }

    /// Enables you to provide your own instance of a recyclable memory stream manager
    let withRecyclableMemoryStreamManager rmsManager options =
        { options with RmsManager = Some rmsManager }

    /// Builds the API using a function that takes the incoming Http context and returns a protocol implementation. You can use the Http context to read information about the incoming request and also use the Http context to resolve dependencies using the underlying dependency injection mechanism.
    let fromContext (f: 'ctx -> 't) (options: RemotingOptions<'ctx, 't>) = 
        { options with Implementation = FromContext f }

    /// Builds the API using the provided static protocol implementation 
    let fromValue (serverImpl: 'implementation) (options: RemotingOptions<'t, 'implementation>)  = 
        { options with Implementation = StaticValue serverImpl }