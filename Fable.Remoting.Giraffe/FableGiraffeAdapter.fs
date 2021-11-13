namespace Fable.Remoting.Giraffe

open Microsoft.AspNetCore.Http

open Giraffe
open System.IO
open Fable.Remoting.Server
open Newtonsoft.Json
open Fable.Remoting.Server.Proxy

module GiraffeUtil =
    let setJsonBody (response: obj) (logger: Option<string -> unit>) : HttpHandler =
        fun (next : HttpFunc) (ctx : HttpContext) ->
            use ms = new MemoryStream ()
            jsonSerialize response ms
            let responseBody = System.Text.Encoding.UTF8.GetString (ms.ToArray ())
            Diagnostics.outputPhase logger responseBody
            ctx.Response.ContentType <- "application/json; charset=utf-8"
            setBodyFromString responseBody next ctx

    /// Handles thrown exceptions
    let fail (ex: exn) (routeInfo: RouteInfo<HttpContext>) (options: RemotingOptions<HttpContext, 't>) : HttpHandler =
        let logger = options.DiagnosticsLogger
        fun (next : HttpFunc) (ctx : HttpContext) ->
            match options.ErrorHandler with
            | None -> setJsonBody (Errors.unhandled routeInfo.methodName) logger next ctx
            | Some errorHandler ->
                match errorHandler ex routeInfo with
                | Ignore -> setJsonBody (Errors.ignored routeInfo.methodName) logger next ctx
                | Propagate error -> setJsonBody (Errors.propagated error) logger next ctx

    /// Used to halt the forwarding of the Http context
    let halt: HttpContext option = None

    let buildFromImplementation<'impl> (implBuilder: HttpContext -> 'impl) (options: RemotingOptions<HttpContext, 'impl>) =
        let proxy = makeApiProxy options
        let rmsManager = options.RmsManager |> Option.defaultWith (fun _ -> recyclableMemoryStreamManager.Value)
        
        fun (next: HttpFunc) (ctx: HttpContext) -> Async.StartAsTask (async {
            let isProxyHeaderPresent = ctx.Request.Headers.ContainsKey "x-remoting-proxy"
            use output = rmsManager.GetStream "remoting-output-stream"

            let props = { ImplementationBuilder = (fun () -> implBuilder ctx); EndpointName = SubRouting.getNextPartOfPath ctx; Input = ctx.Request.Body; IsProxyHeaderPresent = isProxyHeaderPresent;
                HttpVerb = ctx.Request.Method.ToUpper (); IsContentBinaryEncoded = ctx.Request.ContentType = "application/octet-stream"; Output = output }

            match! proxy props with
            | Success isBinaryOutput ->
                ctx.Response.StatusCode <- 200

                if isBinaryOutput && isProxyHeaderPresent then
                    ctx.Response.ContentType <- "application/octet-stream"
                elif options.ResponseSerialization = SerializationType.Json then
                    ctx.Response.ContentType <- "application/json; charset=utf-8"
                else
                    ctx.Response.ContentType <- "application/msgpack"
                
                do! output.CopyToAsync ctx.Response.Body |> Async.AwaitTask
                return! next ctx |> Async.AwaitTask
            | Exception (e, functionName, requestBodyText) ->
                ctx.Response.StatusCode <- 500
                let routeInfo = { methodName = functionName; path = ctx.Request.Path.ToString(); httpContext = ctx; requestBodyText = requestBodyText }
                return! fail e routeInfo options next ctx |> Async.AwaitTask
            | InvalidHttpVerb ->
                return halt
            | EndpointNotFound ->
                match ctx.Request.Method.ToUpper(), options.Docs with
                | "GET", (Some docsUrl, Some docs) when docsUrl = ctx.Request.Path.Value ->
                    let (Documentation(docsName, docsRoutes)) = docs
                    let schema = Docs.makeDocsSchema typeof<'impl> docs options.RouteBuilder
                    let docsApp = DocsApp.embedded docsName docsUrl schema
                    return! htmlString docsApp next ctx |> Async.AwaitTask
                | "OPTIONS", (Some docsUrl, Some docs)
                    when sprintf "/%s/$schema" docsUrl = ctx.Request.Path.Value
                      || sprintf "%s/$schema" docsUrl = ctx.Request.Path.Value ->
                    let schema = Docs.makeDocsSchema typeof<'impl> docs options.RouteBuilder
                    let serializedSchema = schema.ToString(Formatting.None)
                    return! text serializedSchema next ctx |> Async.AwaitTask
                | _ ->
                    return halt
        })

module Remoting =

  /// Builds a HttpHandler from the given implementation and options
  let buildHttpHandler (options: RemotingOptions<HttpContext, 't>) =
    match options.Implementation with
    | Empty -> fun _ _ -> skipPipeline
    | StaticValue impl -> GiraffeUtil.buildFromImplementation (fun _ -> impl) options
    | FromContext createImplementationFrom -> GiraffeUtil.buildFromImplementation createImplementationFrom options