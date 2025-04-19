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
        CustomRequestSerialization = None
        IsMultipartEnabled = false
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

    /// Specifies that the API uses binary serialization for requests
    let withBinaryRequestSerialization (options: RemoteBuilderOptions) =
        let serializer (requestArgs: obj[]) (requestTypes: Type[]) =
            let out = ResizeArray ()

            if requestArgs.Length = 1 then
                MsgPack.Write.Fable.writeObject requestArgs.[0] requestTypes.[0] out
            else
                MsgPack.Write.Fable.writeArrayHeader requestArgs.Length out

                for i in 0 .. requestArgs.Length - 1 do
                    MsgPack.Write.Fable.writeObject requestArgs.[i] requestTypes.[i] out

            out

        { options with CustomRequestSerialization = Some serializer }

    /// Enables top level byte array parameters (such as in `upload: Metadata -> byte[] -> Async<UploadResult>`) to be sent with minimal overhead using multipart/form-data.
    ///
    /// !!! Fable.Remoting.Suave servers do not support this option.
    let withMultipartOptimization options = { options with IsMultipartEnabled = true }

type Remoting() =
    /// For internal library use only.
    static member buildProxy(options: RemoteBuilderOptions, resolvedType: Type) =
        if Reflection.FSharpType.IsRecord resolvedType then
            let recordFields =
                Reflection.FSharpType.GetRecordFields resolvedType
                |> Array.map (fun prop ->
                    let fieldTypeInfo = createTypeInfo prop.PropertyType
                    let fn = Proxy.proxyFetch options resolvedType.Name { PropertyInfo = prop; FieldName = prop.Name; FieldType = fieldTypeInfo } prop.PropertyType

                    let argumentCount =
                        match fieldTypeInfo with
                        | TypeInfo.Func getArgs -> Array.length (getArgs()) - 1
                        | _ -> 0

                    match argumentCount with
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
                        failwithf "Cannot generate proxy function for %s. Only up to 8 arguments are supported. Consider using a record type as input" prop.Name
                )

            FSharpValue.MakeRecord(resolvedType, recordFields) |> unbox
        else
            failwithf "Cannot build proxy. Exepected type %s to be a valid protocol definition which is a record of functions" resolvedType.FullName

    static member inline buildProxy<'t>(options: RemoteBuilderOptions) : 't =
        Remoting.buildProxy(options, typeof<'t>)
