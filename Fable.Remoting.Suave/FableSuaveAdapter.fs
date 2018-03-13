namespace Fable.Remoting.Suave

open Suave
open Suave.Filters
open Suave.Operators
open Suave.Successful

open FSharp.Reflection
open Fable.Remoting.Server
open System.Text

[<AutoOpen>]
module FableSuaveAdapter =
  /// Legacy logger for backward compatibility. Use `use_logger` on the computation expression instead
  let mutable logger : (string -> unit) option = None
  /// Legacy ErrorHandler for backward compatibility. Use `use_error_handler` on the computation expression instead
  let mutable private onErrorHandler : ErrorHandler option = None

  /// Global error handler that intercepts server errors and decides whether or not to propagate a message back to the client for backward compatibility
  let onError (handler: ErrorHandler) =
        onErrorHandler <- Some handler
  type RemoteBuilder(implementation) =
   inherit RemoteBuilderBase<HttpContext,WebPart<HttpContext>>()
   
   override builder.Run(options:SharedCE.BuilderOptions<HttpContext>) =
    let getResourceFromReq (req : HttpRequest) (inputType: System.Type[])  =
        let json = System.Text.Encoding.UTF8.GetString req.rawForm
        builder.Deserialize options json inputType

    let handleRequest methodName serverImplementation routePath =
        let inputType = ServerSide.getInputType methodName serverImplementation
        let hasArg =
            match inputType with
            |[|inputType;_|] when inputType.FullName = "Microsoft.FSharp.Core.Unit" -> false
            |_ -> true
        fun (req: HttpRequest) (ctx:HttpContext) ->
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
            async {return None}
          else
            let setHeaders =
                  match headersOverride with
                  |Some headers ->
                    Option.iter (fun logf -> logf "Fable.Remoting: Setting headers") options.Logger
                    headers |> Map.fold (fun ctx k v -> ctx >=> Writers.addHeader k v) (fun ctx -> async {return Some ctx})
                  |None -> (fun ctx -> async {return Some ctx})
            let setStatus code =
                    match statusCodeOverride with
                    |Some statusCode ->
                        Option.iter (fun logf -> logf (sprintf "Fable.Remoting: Setting status %i" statusCode)) options.Logger
                        fun ctx -> async {return Some {ctx with response = {ctx.response with status = {ctx.response.status with code = statusCode}}}}
                    |None -> Writers.setStatus code
            match bodyOverride with
            |Some b -> (setHeaders >=> setStatus HTTP_200 >=> (fun ctx -> async {return Some {ctx with response = {ctx.response with content = HttpContent.Bytes (System.Text.Encoding.UTF8.GetBytes b)}}})) ctx
            |None ->    
                let flow code response  = 
                    setHeaders
                    >=>
                    OK response                     
                    >=> setStatus code
                    >=> Writers.setMimeType "application/json; charset=utf-8"
                Option.iter (fun logf -> logf (sprintf "Fable.Remoting: Invoking method %s" methodName)) options.Logger
                let requestBodyData =
                    // if input is unit
                    // then don't bother getting any input from request
                    match hasArg with
                    | true  -> getResourceFromReq req inputType
                    | false -> [|null|]
                let result = ServerSide.dynamicallyInvoke methodName serverImplementation requestBodyData
                let onSuccess = flow HttpCode.HTTP_200 
                let onFailure = flow HttpCode.HTTP_500
                async {
                    try
                      let! dynamicResult = result
                      return! builder.Json options dynamicResult |> fun a -> onSuccess a ctx
                    with
                      | ex ->
                         Option.iter (fun logf -> logf (sprintf "Server error at %s" routePath)) options.Logger
                         let route : RouteInfo = { path = routePath; methodName = methodName  }
                         match options.ErrorHandler with
                         | Some handler ->
                            let result = handler ex route
                            match result with
                            // Server error ignored by error handler
                            | Ignore ->
                                let result = { error = "Server error: ignored"; ignored = true; handled = true }
                                return! builder.Json options result |> fun a -> onFailure a ctx
                            // Server error mapped into some other `value` by error handler
                            | Propagate value ->
                                let result = { error = value; ignored = false; handled = true }
                                return! builder.Json options result |> fun a -> onFailure a ctx
                         // There no server handler
                         | None ->
                            let result = { error = "Server error: not handled"; ignored = true; handled = false }
                            return! builder.Json options result |> fun a -> onFailure a ctx
                    }
                

    let sb = StringBuilder()
    let typeName = implementation.GetType().Name
    sb.AppendLine(sprintf "Building Routes for %s" typeName) |> ignore
    implementation.GetType()
        |> FSharpType.GetRecordFields
        |> Seq.map (fun propInfo ->
            let methodName = propInfo.Name
            let fullPath = options.Builder typeName methodName
            sb.AppendLine(sprintf "Record field %s maps to route %s" methodName fullPath) |> ignore
            POST >=> path fullPath >=> request (handleRequest methodName implementation fullPath)
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
  /// Creates a `WebPart` from the given implementation of a protocol and a route builder to specify how to the paths should be built.
  let webPartWithBuilderFor implementation (builder:string->string->string) : WebPart =
    remoting implementation {
            with_builder builder
            use_some_logger logger
            use_some_error_handler onErrorHandler
        }

    /// Creates a WebPart from the given implementation of a protocol. Uses the default route builder: `sprintf "/%s/%s"`.
  let webPartFor implementation : WebPart =
        webPartWithBuilderFor implementation (sprintf "/%s/%s")