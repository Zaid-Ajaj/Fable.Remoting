namespace Fable.Remoting.Client

open Fable.Core
open Fable.SimpleJson
open System
open Microsoft.FSharp.Reflection

module Remoting =
    /// Starts with default configuration for building a proxy
    let createApi() = {
        CustomHeaders = [ ]
        BaseUrl = None
        Authorization = None
        RouteBuilder = sprintf ("/%s/%s")
        ResponseSerialization = Json
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

    /// Specifies that the API uses binary serialization for responses
    let withBinarySerialization (options: RemoteBuilderOptions) =
        { options with ResponseSerialization = MessagePack }

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
                        | 4 -> box (fun a b c d ->
                            let args = Proxy.arguments()
                            fn a b c d null null null null)
                        | 5 -> box (fun a b c d e ->
                            let args = Proxy.arguments()
                            fn a b c d e null null null)
                        | 6 -> box (fun a b c d e f ->
                            let args = Proxy.arguments()
                            fn a b c d e f null null)
                        | 7 -> box (fun a ->
                            let args = Proxy.arguments()
                            fn args.[0] args.[1] args.[2] args.[3] args.[4] args.[5] args.[6] null)
                        | 8 -> box (fun a ->
                            let args = Proxy.arguments()
                            fn args.[0] args.[1] args.[2] args.[3] args.[4] args.[5] args.[6] args.[7])
                        | _ ->
                            failwith "Only up to 8 arguments are supported"

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
            failwithf "Exepected type %s to be a valid protocol definition" resolvedType.FullName

