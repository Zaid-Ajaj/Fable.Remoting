namespace Fable.Remoting.Giraffe

open FSharp.Reflection
open Microsoft.AspNetCore.Http

open Giraffe
open System.IO
open System.Text

open Fable.Remoting.Server
open Fable.Remoting.Server.SharedCE

[<AutoOpen>]
module FableGiraffeAdapter =
  /// Legacy logger for backward compatibility
  let mutable logger : (string -> unit) option = None
  /// Legacy ErrorHandler for backward compatibility

  let mutable private onErrorHandler : ErrorHandler<HttpContext> option = None

  /// Global error handler that intercepts server errors and decides whether or not to propagate a message back to the client for backward compatibility
  let onError (handler: ErrorHandler<HttpContext>) =
        onErrorHandler <- Some handler

  type RemoteBuilder(implementation)=
   inherit RemoteBuilderBase<HttpContext,HttpHandler>()

   override builder.Run(options:SharedCE.BuilderOptions<HttpContext>) =

    // Get data from request body and deserialize.
    // getResourceFromReq : HttpRequest -> obj
    let getResourceFromReq (ctx : HttpContext) (inputType: System.Type[]) (genericType: System.Type[]) =
        let requestBodyStream = ctx.Request.Body
        use streamReader = new StreamReader(requestBodyStream)
        let requestBodyContent = streamReader.ReadToEnd()
        builder.Deserialize options requestBodyContent inputType ctx genericType

    let handleRequest methodName serverImplementation genericType routePath =
        let inputType = ServerSide.getInputType methodName serverImplementation
        let hasArg =
            match inputType with
            |[|inputType;_|] when inputType.FullName = "Microsoft.FSharp.Core.Unit" -> false
            |_ -> true
        fun (next : HttpFunc) (ctx : HttpContext) ->
          let handlerOverride =
            options.CustomHandlers |> Map.tryFind methodName |> Option.map (fun f ->
                    Option.iter (fun logf -> logf (sprintf "Fable.Remoting: Invoking custom handler for method %s" methodName)) options.Logger
                    f ctx) |> Option.flatten
          let (statusCodeOverride, bodyOverride, headersOverride, abort) =
                match handlerOverride with
                |Some ({StatusCode = sc; Body = b; Headers = hd; Abort = abort} as overrides) ->
                    Option.iter (fun logf -> logf (sprintf "Fable.Remoting: Overrides: %0A" overrides)) options.Logger
                    (sc,b,hd,abort)
                |None -> (None, None, None, false)
          if abort then
            task {return None}
          else  
              match bodyOverride with
              |Some b ->
                    task {
                        ctx.Response.StatusCode <-
                            match statusCodeOverride with
                            |None -> 200
                            |Some statusCode ->
                                Option.iter (fun logf -> logf (sprintf "Fable.Remoting: Setting status %i" statusCode)) options.Logger
                                statusCode
                        headersOverride 
                        |> Option.iter(fun m -> 
                            Option.iter (fun logf -> logf "Fable.Remoting: Setting headers") options.Logger
                            m |> Map.iter (fun k v -> ctx.Response.Headers.AppendCommaSeparatedValues(k,v)))
                        return! text b next ctx
                    }
              |None ->
                Option.iter (fun logf -> logf (sprintf "Fable.Remoting: Invoking method %s" methodName)) options.Logger
                let requestBodyData =
                    match hasArg with
                    | true  -> getResourceFromReq ctx inputType genericType
                    | false -> [|null|]
                let result = ServerSide.dynamicallyInvoke methodName serverImplementation requestBodyData
                task {
                    try
                      let! unwrappedFromAsync = Async.StartAsTask result
                      let serializedResult = builder.Json options unwrappedFromAsync
                      headersOverride 
                        |> Option.iter(fun m -> 
                            Option.iter (fun logf -> logf "Fable.Remoting: Setting headers") options.Logger
                            m |> Map.iter (fun k v -> ctx.Response.Headers.AppendCommaSeparatedValues(k,v)))
                      ctx.Response.StatusCode <- statusCodeOverride |> Option.defaultValue 200
                      return! text serializedResult next ctx
                    with
                      | ex ->
                         ctx.Response.StatusCode <-
                            match statusCodeOverride with
                            |None -> 500
                            |Some statusCode ->
                                Option.iter (fun logf -> logf (sprintf "Fable.Remoting: Setting status %i" statusCode)) options.Logger
                                statusCode
                         Option.iter (fun logf -> logf (sprintf "Server error at %s" routePath)) options.Logger
                         match options.ErrorHandler with
                         | Some handler ->
                            let routeInfo = 
                              { path = routePath
                                methodName = methodName
                                httpContext = ctx }
                            match handler ex routeInfo with
                            | Ignore ->
                                let result = { error = "Server error: ignored"; ignored = true; handled = true }
                                return! text (builder.Json options result) next ctx
                            | Propagate value ->
                                let result = { error = value; ignored = false; handled = true }
                                return! text (builder.Json options result) next ctx
                         | None ->
                            let result = { error = "Server error: not handled"; ignored = false; handled = false }
                            return! text (builder.Json options result) next ctx
                }

    let sb = StringBuilder()
    let t = implementation.GetType()
    let typeName =
        match t.GenericTypeArguments with
        |[||] -> t.Name
        |[|_|] -> t.Name.[0..t.Name.Length-3]
        |_ -> failwith "Only one generic type can be injected"
    sb.AppendLine(sprintf "Building Routes for %s" typeName) |> ignore
    implementation.GetType()
        |> FSharpType.GetRecordFields
        |> Seq.map (fun propInfo ->
        let methodName = propInfo.Name
        let fullPath = options.Builder typeName methodName
        sb.AppendLine(sprintf "Record field %s maps to route %s" methodName fullPath) |> ignore
        POST >=> route fullPath
             >=> warbler (fun _ -> handleRequest methodName implementation (t.GenericTypeArguments) fullPath)
    )
    |> List.ofSeq
    |> fun routes ->
        options.Logger |> Option.iter (fun logf -> string sb |> logf)
        choose routes

  /// Computation expression to create a remoting server. Needs to open Fable.Remoting.Suave or Fable.Remoting.Giraffe for actual implementation
  /// Usage:
  /// `let server = remoting implementation {()}` for default options at /typeName/methodName
  /// `let server = remoting implementation = remoting {`
  /// `    with_builder builder` to set a `builder : (string -> string -> string)`
  /// `    use_logger logger` to set a `logger : (string -> unit)`
  /// `    use_error_handler handler` to set a `handler : (System.Exception -> RouteInfo -> ErrorResult)` in case of a server error
  /// `}`
  let remoting = RemoteBuilder
  let httpHandlerWithBuilderFor implementation builder =
    let r = remoting implementation
    let zero = r.Zero()
    let withBuilder = r.WithBuilder(zero,builder)
    let useLogger = logger |> Option.fold (fun s e -> r.UseLogger(s,e)) withBuilder
    let useErrorHandler = onErrorHandler |> Option.fold (fun s e -> r.UseErrorHandler(s,e)) useLogger
    r.Run(useErrorHandler)

  let httpHandlerFor implementation : HttpHandler =
        httpHandlerWithBuilderFor implementation (sprintf "/%s/%s")
