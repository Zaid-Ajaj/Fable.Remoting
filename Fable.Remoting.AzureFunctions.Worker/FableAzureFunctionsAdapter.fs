namespace Fable.Remoting.AzureFunctions.Worker

open System.Net
open System.Threading.Tasks
open Microsoft.Azure.Functions.Worker.Http
open System.IO
open Fable.Remoting.Server
open Newtonsoft.Json
open Fable.Remoting.Server.Proxy
open System.Linq

module private FuncsUtil =
    let private setContentType (t:string) (res:HttpResponseData) =
        res.Headers.Add("Content-Type", t)
        res
    
    let private htmlString (html:string) (req:HttpRequestData) : Task<HttpResponseData option> =
        task {
            let bytes = System.Text.Encoding.UTF8.GetBytes html
            let resp = req.CreateResponse(HttpStatusCode.OK) |> setContentType "text/html; charset=utf-8"
            do! resp.WriteBytesAsync bytes
            return Some resp
        }
    
    let text (str:string) (req:HttpRequestData) : Task<HttpResponseData option> =
        task {
            let bytes = System.Text.Encoding.UTF8.GetBytes str
            let resp = req.CreateResponse(HttpStatusCode.OK) |> setContentType "text/plain; charset=utf-8"
            do! resp.WriteBytesAsync bytes
            return Some resp
        }
    
    let private path (r:HttpRequestData) = r.Url.PathAndQuery.Split("?").[0]

    let setJsonBody (res:HttpResponseData) (response: obj) (logger: Option<string -> unit>) (req:HttpRequestData) : Task<HttpResponseData option> =
        task {
            use ms = new MemoryStream ()
            jsonSerialize response ms
            let responseBody = System.Text.Encoding.UTF8.GetString (ms.ToArray ())
            Diagnostics.outputPhase logger responseBody
            let res = res |> setContentType "application/json; charset=utf-8"
            do! res.WriteStringAsync responseBody
            return Some res
        }

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
        let rmsManager = options.RmsManager |> Option.defaultWith (fun _ -> recyclableMemoryStreamManager.Value)

        fun (req:HttpRequestData) ->
            task {
                let isProxyHeaderPresent = req.Headers.Contains "x-remoting-proxy"
                use output = rmsManager.GetStream "remoting-output-stream"
                let contentType =
                    match req.Headers.TryGetValues "Content-Type" with
                    | true, values when values.Any () -> values.First ()
                    | _ -> ""

                let props = { ImplementationBuilder = (fun () -> implBuilder req); EndpointName = path req; Input = req.Body; IsProxyHeaderPresent = isProxyHeaderPresent;
                    HttpVerb = req.Method.ToUpper (); InputContentType = contentType; Output = output }

                match! proxy props with
                | Success isBinaryOutput ->
                    let resp =
                        req.CreateResponse(HttpStatusCode.OK)
                        |> (fun r ->
                            if isBinaryOutput && isProxyHeaderPresent then
                                r |> setContentType "application/octet-stream"
                            elif options.ResponseSerialization = SerializationType.Json then
                                r |> setContentType "application/json; charset=utf-8"
                            else
                                r |> setContentType "application/vnd.msgpack"
                        )
                    do! output.CopyToAsync resp.Body
                    return Some resp
                | Exception (e, functionName, requestBodyText) ->
                    let routeInfo = { methodName = functionName; path = path req; httpContext = req; requestBodyText = requestBodyText }
                    return! fail e routeInfo options req
                | InvalidHttpVerb -> return halt
                | EndpointNotFound ->
                    match req.Method.ToUpper(), options.Docs with
                    | "GET", (Some docsUrl, Some docs) when docsUrl = (path req) ->
                        let (Documentation(docsName, docsRoutes)) = docs
                        let schema = Docs.makeDocsSchema typeof<'impl> docs options.RouteBuilder
                        let docsApp = DocsApp.embedded docsName docsUrl schema
                        return! htmlString docsApp req
                    | "OPTIONS", (Some docsUrl, Some docs)
                        when sprintf "/%s/$schema" docsUrl = (path req)
                          || sprintf "%s/$schema" docsUrl = (path req) ->
                        let schema = Docs.makeDocsSchema typeof<'impl> docs options.RouteBuilder
                        let serializedSchema = schema.ToString(Formatting.None)
                        return! text serializedSchema req
                    | _ -> return halt
            }

module Remoting =

  /// Builds a HttpRequestData -> HttpResponseData option function from the given implementation and options
  /// Please see HttpResponseData.fromRequestHandler for using output of this function
  let buildRequestHandler (options: RemotingOptions<HttpRequestData, 't>) =
    match options.Implementation with
    | StaticValue impl -> FuncsUtil.buildFromImplementation (fun _ -> impl) options
    | FromContext createImplementationFrom -> FuncsUtil.buildFromImplementation createImplementationFrom options
    | Empty -> fun _ -> Task.FromResult None

module FunctionsRouteBuilder =
    /// Default RouteBuilder for Azure Functions running HttpTrigger on /api prefix
    let apiPrefix = sprintf "/api/%s/%s"
    /// RouteBuilder for Azure Functions running HttpTrigger without any prefix
    let noPrefix = sprintf "/%s/%s"

module HttpResponseData =
    
    let rec private chooseHttpResponse (fns:(HttpRequestData -> Task<HttpResponseData option>) list) =
        fun (req:HttpRequestData) ->
            task {
                match fns with
                | [] -> return req.CreateResponse(HttpStatusCode.NotFound)
                | func :: tail ->
                    match! func req with
                    | Some r -> return r
                    | None -> return! chooseHttpResponse tail req
            }
    
    /// Build HttpResponseData from builder functions and HttpRequestData
    /// This functionality is very similar to choose function from Giraffe
    let fromRequestHandlers (req:HttpRequestData) (fns:(HttpRequestData -> Task<HttpResponseData option>) list)  : Task<HttpResponseData> =
        chooseHttpResponse fns req
    
    /// Build HttpResponseData from single builder function and HttpRequestData
    let fromRequestHandler (req:HttpRequestData) (fn:HttpRequestData -> Task<HttpResponseData option>)  : Task<HttpResponseData> =
        fromRequestHandlers req [fn]