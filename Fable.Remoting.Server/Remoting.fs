namespace Fable.Remoting.Server

module Remoting = 
    
    let documentation (name: string) (routes: RouteDocs list) : Documentation = Documentation (name, routes)

    /// Starts with the default configuration for building an API 
    let createApi()  = 
        { Implementation = Empty 
          RouteBuilder = sprintf "/%s/%s" 
          ErrorHandler = None 
          DiagnosticsLogger = None
          Docs = None, None
          ResponseSerialization = Json
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

    /// Enables you to provide your own instance of a recyclable memory stream manager
    let withRecyclableMemoryStreamManager rmsManager options =
        { options with RmsManager = Some rmsManager }

    /// Builds the API using a function that takes the incoming Http context and returns a protocol implementation. You can use the Http context to read information about the incoming request and also use the Http context to resolve dependencies using the underlying dependency injection mechanism.
    let fromContext (f: 'ctx -> 't) (options: RemotingOptions<'ctx, 't>) = 
        { options with Implementation = FromContext f }

    /// Builds the API using the provided static protocol implementation 
    let fromValue (serverImpl: 'implementation) (options: RemotingOptions<'t, 'implementation>)  = 
        { options with Implementation = StaticValue serverImpl }