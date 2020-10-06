namespace Fable.Remoting.DotnetClient

open FSharp.Core.OptimizedClosures
open System
open System.Net.Http
open System.Reflection

module ClientRemoting =

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

    type RemoteBuilderOptions = private {
        routeBuilder: string -> string -> string
        baseUri: Uri
        client: HttpClient option
        authToken: string option
        isBinarySerialization: bool
        customHeaders: (string * string) list
    }

    // Warning: if routeBuilder returns a path with a leading /, it will override any subpath inside baseUrl
    let createApi (baseUrl: string) (routeBuilder: string -> string -> string) =
        {
            routeBuilder = routeBuilder
            baseUri = Uri(baseUrl)
            client = None
            authToken = None
            isBinarySerialization = false
            customHeaders = []
        }

    let withAuthorizationHeader token options = { options with authToken = Some token }

    let withBinarySerialization options = { options with isBinarySerialization = true }

    let withHttpClient client options = { options with client = Some client }

    let withCustomHeader (headers: (string * string) list) options = { options with customHeaders = headers @ options.customHeaders }

    let buildProxy<'t> (options: RemoteBuilderOptions) : 't =
        let isFSharpRecordType (t: Type) =
            match t.GetCustomAttributes<CompilationMappingAttribute>() |> Seq.toList with
            | [ attr ] -> attr.SourceConstructFlags.HasFlag SourceConstructFlags.RecordType
            | _ -> false

        let t = typeof<'t>
        if not <| isFSharpRecordType t then failwithf "Type %s is not an F# record type" t.Name

        let client = defaultArg options.client (new HttpClient())

        options.customHeaders |> List.iter (fun (name, value) -> client.DefaultRequestHeaders.Add(name, value))

        match options.authToken with
        | Some token -> client.DefaultRequestHeaders.Add("Authorization", token)
        | _ -> ()

        let ctor = t.GetConstructors() |> Seq.exactlyOne
        let parameters =
            ctor.GetParameters()
            |> Array.map (fun param ->
                let funcType = param.ParameterType
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

                let route = Uri(options.baseUri, options.routeBuilder t.Name param.Name) |> string
                if Array.length argTypes = 1 then
                    typedefof<ParameterlessServiceCall<_>>
                        .MakeGenericType(argTypes.[0])
                        .GetMethod("_Invoke", BindingFlags.NonPublic ||| BindingFlags.Static)
                        .Invoke(null, [| route; client; options.authToken; options.isBinarySerialization |])
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

                    Activator.CreateInstance(callerType, route, client, options.authToken, options.isBinarySerialization)
            )

        ctor.Invoke(parameters) :?> 't
