namespace Fable.Remoting.Server

module Remoting = 
    
    let documentation (name: string) (routes: RouteDocs list) : Documentation = Documentation (name, routes)

    /// Starts with the default configuration for building an API 
    let createApi()  = 
        { Implementation = Empty 
          RouteBuilder = sprintf "/%s/%s" 
          ErrorHandler = None 
          DiagnosticsLogger = None
          Docs = None, None }

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

    /// Builds the API using the provided static protocol implementation 
    let fromValue (serverImpl: 'implementation) (options: RemotingOptions<'t, 'implementation>)  = 
        DynamicRecord.checkProtocolDefinition serverImpl
        { options with Implementation = StaticValue serverImpl }



module Other = 
    type Number = { value: int; factor: int }
    
    type IServerApi = { 
        getLength: string -> Async<int>
        simpleAsync: Async<string> 
        multiply: Number -> Async<int>
    } 
    let docs = Docs.createFor<IServerApi>()

    let serverApiDocs = 
        Remoting.documentation "Server Api" [
            docs.route <@ fun api -> api.getLength @>
            |> docs.alias "Get Length"
            |> docs.description "Returns the length of the input string"

            docs.route <@ fun api -> api.multiply @>
            |> docs.alias "Multiply"
            |> docs.description "Multiplyes the input value times the factor"
            |> docs.example <@ fun api -> api.multiply { value = 10; factor = 2  } @>
            |> docs.example <@ fun api -> api.multiply { value = 40; factor = -3 } @>

            docs.route <@ fun api -> api.simpleAsync @>
            |> docs.alias "Simple Async"
            |> docs.description "Returns a static string value"
        ]