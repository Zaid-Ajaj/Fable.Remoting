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
  let mutable private onErrorHandler : ErrorHandler<HttpContext> option = None

  /// Global error handler that intercepts server errors and decides whether or not to propagate a message back to the client for backward compatibility
  let onError (handler: ErrorHandler<HttpContext>) =
        onErrorHandler <- Some handler

  let handleRequest routePath creator =
    POST >=> path routePath >=> request (
      fun (req: HttpRequest) (ctx:HttpContext) ->
        let onSuccess result = OK result >=> Writers.setMimeType "application/json; charset=utf-8"
        let onFailure result = onSuccess result >=> Writers.setStatus HttpCode.HTTP_500
        let json = System.Text.Encoding.UTF8.GetString req.rawForm
        async {
            let! result = creator ctx json
            match result with
            | Choice1Of2 res -> return! onSuccess res ctx
            | Choice2Of2 res -> return! onFailure res ctx })


  type RemoteBuilder(implementation) =
   inherit RemoteBuilderBase<HttpContext,(HttpContext -> Async<HttpContext option>),WebPart<HttpContext>>(implementation,handleRequest,choose)

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
    let r = remoting implementation
    let zero = r.Zero()
    let withBuilder = r.WithBuilder(zero,builder)
    let useLogger = logger |> Option.fold (fun s e -> r.UseLogger(s,e)) withBuilder
    let useErrorHandler = onErrorHandler |> Option.fold (fun s e -> r.UseErrorHandler(s,e)) useLogger
    r.Run(useErrorHandler)

    /// Creates a WebPart from the given implementation of a protocol. Uses the default route builder: `sprintf "/%s/%s"`.
  let webPartFor implementation : WebPart =
        webPartWithBuilderFor implementation (sprintf "/%s/%s")
