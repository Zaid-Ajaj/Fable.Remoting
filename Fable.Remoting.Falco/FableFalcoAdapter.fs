namespace Fable.Remoting.Falco

open System
open System.IO
open Fable.Remoting.Server.Proxy
open Falco
open Fable.Remoting.Server
open Falco.Routing
open Microsoft.AspNetCore.Http
open Newtonsoft.Json
open TypeShape

//Implement similar methods to the giraffe pipline. Modified to HttpHandler returns a task unit
module FalcoUtils =
    let writeStringAsync (input: string) (ctx: HttpContext) (logger: Option<string -> unit>) =
        task {
            Diagnostics.outputPhase logger input
            let bytes = System.Text.Encoding.UTF8.GetBytes(input)
            do! ctx.Response.Body.WriteAsync(bytes, 0, bytes.Length)
        }

    let text (input: string) (ctx: HttpContext) =
            task {
                let bytes = System.Text.Encoding.UTF8.GetBytes(input)
                do! ctx.Response.Body.WriteAsync(bytes, 0, bytes.Length)
            }

    /// Sets the content type of the Http response
    let setContentType (contentType: string) (ctx: HttpContext) : HttpContext =
        Response.withContentType contentType ctx
    
    let setResponseBody (response: obj) logger (ctx: HttpContext ) =
            task {
                use ms = new MemoryStream ()
                jsonSerialize response ms
                let responseBody = System.Text.Encoding.UTF8.GetString (ms.ToArray ())
                do! writeStringAsync responseBody ctx logger
            }

    /// Sets the body of the response to type of JSON
    let setBody value logger  (ctx: HttpContext)=
                setContentType "application/json; charset=utf-8" ctx
                |> setResponseBody value logger
            

    /// If no endpoints are found send an empty response
    let halt : HttpHandler =
      Response.ofEmpty

    let fail (ex: exn) (routeInfo: RouteInfo<HttpContext>) (options: RemotingOptions<HttpContext, 't>) : HttpHandler =
      let logger = options.DiagnosticsLogger
      fun (ctx : HttpContext) ->
        task {
            match options.ErrorHandler with
            | None -> return! setBody (Errors.unhandled routeInfo.methodName) logger ctx
            | Some errorHandler ->
                match errorHandler ex routeInfo with
                | Ignore -> return! setBody (Errors.ignored routeInfo.methodName) logger ctx
                | Propagate error -> return! setBody (Errors.propagated error) logger ctx
        }

    let notFound (options: RemotingOptions<HttpContext, 'impl>) : HttpHandler=
        fun (ctx: HttpContext) -> task {
            match ctx.Request.Method.ToUpper(), options.Docs with
            | "GET", (Some docsUrl, Some docs) when docsUrl = ctx.Request.Path.Value ->
                let (Documentation(docsName, docsRoutes)) = docs
                let schema = Docs.makeDocsSchema typeof<'impl> docs options.RouteBuilder
                let docsApp = DocsApp.embedded docsName docsUrl schema
                return! ctx |> setContentType "text/html" |> text docsApp
            | "OPTIONS", (Some docsUrl, Some docs)
                when sprintf "/%s/$schema" docsUrl = ctx.Request.Path.Value
                  || sprintf "%s/$schema" docsUrl = ctx.Request.Path.Value ->
                let schema = Docs.makeDocsSchema typeof<'impl> docs options.RouteBuilder
                let serializedSchema = schema.ToString(Formatting.None)
                return! text serializedSchema ctx
            | _ ->
                return! halt ctx
        }
        

    let buildFromImplementation<'impl> (implBuilder: HttpContext -> 'impl) (options: RemotingOptions<HttpContext, 'impl>) : HttpEndpoint seq=
        let proxy = makeApiProxy options
        let rmsManager = getRecyclableMemoryStreamManager options

        let handler: HttpHandler = fun (ctx: HttpContext) -> task {
            let isProxyHeaderPresent = ctx.Request.Headers.ContainsKey "x-remoting-proxy"
            use output = rmsManager.GetStream "remoting-output-stream"

            let props = { ImplementationBuilder = (fun () -> implBuilder ctx); EndpointName = ctx.Request.Path.Value; Input = ctx.Request.Body; IsProxyHeaderPresent = isProxyHeaderPresent;
                HttpVerb = ctx.Request.Method; InputContentType = ctx.Request.ContentType; Output = output }

            match! proxy props with
            | Success isBinaryOutput ->
                ctx.Response.StatusCode <- 200

                if isBinaryOutput && isProxyHeaderPresent then
                    ctx.Response.ContentType <- "application/octet-stream"
                elif options.ResponseSerialization.IsJson then
                    ctx.Response.ContentType <- "application/json; charset=utf-8"
                else
                    ctx.Response.ContentType <- "application/vnd.msgpack"

                do! output.CopyToAsync ctx.Response.Body
            | Exception (e, functionName, requestBodyText) ->
                ctx.Response.StatusCode <- 500
                let routeInfo = { methodName = functionName; path = ctx.Request.Path.ToString(); httpContext = ctx; requestBodyText = requestBodyText }
                return! fail e routeInfo options ctx
            | InvalidHttpVerb ->
                return! halt ctx
            | EndpointNotFound ->
                return! notFound options ctx
        }
        
        //Get a list of endpoints. Needed to construct the pattern for route matching
        let endPoints =
            match shapeof<'impl> with
            | Shape.FSharpRecord (:? ShapeFSharpRecord<'impl> as shape) ->
                shape.Fields
                |> Array.map (fun f -> options.RouteBuilder typeof<'impl>.Name f.MemberInfo.Name)
            | _ ->
                failwithf "Protocol definition must be encoded as a record type. The input type '%s' was not a record." typeof<'impl>.Name
        
        //add document endpoint if it exists
        let endPointsWithDoc =
            match options.Docs with
            | (Some docsUrl, _) -> Array.append endPoints [|docsUrl|]
            | _ -> endPoints
        
        endPointsWithDoc |> Array.map (fun endPoint -> any endPoint handler) |> Seq.ofArray

module Remoting =
    let buildHttpEndpoints (options: RemotingOptions<HttpContext, 't>) : HttpEndpoint seq =
        match options.Implementation with
        | Empty -> []
        | StaticValue impl -> FalcoUtils.buildFromImplementation (fun _ -> impl) options
        | FromContext createImplementationFrom -> FalcoUtils.buildFromImplementation createImplementationFrom options
