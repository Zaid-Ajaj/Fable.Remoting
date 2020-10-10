namespace Fable.Remoting.DotnetClient

open FSharp.Core.OptimizedClosures
open System
open System.Net.Http
open System.Reflection
open Microsoft.FSharp.Reflection

module Remoting =

    type private ParameterlessServiceCall<'a>() =
        static member _Invoke(route: string, client: HttpClient, isBinarySerialization) : Async<'a> =
            Proxy.proxyPost<'a> [] route client isBinarySerialization

    type private ServiceCallerFunc2<'a, 'b>(route: string, client: HttpClient, isBinarySerialization) =
        inherit FSharpFunc<'a, Async<'b>>()

        override _.Invoke(a) =
            Proxy.proxyPost<'b> [ box a ] route client isBinarySerialization

    type private ServiceCallerFunc3<'a, 'b, 'c>(route: string, client: HttpClient, isBinarySerialization) =
        inherit FSharpFunc<'a, 'b, Async<'c>>()

        override _.Invoke a =
            fun b -> Proxy.proxyPost<'c> [ box a; box b ] route client isBinarySerialization

        override _.Invoke(a, b) =
            Proxy.proxyPost<'c> [ box a; box b ] route client isBinarySerialization

    type private ServiceCallerFunc4<'a, 'b, 'c, 'd>(route: string, client: HttpClient, isBinarySerialization) =
        inherit FSharpFunc<'a, 'b, 'c, Async<'d>>()

        override _.Invoke a =
            fun b c -> Proxy.proxyPost<'d> [ box a; box b; box c ] route client isBinarySerialization

        override _.Invoke(a, b, c) =
            Proxy.proxyPost<'d> [ box a; box b; box c ] route client isBinarySerialization

    type private ServiceCallerFunc5<'a, 'b, 'c, 'd, 'e>(route: string, client: HttpClient, isBinarySerialization) =
        inherit FSharpFunc<'a, 'b, 'c, 'd, Async<'e>>()

        override _.Invoke a =
            fun b c d -> Proxy.proxyPost<'e> [ box a; box b; box c; box d ] route client isBinarySerialization

        override _.Invoke(a, b, c, d) =
            Proxy.proxyPost<'e> [ box a; box b; box c; box d ] route client isBinarySerialization

    type private ServiceCallerFunc6<'a, 'b, 'c, 'd, 'e, 'f>(route: string, client: HttpClient, isBinarySerialization) =
        inherit FSharpFunc<'a, 'b, 'c, 'd, 'e, Async<'f>>()

        override _.Invoke a =
            fun b c d e -> Proxy.proxyPost<'f> [ box a; box b; box c; box d; box e ] route client isBinarySerialization

        override _.Invoke(a, b, c, d, e) =
            Proxy.proxyPost<'f> [ box a; box b; box c; box d; box e ] route client isBinarySerialization

    type private ServiceCallerFunc7<'a, 'b, 'c, 'd, 'e, 'f, 'g>(route: string, client: HttpClient, isBinarySerialization) =
        inherit FSharpFunc<'a, 'b, 'c, 'd, 'e, FSharpFunc<'f, Async<'g>>>()

        override _.Invoke a =
            fun b c d e f -> Proxy.proxyPost<'g> [ box a; box b; box c; box d; box e; box f ] route client isBinarySerialization

        override _.Invoke(a, b, c, d, e) =
            fun f -> Proxy.proxyPost<'g> [ box a; box b; box c; box d; box e; box f ] route client isBinarySerialization

    type private ServiceCallerFunc8<'a, 'b, 'c, 'd, 'e, 'f, 'g, 'h>(route: string, client: HttpClient, isBinarySerialization) =
        inherit FSharpFunc<'a, 'b, 'c, 'd, 'e, FSharpFunc<'f, FSharpFunc<'g, Async<'h>>>>() // the compiler will optimize `fun f g -> ...` to FSharpFunc<'f, 'g, 'h>

        override _.Invoke a =
            fun b c d e f g -> Proxy.proxyPost<'h> [ box a; box b; box c; box d; box e; box f; box g ] route client isBinarySerialization

        override _.Invoke(a, b, c, d, e) =
            fun f g -> Proxy.proxyPost<'h> [ box a; box b; box c; box d; box e; box f; box g ] route client isBinarySerialization

    type private ServiceCallerFunc9<'a, 'b, 'c, 'd, 'e, 'f, 'g, 'h, 'i>(route: string, client: HttpClient, isBinarySerialization) =
        inherit FSharpFunc<'a, 'b, 'c, 'd, 'e, FSharpFunc<'f, FSharpFunc<'g, FSharpFunc<'h, Async<'i>>>>>()

        override _.Invoke a =
            fun b c d e f g h -> Proxy.proxyPost<'i> [ box a; box b; box c; box d; box e; box f; box g; box h ] route client isBinarySerialization

        override _.Invoke(a, b, c, d, e) =
            fun f g h -> Proxy.proxyPost<'i> [ box a; box b; box c; box d; box e; box f; box g; box h ] route client isBinarySerialization

    type RemoteBuilderOptions = {
        RouteBuilder: (string -> string -> string) option
        BaseUri: Uri
        Client: HttpClient option
        AuthorizationToken: string option
        IsBinarySerialization: bool
        CustomHeaders: (string * string) list
    }

    /// <summary>
    /// Creates the initial configration for building a proxy using the base url of the backend
    /// </summary>
    let createApi (baseUrl: string) =
        {
            RouteBuilder = None
            BaseUri = Uri(baseUrl)
            Client = None
            AuthorizationToken = None
            IsBinarySerialization = false
            CustomHeaders = []
        }

    let withRouteBuilder (routeBuilder: string -> string -> string) options = { options with RouteBuilder = Some routeBuilder }

    /// <summary>
    /// Appends an Authorization header in the HttpClient used by the generated proxy
    /// </summary>
    let withAuthorizationHeader token options = { options with AuthorizationToken = Some token }

    /// <summary>
    /// Enables the binary serialization protocol which uses the binary msgpack format for data transport instead of Json
    /// </summary>
    let withBinarySerialization options = { options with IsBinarySerialization = true }

    /// <summary>
    /// Overrides the HttpClient client used by the generated proxy
    /// </summary>
    let withHttpClient client options = { options with Client = Some client }

    /// <summary>
    /// Adds custom headers for each request send by the HttpClient of the generated proxy
    /// </summary>
    let withCustomHeaders (headers: (string * string) list) options = { options with CustomHeaders = headers @ options.CustomHeaders }

    /// <summary>
    /// Generates an instance of the protocol F# record using the provided options
    /// </summary>
    let buildProxy<'t> (options: RemoteBuilderOptions) : 't =
        let t = typeof<'t>
        if not <| FSharpType.IsRecord t then failwithf "Type %s is not an F# record type" t.Name

        let client = defaultArg options.Client (new HttpClient())
        let routeBuilder = defaultArg options.RouteBuilder (fun x y -> sprintf "%s/%s" x y)

        options.CustomHeaders |> List.iter (fun (name, value) -> client.DefaultRequestHeaders.Add(name, value))

        match options.AuthorizationToken with
        | Some token -> client.DefaultRequestHeaders.Add("Authorization", token)
        | _ -> ()

        let parameters =
            FSharpType.GetRecordFields t
            |> Array.map (fun param ->
                let funcType = param.PropertyType
                let argTypes =
                    Some funcType |> Seq.unfold (fun t ->
                        match t with
                        | Some t ->
                            let generic = t.GetGenericTypeDefinition()
                            if generic = typedefof<FSharpFunc<_,_>> then
                                let args = t.GetGenericArguments()
                                Some (args.[0], Some args.[1])
                            elif generic = typedefof<Async<_>> then
                                Some (t.GetGenericArguments().[0], None)
                            else
                                failwithf "Bad API record field %s, must be of type Async<'a> or a function returning Async<'a>" param.Name
                        | None -> None
                    )
                    |> Seq.toArray

                let route = Uri(options.BaseUri, (routeBuilder t.Name param.Name).TrimStart('/')) |> string
                if Array.length argTypes = 1 then
                    typedefof<ParameterlessServiceCall<_>>
                        .MakeGenericType(argTypes.[0])
                        .GetMethod("_Invoke", BindingFlags.NonPublic ||| BindingFlags.Static)
                        .Invoke(null, [| route; client; options.IsBinarySerialization |])
                else
                    let callerType =
                        match Array.length argTypes with
                        | 2 -> typedefof<ServiceCallerFunc2<_,_>>.MakeGenericType(argTypes)
                        | 3 -> typedefof<ServiceCallerFunc3<_,_,_>>.MakeGenericType(argTypes)
                        | 4 -> typedefof<ServiceCallerFunc4<_,_,_,_>>.MakeGenericType(argTypes)
                        | 5 -> typedefof<ServiceCallerFunc5<_,_,_,_,_>>.MakeGenericType(argTypes)
                        | 6 -> typedefof<ServiceCallerFunc6<_,_,_,_,_,_>>.MakeGenericType(argTypes)
                        | 7 -> typedefof<ServiceCallerFunc7<_,_,_,_,_,_,_>>.MakeGenericType(argTypes)
                        | 8 -> typedefof<ServiceCallerFunc8<_,_,_,_,_,_,_,_>>.MakeGenericType(argTypes)
                        | 9 -> typedefof<ServiceCallerFunc9<_,_,_,_,_,_,_,_,_>>.MakeGenericType(argTypes)
                        | _ -> failwith "RPC methods with at most 8 curried arguments are supported"

                    Activator.CreateInstance(callerType, route, client, options.IsBinarySerialization)
            )

        FSharpValue.MakeRecord(t, parameters) :?> 't
