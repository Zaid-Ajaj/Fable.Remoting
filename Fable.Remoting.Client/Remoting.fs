namespace Fable.Remoting.Client 

open Fable.Core

module Remoting = 
    /// Starts with default configuration for building a proxy
    let createApi() = {
        CustomHeaders = [ ]
        BaseUrl = None
        Authorization = None
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
 
    [<PassGenerics>]
    let buildProxy<'t> (options: RemoteBuilderOptions) : 't = 
        // create an empty object literal
        let proxy = obj()
        let typeInfo = typeof<'t>
        let typeName = typeInfo.Name
        let recordFunctions = Proxy.recordFieldsAsFunctions<'t>()
        recordFunctions |> List.iter (fun func ->
            let normalize n =
                let fn = Proxy.proxyFetch options typeName func
                match n with
                | 0 -> box (fn null null null null null null null null null null null null null null null null)
                | 1 -> box (fun a -> fn a null null null null null null null null null null null null null null null)
                | 2 -> box (fun a b -> fn a b null null null null null null null null null null null null null null)
                | 3 -> box (fun a b c -> fn a b c null null null null null null null null null null null null null)
                | 4 -> box (fun a b c d -> fn a b c d null null null null null null null null null null null null)
                | 5 -> box (fun a b c d e -> fn a b c d e null null null null null null null null null null null)
                | 6 -> box (fun a b c d e f -> fn a b c d e f null null null null null null null null null null)
                | 7 -> box (fun a b c d e f g -> fn a b c d e f g null null null null null null null null null)
                | 8 -> box (fun a b c d e f g h -> fn a b c d e f g h null null null null null null null null)
                | 9 -> box (fun a b c d e f g h i -> fn a b c d e f g h i null null null null null null null)
                | 10 -> box (fun a b c d e f g h i j -> fn a b c d e f g h i j null null null null null null)
                | 11 -> box (fun a b c d e f g h i j k -> fn a b c d e f g h i j k null null null null null)
                | 12 -> box (fun a b c d e f g h i j k l -> fn a b c d e f g h i j k l null null null null)
                | 13 -> box (fun a b c d e f g h i j k l m -> fn a b c d e f g h i j k l m null null null)
                | 14 -> box (fun a b c d e f g h i j k l m n -> fn a b c d e f g h i j k l m n null null)
                | 15 -> box (fun a b c d e f g h i j k l m n o -> fn a b c d e f g h i j k l m n o null)
                | 16 -> box fn
                | _ -> failwith "Only up to 16 arguments are supported"
            Proxy.setProp func.Name (normalize (Proxy.getArgumentCount func.Type)) proxy)
        unbox proxy 