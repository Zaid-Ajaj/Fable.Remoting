namespace Fable.Remoting.Client

open Fable.Core
open Fable.SimpleJson
open System
open Microsoft.FSharp.Reflection
open Fable.Remoting

module Remoting =
    /// Starts with default configuration for building a proxy
    let createApi() = {
        CustomHeaders = [ ]
        BaseUrl = None
        Authorization = None
        WithCredentials = false
        RouteBuilder = sprintf ("/%s/%s")
        CustomResponseSerialization = None
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

    /// Sets the withCredentials option on the XHR request, which is useful for CORS scenarios
    let withCredentials withCredentials (options: RemoteBuilderOptions) =
        { options with WithCredentials = withCredentials }

    /// Specifies that the API uses binary serialization for responses
    let withBinarySerialization (options: RemoteBuilderOptions) =
        let serializer response returnType = MsgPack.Read.Reader(response).Read returnType
        { options with CustomResponseSerialization = Some serializer }

type Remoting() =
    static member buildProxy<'t>(options: RemoteBuilderOptions, [<Inject>] ?resolver: ITypeResolver<'t>) : 't =
        let resolvedType = resolver.Value.ResolveType()
        let schemaType = createTypeInfo resolvedType
        match schemaType with
        | TypeInfo.Record getFields ->
            let (fields, recordType) = getFields()
            let fieldTypes = Reflection.FSharpType.GetRecordFields recordType |> Array.map (fun prop -> prop.Name, prop.PropertyType)
            let recordFields = [|
                for field in fields do
                    let normalize n =
                        let fieldType = fieldTypes |> Array.pick (fun (name, typ) -> if name = field.FieldName then Some typ else None)
                        let fn = Proxy.proxyFetch options recordType.Name field fieldType
                        match n with
                        | 0 -> box (fn null null null null null null null null)
                        | 1 -> box (fun a ->
                            fn a null null null null null null null)
                        | 2 ->
                            let proxyF a b = fn a b null null null null null null
                            unbox (System.Func<_,_,_> proxyF)
                        | 3 ->
                            let proxyF a b c = fn a b c null null null null null
                            unbox (System.Func<_,_,_,_> proxyF)
                        | 4 ->
                            let proxyF a b c d = fn a b c d null null null null
                            unbox (System.Func<_,_,_,_,_> proxyF)
                        | 5 ->
                            let proxyF a b c d e = fn a b c d e null null null
                            unbox (System.Func<_,_,_,_,_,_> proxyF)
                        | 6 ->
                            let proxyF a b c d e f = fn a b c d e f null null
                            unbox (System.Func<_,_,_,_,_,_,_> proxyF)
                        | 7 ->
                            let proxyF a b c d e f g = fn a b c d e f g null
                            unbox (System.Func<_,_,_,_,_,_,_,_> proxyF)
                        | 8 ->
                            let proxyF a b c d e f g h = fn a b c d e f g h
                            unbox (System.Func<_,_,_,_,_,_,_,_,_> proxyF)
                        | _ ->
                            failwithf "Cannot generate proxy function for %s. Only up to 8 arguments are supported. Consider using a record type as input" field.FieldName

                    let argumentCount =
                        match field.FieldType with
                        | TypeInfo.Async _  -> 0
                        | TypeInfo.Promise _  -> 0
                        | TypeInfo.Func getArgs -> Array.length (getArgs()) - 1
                        | _ -> 0

                    normalize argumentCount
                |]

            let proxy = FSharpValue.MakeRecord(recordType, recordFields)
            unbox proxy
        | _ ->
            failwithf "Cannot build proxy. Exepected type %s to be a valid protocol definition which is a record of functions" resolvedType.FullName

