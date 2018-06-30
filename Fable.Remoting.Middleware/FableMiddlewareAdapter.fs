namespace Fable.Remoting.Middleware

open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http

open System.IO
open Fable.Remoting.Server.SharedCE
open FSharp.Control.Tasks

[<AutoOpen>]
module FableMiddlewareAdapter =

  type BuilderOptions<'ctx> with
    member this.WithBuilder b = { this with Builder = b }
    member this.WithLogger l = { this with Logger = Some l }
    member this.WithErrorHandler eh = { this with ErrorHandler = Some eh }
    member this.AddCustomHandler meth ch = { this with CustomHandlers = this.CustomHandlers |> Map.add meth ch }

  type RemotingOptions = BuilderOptions<HttpContext>

  type FableRemotingMiddleware(next          : RequestDelegate,
                               implementation: obj,
                               options       : RemotingOptions) =

      do if isNull next then nullArg "next"
      let handleRequest routePath creator =
        routePath,
        fun (next:RequestDelegate) (ctx : HttpContext)  ->
            if not (HttpMethods.IsPost ctx.Request.Method) || ctx.Request.Path.Value <> routePath then
                next.Invoke ctx
            else
                let requestBodyStream = ctx.Request.Body
                use streamReader = new StreamReader(requestBodyStream)
                let json = streamReader.ReadToEnd()
                task {
                      match (creator ctx json) with
                      | None ->
                            return! next.Invoke ctx
                      | Some res ->
                          let! { Response.StatusCode = sc; Headers = headers; Body = body } = Async.StartAsTask res
                          headers |> Map.iter (fun k v -> ctx.Response.Headers.AppendCommaSeparatedValues(k,v))
                          ctx.Response.StatusCode <- sc
                          return! ctx.Response.WriteAsync(body)} :> _
      let map = RemoteBuilderBase(implementation, handleRequest, Map.ofList).Run(options)
      member __.Invoke (ctx : HttpContext) =
        match map |> Map.tryFind (ctx.Request.Path.Value) with
        | Some func -> func next ctx
        | None -> next.Invoke ctx


  type IApplicationBuilder with
    member this.UseRemoting(impl:#obj,options:RemotingOptions) : IApplicationBuilder =
        this.UseMiddleware<FableRemotingMiddleware>(impl, options)