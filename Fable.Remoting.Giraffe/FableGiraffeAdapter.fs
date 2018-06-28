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

  let handleRequest routePath creator =
        POST >=> route routePath
             >=> warbler ( fun _ ->
        fun (next : HttpFunc) (ctx : HttpContext) ->
            let requestBodyStream = ctx.Request.Body
            use streamReader = new StreamReader(requestBodyStream)
            let json = streamReader.ReadToEnd()
            task {
                let! result = Async.StartAsTask (creator ctx json)
                match result with
                | Choice1Of2 res ->
                  ctx.Response.StatusCode <- 200
                  return! text res next ctx
                | Choice2Of2 res ->
                  ctx.Response.StatusCode <- 200
                  return! text res next ctx })

  type RemoteBuilder(implementation)=
   inherit RemoteBuilderBase<HttpContext,(HttpFunc -> HttpContext -> HttpFuncResult),HttpHandler>(implementation, handleRequest, choose)

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
