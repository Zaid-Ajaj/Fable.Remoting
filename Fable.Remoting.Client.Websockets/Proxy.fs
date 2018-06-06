namespace Fable.Remoting.Client

open FSharp.Reflection
open Fable.PowerPack
open Fable.Core
open Fable.Core.JsInterop

module Proxy =

    type Result = {
        Id: int
        Result: obj
    }

    type Request = {
        Id: int
        Method: string
        Arguments: obj array
    }

    exception SocketException of string

    type SocketMessages =
        | Call of method:string*args:obj array*channel:AsyncReplyChannel<Result<obj,exn>>
        | Response of Result
        | Disconnected

    type SocketBuilderOptions =
        {
           Endpoint               : string option
           Builder                : (string -> string)
        }
        with
            static member Empty =  {
                   Endpoint              = None
                   Builder               = sprintf "/%s"
                }
    [<Emit("$2[$0] = $1")>]
    let private setProp (propName: string) (propValue: obj) (any: obj) : unit = jsNative

    [<Emit("$0")>]
    let private typed<'a> (x: obj) : 'a = jsNative

    [<Emit("$0[$1]")>]
    let private getAs<'a> (x: obj) (key: string) : 'a = jsNative
    [<Emit("JSON.parse($0)")>]
    let private jsonParse (content: string) : obj = jsNative
    [<Emit("JSON.stringify($0)")>]
    let private stringify (x: obj) : string = jsNative
    [<PassGenerics>]
    let private fields<'t> =
        FSharpType.GetRecordFields typeof<'t>
        |> Seq.choose
            (fun propInfo ->
                match propInfo.PropertyType with
                |t when FSharpType.IsFunction t ->
                    let funcName = propInfo.Name
                    let funcParamterTypes =
                        FSharpType.GetFunctionElements (propInfo.PropertyType)
                        |> typed<System.Type []>
                    Some (funcName, funcParamterTypes)
                |t when box (t?definition?name) = box (typeof<Async<_>>?definition?name) ->
                    Some(propInfo.Name, [|t|])
                |_ -> None
        )
        |> List.ofSeq

    let private proxyFetch (inbox:MailboxProcessor<SocketMessages>) methodName typeCount =

        fun arg0 arg1 arg2 arg3 arg4 arg5 arg6 arg7 arg8 arg9 arg10 arg11 arg12 arg13 arg14 arg15 ->
            let data =
               [| box arg0;box arg1;box arg2;box arg3;box arg4;box arg5;box arg6;box arg7;box arg8;box arg9;box arg10;box arg11;box arg12;box arg13;box arg14;box arg15 |].[0..typeCount-1]

            async {
                let! msg = inbox.PostAndAsyncReply(fun rc -> Call(methodName,data,rc))
                match msg with
                |Ok v -> return v
                |Error exn ->
                    return! raise exn}
    type SocketBuilder<'a>() =
            member __.Yield(_) =
                SocketBuilderOptions.Empty
            /// Enables empty computation expression
            member __.Zero() =
                SocketBuilderOptions.Empty

            [<PassGenerics>]
            member __.Run(options) : 't =
                let typeName = typeof<'t>.Name
                let route = options.Builder typeName
                let server =
                    match options.Endpoint with
                    | Some path ->
                        if path.EndsWith("/")
                        then sprintf "%s%s" path route
                        else sprintf "%s/%s" path route
                    | None -> route

                let url = Fable.Import.Browser.URL.Create(Fable.Import.Browser.window.location.href)
                url.protocol <- url.protocol.Replace ("http","ws")
                url.pathname <- server
                let ws :Fable.Import.Browser.WebSocket option ref = ref None
                let inbox = MailboxProcessor.Start(fun (mb:MailboxProcessor<_>) ->
                    let rec loop n (requests:Map<int,AsyncReplyChannel<Result<obj,exn>>>) =
                        async {
                            let! msg = mb.Receive()
                            match msg with
                            |Disconnected ->
                                let msg = SocketException("Connection was lost")
                                requests |> Map.iter (fun _ c -> c.Reply(Error msg))
                                return! loop 0 Map.empty
                            |Call (meth,args,rc) ->
                                match !ws with
                                |Some s ->
                                    let req = {Id=n;Method=meth;Arguments=args}
                                    s.send(JsInterop.toJson req)
                                    return! loop (n+1) (requests |> Map.add n rc)
                                |None ->
                                    rc.Reply(Error(SocketException "No connection"))
                                    return! loop n requests
                            |Response {Id=id;Result=res} ->
                                let rc = requests |> Map.tryFind id
                                match rc with
                                |Some c ->
                                    c.Reply (Ok res)
                                |None -> ()
                                return! loop n (requests |> Map.remove id)
                        }
                    loop 0 Map.empty
                )
                let rec websocket server r =
                    let ws = Fable.Import.Browser.WebSocket.Create server
                    r := Some ws
                    ws.onclose <- fun _ ->
                        r := None
                        inbox.Post Disconnected
                        Fable.Import.Browser.window.setTimeout(websocket server r, 1000) |> ignore
                    ws.onmessage <- fun e ->
                        e.data |> string |> JsInterop.ofJson |> Response |> inbox.Post
                websocket url.href ws
                // create an empty object literal
                let proxy = obj()
                let fields = fields<'t>
                fields |> List.iter (fun field ->
                    let funcTypes = snd field
                    // Async<T>
                    let asyncOfreturnType = funcTypes |> Array.last
                    // T
                    let returnType = asyncOfreturnType.GenericTypeArguments.[0]
                    let fieldName = fst field
                    let normalize n =
                        let fn = proxyFetch inbox fieldName n
                        match n with
                        |0 ->
                            box (fn null null null null null null null null null null null null null null null null)
                        |1 ->
                            box (fun a -> fn a null null null null null null null null null null null null null null null)
                        |2 ->
                            box (fun a b -> fn a b null null null null null null null null null null null null null null)
                        |3 ->
                            box (fun a b c -> fn a b c null null null null null null null null null null null null null)
                        |4 ->
                            box (fun a b c d -> fn a b c d null null null null null null null null null null null null)
                        |5 ->
                            box (fun a b c d e -> fn a b c d e null null null null null null null null null null null)
                        |6 ->
                            box (fun a b c d e f -> fn a b c d e f null null null null null null null null null null)
                        |7 ->
                            box (fun a b c d e f g -> fn a b c d e f g null null null null null null null null null)
                        |8 ->
                            box (fun a b c d e f g h -> fn a b c d e f g h null null null null null null null null)
                        |9 ->
                            box (fun a b c d e f g h i -> fn a b c d e f g h i null null null null null null null)
                        |10 ->
                            box (fun a b c d e f g h i j -> fn a b c d e f g h i j null null null null null null)
                        |11 ->
                            box (fun a b c d e f g h i j k -> fn a b c d e f g h i j k null null null null null)
                        |12 ->
                            box (fun a b c d e f g h i j k l -> fn a b c d e f g h i j k l null null null null)
                        |13 ->
                            box (fun a b c d e f g h i j k l m -> fn a b c d e f g h i j k l m null null null)
                        |14 ->
                            box (fun a b c d e f g h i j k l m n -> fn a b c d e f g h i j k l m n null null)
                        |15 ->
                            box (fun a b c d e f g h i j k l m n o -> fn a b c d e f g h i j k l m n o null)
                        |16 ->
                            box fn
                        |_ -> failwith "Only up to 16 arguments are supported"
                    setProp fieldName (normalize (funcTypes.Length - 1)) proxy
                )
                unbox proxy
            /// Pins the proxy at an endpoint
            [<CustomOperation("at_endpoint")>]
            member __.AtEndpoint(state,endpoint) =
                {state with Endpoint = Some endpoint}
            /// Alias for `use_route_builder`. Uses a custom route builder. By default, the route paths have the form `/{typeName}` when you use a custom route builder, you override this behaviour. A custom route builder is a function of type `typeName:string -> string`.
            [<CustomOperation("with_builder")>]
            member __.WithBuilder(state,builder) =
                {state with Builder = builder}
            /// Uses a custom route builder. By default, the route paths have the form `/{typeName}` when you use a custom route builder, you override this behaviour. A custom route builder is a function of type `typeName:string -> string`.
            [<CustomOperation("use_route_builder")>]
            member __.UseRouteBuilder(state,builder) =
                {state with Builder = builder}

    /// Computation expression to create a remoting proxy via websockets.
    /// Usage:
    /// `let proxy : IType = remoting {()}` for default options at /typeName
    /// `let proxy : IType = remoting {`
    /// `    with_builder builder` to set a `builder : (string -> string)`
    /// `}`
    [<PassGenerics>]
    let remoting<'t> = SocketBuilder()