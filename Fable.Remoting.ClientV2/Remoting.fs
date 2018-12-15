namespace Fable.Remoting.Client 

open Fable.Core
open Fable.SimpleJson

module Remoting = 
    /// Starts with default configuration for building a proxy
    let createApi() = {
        CustomHeaders = [ ]
        BaseUrl = None
        Authorization = None
        AuthorizationResolve = None
        RouteBuilder = sprintf ("/%s/%s") 
    }
    
    /// Defines how routes are built using the type name and method name. By default, the generated routes are of the form `/typeName/methodName`.
    let withRouteBuilder builder (options: RemoteBuilderOptions) = 
        { options with RouteBuilder = builder }

    /// Sets the base url for the request. Useful if you are making cross-domain requests 
    let withBaseUrl url (options: RemoteBuilderOptions) = 
        { options with BaseUrl = Some url }

    /// Adds custom headers to each request of the proxy
    let withCustomHeader headers (options: RemoteBuilderOptions) = 
        { options with CustomHeaders = headers }

    /// Sets the authorization header of every request from the proxy
    let withAuthorizationHeader token (options: RemoteBuilderOptions) = 
        { options with Authorization = Some token }

    /// Sets the resolve function for authorization header of every request from the proxy
    let withAuthorizationHeaderResolver resolveFun (options: RemoteBuilderOptions) =
        { options with AuthorizationResolve = Some resolveFun }

type Remoting() = 
    static member buildProxy<'t>(options: RemoteBuilderOptions, [<Inject>] ?resolver: ITypeResolver<'t>) : 't = 
        let resolvedType = resolver.Value.ResolveType()
        let schemaType = Converter.createTypeInfo resolvedType
        match schemaType with 
        | TypeInfo.Record getFields ->
            let (fields, recordType) = getFields() 
            let proxy = obj() 
            for field in fields do 
                let normalize n =
                    let fn = Proxy.proxyFetch options recordType.Name field
                    match n with
                    | 0 -> box (fn null null null null null null null null)
                    | 1 -> box (fun a -> 
                        let args = Proxy.arguments()
                        fn args.[0] null null null null null null null)
                    | 2 -> box (fun a -> 
                        let args = Proxy.arguments()
                        fn args.[0] args.[1] null null null null null null)
                    | 3 -> box (fun a ->  
                        let args = Proxy.arguments()
                        fn args.[0] args.[1] args.[2] null null null null null)
                    | 4 -> box (fun a ->  
                        let args = Proxy.arguments()
                        fn args.[0] args.[1] args.[2] args.[3] null null null null)
                    | 5 -> box (fun a -> 
                        let args = Proxy.arguments()
                        fn args.[0] args.[1] args.[2] args.[3] args.[4] null null null)
                    | 6 -> box (fun a -> 
                        let args = Proxy.arguments()
                        fn args.[0] args.[1] args.[2] args.[3] args.[4] args.[5] null null)
                    | 7 -> box (fun a -> 
                        let args = Proxy.arguments()
                        fn args.[0] args.[1] args.[2] args.[3] args.[4] args.[5] args.[6] null)
                    | 8 -> box (fun a -> 
                        let args = Proxy.arguments()
                        fn args.[0] args.[1] args.[2] args.[3] args.[4] args.[5] args.[6] args.[7])
                    | _ -> failwith "Only up to 8 arguments are supported" 
                let argumentCount = 
                    match field.FieldType with 
                    | TypeInfo.Async _  -> 0 
                    | TypeInfo.Promise _  -> 0 
                    | TypeInfo.Func getArgs -> Array.length (getArgs()) - 1
                    | _ -> 0 
                let normalizedProxyFetch = normalize argumentCount
                Proxy.setProp field.FieldName normalizedProxyFetch proxy
            unbox proxy
        | _ -> failwithf "Exepected type %s to be a valid protocol definition" resolvedType.FullName 

