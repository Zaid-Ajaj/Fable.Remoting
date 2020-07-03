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
          ResponseSerialization = Json }

    /// Defines how routes are built using the type name and method name. By default, the generated routes are of the form `/typeName/methodName`.
    let withRouteBuilder builder options = 
        { options with RouteBuilder = builder } 

    /// Enables the diagnostics logger that will log what steps the library is taking when a request comes in. This could help troubleshoot serialization issues but it could be also be interesting to see what is going on under the hood.
    let withDiagnosticsLogger logger options = 
        { options with DiagnosticsLogger = Some logger }

    /// Enables the automatic generation of API documentation based on type-metadata 
    let withDocs (url: string) (docs: Documentation) options = 
        { options with Docs = Some url, Some docs }

    /// Ennables you to define a custom error handler for unhandled exceptions thrown by your remote functions. It can also be used for logging purposes or if you wanted to propagate errors back to client.
    let withErrorHandler handler options = 
        { options with ErrorHandler = Some handler }

    /// Specifies that the API only uses binary serialization
    let withBinarySerialization options = 
        { options with ResponseSerialization = MessagePack }

    /// Builds the API using the provided static protocol implementation 
    let fromValue (serverImpl: 'implementation) (options: RemotingOptions<'t, 'implementation>)  = 
        DynamicRecord.checkProtocolDefinition serverImpl
        { options with Implementation = StaticValue serverImpl }