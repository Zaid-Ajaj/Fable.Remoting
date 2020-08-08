namespace Fable.Remoting.Giraffe

open Microsoft.AspNetCore.Http

open Giraffe
open System.IO
open Fable.Remoting.Server
open FSharp.Control.Tasks.V2.ContextInsensitive
open Newtonsoft.Json
open Fable.Remoting.Server.Proxy

module GiraffeUtil =
    let setJsonBody (response: obj) (logger: Option<string -> unit>) : HttpHandler =
        fun (next : HttpFunc) (ctx : HttpContext) ->
            task {
                use ms = new MemoryStream ()
                jsonSerialize response ms
                let responseBody = System.Text.Encoding.UTF8.GetString (ms.ToArray ())
                Diagnostics.outputPhase logger responseBody
                ctx.Response.ContentType <- "application/json; charset=utf-8"
                return! setBodyFromString responseBody next ctx
            }

    /// Handles thrown exceptions
    let fail (ex: exn) (routeInfo: RouteInfo<HttpContext>) (options: RemotingOptions<HttpContext, 't>) : HttpHandler =
      let logger = options.DiagnosticsLogger
      fun (next : HttpFunc) (ctx : HttpContext) ->
        task {
            match options.ErrorHandler with
            | None -> return! setJsonBody (Errors.unhandled routeInfo.methodName) logger next ctx
            | Some errorHandler ->
                match errorHandler ex routeInfo with
                | Ignore -> return! setJsonBody (Errors.ignored routeInfo.methodName) logger next ctx
                | Propagate error -> return! setJsonBody (Errors.propagated error) logger next ctx
        }

    let buildFromImplementation<'impl> (implBuilder: HttpContext -> 'impl) (options: RemotingOptions<HttpContext, 'impl>) =
        let proxy = makeApiProxy options
        
        fun (next: HttpFunc) (ctx: HttpContext) -> task {
            let isProxyHeaderPresent = ctx.Request.Headers.ContainsKey "x-remoting-proxy"
            let impl = implBuilder ctx
            let props = { Implementation = impl; EndpointName = ctx.Request.Path.Value; Input = ctx.Request.Body; IsProxyHeaderPresent = isProxyHeaderPresent;
                HttpVerb = ctx.Request.Method.ToUpper (); IsContentBinaryEncoded = ctx.Request.ContentType = "application/octet-stream" }

            match! proxy props with
            | Success (isBinaryOutput, output) ->
                use output = output
                ctx.Response.StatusCode <- 200

                if isBinaryOutput && isProxyHeaderPresent then
                    ctx.Response.ContentType <- "application/octet-stream"
                elif options.ResponseSerialization = SerializationType.Json then
                    ctx.Response.ContentType <- "application/json; charset=utf-8"
                else
                    ctx.Response.ContentType <- "application/msgpack"
                
                do! output.CopyToAsync ctx.Response.Body
                return! next ctx
            | Exception (e, functionName) ->
                ctx.Response.StatusCode <- 500
                let routeInfo = { methodName = functionName; path = ctx.Request.Path.ToString(); httpContext = ctx }
                return! fail e routeInfo options next ctx
            | InvalidHttpVerb ->
                return! skipPipeline
            | EndpointNotFound ->
                match ctx.Request.Method.ToUpper(), options.Docs with
                | "GET", (Some docsUrl, Some docs) when docsUrl = ctx.Request.Path.Value ->
                    let (Documentation(docsName, docsRoutes)) = docs
                    let schema = Docs.makeDocsSchema typeof<'impl> docs options.RouteBuilder
                    let docsApp = DocsApp.embedded docsName docsUrl schema
                    return! htmlString docsApp next ctx
                | "OPTIONS", (Some docsUrl, Some docs)
                    when sprintf "/%s/$schema" docsUrl = ctx.Request.Path.Value
                      || sprintf "%s/$schema" docsUrl = ctx.Request.Path.Value ->
                    let schema = Docs.makeDocsSchema typeof<'impl> docs options.RouteBuilder
                    let serializedSchema = schema.ToString(Formatting.None)
                    return! text serializedSchema next ctx
                | _ ->
                    return! skipPipeline
        }

module Remoting =

  /// Builds a HttpHandler from the given implementation and options
  let buildHttpHandler (options: RemotingOptions<HttpContext, 't>) =
    match options.Implementation with
    | Empty -> fun _ _ -> skipPipeline
    | StaticValue impl -> GiraffeUtil.buildFromImplementation (fun _ -> impl) options
    | FromContext createImplementationFrom -> GiraffeUtil.buildFromImplementation createImplementationFrom options
            