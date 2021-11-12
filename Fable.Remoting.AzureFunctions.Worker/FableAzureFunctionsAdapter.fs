namespace Fable.Remoting.AzureFunctions.Worker

open System
open System.Net
open System.Threading.Tasks
open Microsoft.Azure.Functions.Worker
open Microsoft.Azure.Functions.Worker.Http
open System.IO
open Fable.Remoting.Server
open Newtonsoft.Json
open Fable.Remoting.Server.Proxy

module private FuncsUtil =
    let private setContentType (t:string) (res:HttpResponseData) =
        res.Headers.Add("Content-Type", t)
        res
    
    let private htmlString (html:string) (req:HttpRequestData) : Task<HttpResponseData option> =
        async {
            let bytes = System.Text.Encoding.UTF8.GetBytes html
            let resp = req.CreateResponse(HttpStatusCode.OK) |> setContentType "text/html; charset=utf-8"
            do! resp.WriteBytesAsync bytes |> Async.AwaitTask
            return Some resp
        }
        |> Async.StartAsTask
    
    let text (str:string) (req:HttpRequestData) : Task<HttpResponseData option> =
        async {
            let bytes = System.Text.Encoding.UTF8.GetBytes str
            let resp = req.CreateResponse(HttpStatusCode.OK) |> setContentType "text/plain; charset=utf-8"
            do! resp.WriteBytesAsync bytes |> Async.AwaitTask
            return Some resp
        }
        |> Async.StartAsTask
    
    let private path (r:HttpRequestData) = r.Url.PathAndQuery.Split("?").[0]

    let setJsonBody (res:HttpResponseData) (response: obj) (logger: Option<string -> unit>) (req:HttpRequestData) : Task<HttpResponseData option> =
        async {
            use ms = new MemoryStream ()
            jsonSerialize response ms
            let responseBody = System.Text.Encoding.UTF8.GetString (ms.ToArray ())
            Diagnostics.outputPhase logger responseBody
            let res = res |> setContentType "application/json; charset=utf-8"
            do! res.WriteStringAsync responseBody |> Async.AwaitTask
            return Some res
        }
        |> Async.StartAsTask

    /// Handles thrown exceptions
    let fail (ex: exn) (routeInfo: RouteInfo<HttpRequestData>) (options: RemotingOptions<HttpRequestData, 't>) (req:HttpRequestData) : Task<HttpResponseData option> =
        let resp = req.CreateResponse(HttpStatusCode.InternalServerError)
        let logger = options.DiagnosticsLogger
        match options.ErrorHandler with
        | None -> setJsonBody resp (Errors.unhandled routeInfo.methodName) logger req
        | Some errorHandler ->
            match errorHandler ex routeInfo with
            | Ignore -> setJsonBody resp (Errors.ignored routeInfo.methodName) logger req
            | Propagate error -> setJsonBody resp (Errors.propagated error) logger req
    
    let halt: HttpResponseData option = None
    
    let buildFromImplementation<'impl> (implBuilder: HttpRequestData -> 'impl) (options: RemotingOptions<HttpRequestData, 'impl>) =
        let proxy = makeApiProxy options
        fun (req:HttpRequestData) ->
            async {
                let isProxyHeaderPresent = req.Headers.Contains "x-remoting-proxy"
                let isBinaryEncoded =
                    match req.Headers.TryGetValues "Content-Type" with
                    | true, values -> values |> Seq.contains "application/octet-stream"
                    | false, _ -> false
                let props = { ImplementationBuilder = (fun () -> implBuilder req); EndpointName = path req; Input = req.Body; IsProxyHeaderPresent = isProxyHeaderPresent;
                    HttpVerb = req.Method.ToUpper (); IsContentBinaryEncoded = isBinaryEncoded }

                match! proxy props with
                | Success (isBinaryOutput, output) ->
                    use output = output
                    let resp =
                        req.CreateResponse(HttpStatusCode.OK)
                        |> (fun r ->
                            if isBinaryOutput && isProxyHeaderPresent then
                                r |> setContentType "application/octet-stream"
                            elif options.ResponseSerialization = SerializationType.Json then
                                r |> setContentType "application/json; charset=utf-8"
                            else
                                r |> setContentType "application/msgpack"
                        )
                    do! output.CopyToAsync resp.Body |> Async.AwaitTask
                    return Some resp
                | Exception (e, functionName, requestBodyText) ->
                    let routeInfo = { methodName = functionName; path = path req; httpContext = req; requestBodyText = requestBodyText }
                    return! fail e routeInfo options req |> Async.AwaitTask
                | InvalidHttpVerb -> return halt
                | EndpointNotFound ->
                    match req.Method.ToUpper(), options.Docs with
                    | "GET", (Some docsUrl, Some docs) when docsUrl = (path req) ->
                        let (Documentation(docsName, docsRoutes)) = docs
                        let schema = Docs.makeDocsSchema typeof<'impl> docs options.RouteBuilder
                        let docsApp = DocsApp.embedded docsName docsUrl schema
                        return! htmlString docsApp req |> Async.AwaitTask
                    | "OPTIONS", (Some docsUrl, Some docs)
                        when sprintf "/%s/$schema" docsUrl = (path req)
                          || sprintf "%s/$schema" docsUrl = (path req) ->
                        let schema = Docs.makeDocsSchema typeof<'impl> docs options.RouteBuilder
                        let serializedSchema = schema.ToString(Formatting.None)
                        return! text serializedSchema req |> Async.AwaitTask
                    | _ -> return halt
            }
            |> Async.StartAsTask

module Remoting =

  /// Builds a HttpRequestData -> HttpResponseData option function from the given implementation and options
  /// Please see HttpResponseData.fromRequestHandler for using output of this function
  let buildRequestHandler (options: RemotingOptions<HttpRequestData, 't>) =
    match options.Implementation with
    | StaticValue impl -> FuncsUtil.buildFromImplementation (fun _ -> impl) options
    | FromContext createImplementationFrom -> FuncsUtil.buildFromImplementation createImplementationFrom options
    | Empty -> fun _ -> async { return None } |> Async.StartAsTask

module FunctionsRouteBuilder =
    /// Default RouteBuilder for Azure Functions running HttpTrigger on /api prefix
    let apiPrefix = sprintf "/api/%s/%s"
    /// RouteBuilder for Azure Functions running HttpTrigger without any prefix
    let noPrefix = sprintf "/%s/%s"

module HttpResponseData =
    
    let rec private chooseHttpResponse (fns:(HttpRequestData -> Task<HttpResponseData option>) list) =
        fun (req:HttpRequestData) ->
            async {
                match fns with
                | [] -> return req.CreateResponse(HttpStatusCode.NotFound)
                | func :: tail ->
                    let! result = func req |> Async.AwaitTask
                    match result with
                    | Some r -> return r
                    | None -> return! chooseHttpResponse tail req
            }
    
    /// Build HttpResponseData from builder functions and HttpRequestData
    /// This functionality is very similar to choose function from Giraffe
    let fromRequestHandlers (req:HttpRequestData) (fns:(HttpRequestData -> Task<HttpResponseData option>) list)  : Task<HttpResponseData> =
        chooseHttpResponse fns req |> Async.StartAsTask
    
    /// Build HttpResponseData from single builder function and HttpRequestData
    let fromRequestHandler (req:HttpRequestData) (fn:HttpRequestData -> Task<HttpResponseData option>)  : Task<HttpResponseData> =
        fromRequestHandlers req [fn]