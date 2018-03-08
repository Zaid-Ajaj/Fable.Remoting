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
  type RemoteBuilder<'a>(implementation:'a) =
   inherit RemoteBuilderBase<'a,HttpRequest,WebPart<HttpContext>>(implementation)
   override __.Context(ctx) = {
       Host = ctx.host
   }
   override builder.Run(options:SharedCE.BuilderOptions) =
    let getResourceFromReq (req : HttpRequest) (inputType: System.Type[])  =
        let json = System.Text.Encoding.UTF8.GetString req.rawForm
        builder.Deserialize options json inputType

    let handleRequest methodName serverImplementation routePath =
        let inputType = ServerSide.getInputType methodName serverImplementation
        let hasArg =
            match inputType with
            |[|inputType;_|] when inputType.FullName = "Microsoft.FSharp.Core.Unit" -> false
            |_ -> true
        fun (req: HttpRequest) ->
            Option.iter (fun logf -> logf (sprintf "Fable.Remoting: Invoking method %s" methodName)) options.Logger
            let requestBodyData =
                // if input is unit
                // then don't bother getting any input from request
                match hasArg with
                | true  -> getResourceFromReq req inputType
                | false -> [|null|]
            let result = ServerSide.dynamicallyInvoke methodName serverImplementation requestBodyData
            let onSuccess result = OK result >=> Writers.setMimeType "application/json; charset=utf-8"
            let onFailure result = onSuccess result >=> Writers.setStatus HttpCode.HTTP_500
            async {
                try
                  let! dynamicResult = result
                  return builder.Json options dynamicResult |> onSuccess
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
                            return builder.Json options result |> onFailure
                        // Server error mapped into some other `value` by error handler
                        | Propagate value ->
                            let result = { error = value; ignored = false; handled = true }
                            return builder.Json options result |> onFailure
                     // There no server handler
                     | None ->
                        let result = { error = "Server error: not handled"; ignored = true; handled = false }
                        return builder.Json options result |> onFailure
                }
            |> Async.RunSynchronously

    let sb = StringBuilder()
    let typeName = builder.Implementation.GetType().Name
    sb.AppendLine(sprintf "Building Routes for %s" typeName) |> ignore
    builder.Implementation.GetType()
        |> FSharpType.GetRecordFields
        |> Seq.map (fun propInfo ->
            let methodName = propInfo.Name
            let fullPath = options.Builder typeName methodName
            sb.AppendLine(sprintf "Record field %s maps to route %s" methodName fullPath) |> ignore
            POST >=> path fullPath >=> request (handleRequest methodName builder.Implementation fullPath)
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