namespace Fable.Remoting.AspNetCore

open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http

open System.IO
open System.Threading.Tasks
open Fable.Remoting.Server
open Newtonsoft.Json
open Fable.Remoting.Server.Proxy

type HttpFuncResult = Task<HttpContext option>
type HttpFunc = HttpContext -> HttpFuncResult
type HttpHandler = HttpFunc -> HttpFunc

[<AutoOpen>]
module Extensions =
    type HttpContext with
        member self.GetService<'t>() = self.RequestServices.GetService(typeof<'t>) :?> 't


/// The parts from Giraffe needed to simplify the middleware implementation
module internal Middleware =

    let writeStringAsync (input: string) (ctx: HttpContext) (logger: Option<string -> unit>) =
        task {
            Diagnostics.outputPhase logger input
            let bytes = System.Text.Encoding.UTF8.GetBytes(input)
            do! ctx.Response.Body.WriteAsync(bytes, 0, bytes.Length)
            return Some ctx
        }

    let text (input: string) =
        fun (next : HttpFunc) (ctx : HttpContext) ->
            task {
                let bytes = System.Text.Encoding.UTF8.GetBytes(input)
                do! ctx.Response.Body.WriteAsync(bytes, 0, bytes.Length)
                return Some ctx
            }

    let compose (handler1 : HttpHandler) (handler2 : HttpHandler) : HttpHandler =
        fun (final : HttpFunc) ->
            let func = final |> handler2 |> handler1
            fun (ctx : HttpContext) ->
                match ctx.Response.HasStarted with
                | true  -> final ctx
                | false -> func ctx

    let (>=>) = compose

    let setResponseBody (response: obj) logger : HttpHandler =
        fun (next : HttpFunc) (ctx : HttpContext) ->
            task {
                use ms = new MemoryStream ()
                jsonSerialize response ms
                let responseBody = System.Text.Encoding.UTF8.GetString (ms.ToArray ())
                return! writeStringAsync responseBody ctx logger
            }

    /// Sets the content type of the Http response
    let setContentType (contentType: string) : HttpHandler =
        fun (next : HttpFunc) (ctx : HttpContext) ->
            task {
                ctx.Response.ContentType <- contentType
                return Some ctx
            }

    /// Sets the body of the response to type of JSON
    let setBody value logger : HttpHandler =
        setResponseBody value logger
        >=> setContentType "application/json; charset=utf-8"

    /// Used to forward of the Http context
    let halt : HttpHandler =
      fun (next : HttpFunc) (ctx : HttpContext) -> Task.FromResult None

    let fail (ex: exn) (routeInfo: RouteInfo<HttpContext>) (options: RemotingOptions<HttpContext, 't>) : HttpHandler =
      let logger = options.DiagnosticsLogger
      fun (next : HttpFunc) (ctx : HttpContext) ->
        task {
            match options.ErrorHandler with
            | None -> return! setBody (Errors.unhandled routeInfo.methodName) logger next ctx
            | Some errorHandler ->
                match errorHandler ex routeInfo with
                | Ignore -> return! setBody (Errors.ignored routeInfo.methodName) logger next ctx
                | Propagate error -> return! setBody (Errors.propagated error) logger next ctx
        }

    let notFound (options: RemotingOptions<HttpContext, 'impl>) next (ctx: HttpContext) =
        match ctx.Request.Method.ToUpper(), options.Docs with
        | "GET", (Some docsUrl, Some docs) when docsUrl = ctx.Request.Path.Value ->
            let (Documentation(docsName, docsRoutes)) = docs
            let schema = Docs.makeDocsSchema typeof<'impl> docs options.RouteBuilder
            let docsApp = DocsApp.embedded docsName docsUrl schema
            (text docsApp >=> setContentType "text/html") next ctx
        | "OPTIONS", (Some docsUrl, Some docs)
            when sprintf "/%s/$schema" docsUrl = ctx.Request.Path.Value
              || sprintf "%s/$schema" docsUrl = ctx.Request.Path.Value ->
            let schema = Docs.makeDocsSchema typeof<'impl> docs options.RouteBuilder
            let serializedSchema = schema.ToString(Formatting.None)
            text serializedSchema next ctx
        | _ ->
            halt next ctx

    let buildFromImplementation<'impl> (implBuilder: HttpContext -> 'impl) (options: RemotingOptions<HttpContext, 'impl>) =
        let proxy = makeApiProxy options
        let rmsManager = options.RmsManager |> Option.defaultWith (fun _ -> recyclableMemoryStreamManager.Value)

        fun (next: HttpFunc) (ctx: HttpContext) -> task {
            let isProxyHeaderPresent = ctx.Request.Headers.ContainsKey "x-remoting-proxy"
            use output = rmsManager.GetStream "remoting-output-stream"

            let props = { ImplementationBuilder = (fun () -> implBuilder ctx); EndpointName = ctx.Request.Path.Value; Input = ctx.Request.Body; IsProxyHeaderPresent = isProxyHeaderPresent;
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

                do! output.CopyToAsync ctx.Response.Body
                return! next ctx
            | Exception (e, functionName, requestBodyText) ->
                ctx.Response.StatusCode <- 500
                let routeInfo = { methodName = functionName; path = ctx.Request.Path.ToString(); httpContext = ctx; requestBodyText = requestBodyText }
                return! fail e routeInfo options next ctx
            | InvalidHttpVerb ->
                return! halt next ctx
            | EndpointNotFound ->
                return! notFound options next ctx
        }

type RemotingMiddleware<'t>(next          : RequestDelegate,
                            handler       : HttpFunc) =

    do if isNull next then nullArg "next"

    member __.Invoke (ctx : HttpContext) = task {
        let! result = handler ctx
        if (result.IsNone) then return! next.Invoke ctx
    }

[<AutoOpen>]
module AppBuilderExtensions =
    type IApplicationBuilder with
      member this.UseRemoting(options:RemotingOptions<HttpContext, 't>) =
          let handler =
              match options.Implementation with
              | Empty -> Middleware.halt
              | StaticValue impl -> Middleware.buildFromImplementation (fun _ -> impl) options
              | FromContext createImplementationFrom -> Middleware.buildFromImplementation createImplementationFrom options

          this.UseMiddleware<RemotingMiddleware<'t>> (handler (Some >> Task.FromResult)) |> ignore