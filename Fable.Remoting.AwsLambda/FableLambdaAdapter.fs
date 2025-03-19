namespace Fable.Remoting.AwsLambda.Worker
// TODO? rename to Fable.Remoting.AwsLambda.ApiGatewayHttpApiV2 

open System
open System.Net
open System.Text
open System.Threading.Tasks
open System.IO
open Fable.Remoting.Server
open Fable.Remoting.Server.Proxy
open Amazon.Lambda.APIGatewayEvents
open Newtonsoft.Json

type HttpRequestData = APIGatewayHttpApiV2ProxyRequest
type HttpResponseData = APIGatewayHttpApiV2ProxyResponse

module private FuncsUtil =

  let private htmlString (html: string) (req: HttpRequestData) : Task<HttpResponseData option> =
    task {
      let resp = HttpResponseData(StatusCode = int HttpStatusCode.OK, Body = html)
      resp.SetHeaderValues("Content-Type", "text/html; charset=utf-8", false)

      return Some resp
    }

  let text (str: string) (req: HttpRequestData) : Task<HttpResponseData option> =
    task {
      let resp = HttpResponseData(StatusCode = int HttpStatusCode.OK, Body = str)
      resp.SetHeaderValues("Content-Type", "text/plain; charset=utf-8", false)
      return Some resp
    }

  let private path (r: HttpRequestData) = r.RawPath

  let setJsonBody
    (res: HttpResponseData)
    (response: obj)
    (logger: Option<string -> unit>)
    (req: HttpRequestData)
    : Task<HttpResponseData option> =
    task {
      use ms = new MemoryStream()
      jsonSerialize response ms
      let responseBody = System.Text.Encoding.UTF8.GetString(ms.ToArray())
      Diagnostics.outputPhase logger responseBody
      res.SetHeaderValues("Content-Type", "application/json; charset=utf-8", false)
      res.Body <- responseBody
      return Some res
    }

  /// Handles thrown exceptions
  let fail
    (ex: exn)
    (routeInfo: RouteInfo<HttpRequestData>)
    (options: RemotingOptions<HttpRequestData, 't>)
    (req: HttpRequestData)
    : Task<HttpResponseData option> =
    let resp = HttpResponseData(StatusCode = int HttpStatusCode.InternalServerError)
    let logger = options.DiagnosticsLogger

    match options.ErrorHandler with
    | None -> setJsonBody resp (Errors.unhandled routeInfo.methodName) logger req
    | Some errorHandler ->
      match errorHandler ex routeInfo with
      | Ignore -> setJsonBody resp (Errors.ignored routeInfo.methodName) logger req
      | Propagate error -> setJsonBody resp (Errors.propagated error) logger req

  let halt: HttpResponseData option = None

  let buildFromImplementation<'impl>
    (implBuilder: HttpRequestData -> 'impl)
    (options: RemotingOptions<HttpRequestData, 'impl>)
    =
    let proxy = makeApiProxy options

    let rmsManager =
      options.RmsManager
      |> Option.defaultWith (fun _ -> recyclableMemoryStreamManager.Value)

    fun (req: HttpRequestData) ->
      task {
        let isProxyHeaderPresent = req.Headers.Keys.Contains "x-remoting-proxy"
        use output = rmsManager.GetStream "remoting-output-stream"

        let contentType =
          match req.Headers.TryGetValue "Content-Type" with
          | true, x -> x
          | _ -> ""

        let bodyAsStream =
          if String.IsNullOrEmpty req.Body then
            new MemoryStream()
          else
            new MemoryStream(Encoding.UTF8.GetBytes(req.Body))

        let props =
          { ImplementationBuilder = (fun () -> implBuilder req)
            EndpointName = path req
            Input = bodyAsStream
            IsProxyHeaderPresent = isProxyHeaderPresent
            HttpVerb = req.RequestContext.Http.Method
            InputContentType = contentType
            Output = output }

        match! proxy props with
        | Success isBinaryOutput ->
          let resp = HttpResponseData(StatusCode = int HttpStatusCode.OK)

          if isBinaryOutput && isProxyHeaderPresent then
            resp.SetHeaderValues("Content-Type", "application/octet-stream", false)
          elif options.ResponseSerialization = SerializationType.Json then
            resp.SetHeaderValues("Content-Type", "application/json; charset=utf-8", false)
          else
            resp.SetHeaderValues("Content-Type", "application/vnd.msgpack", false)

          let result = Encoding.UTF8.GetString(output.ToArray())
          resp.Body <- result

          return Some resp
        | Exception(e, functionName, requestBodyText) ->
          let routeInfo =
            { methodName = functionName
              path = path req
              httpContext = req
              requestBodyText = requestBodyText }

          return! fail e routeInfo options req
        | InvalidHttpVerb -> return halt
        | EndpointNotFound ->
          match req.RequestContext.Http.Method.ToUpper(), options.Docs with
          | "GET", (Some docsUrl, Some docs) when docsUrl = (path req) ->
            let (Documentation(docsName, docsRoutes)) = docs
            let schema = Docs.makeDocsSchema typeof<'impl> docs options.RouteBuilder
            let docsApp = DocsApp.embedded docsName docsUrl schema
            return! htmlString docsApp req
          | "OPTIONS", (Some docsUrl, Some docs) when
            sprintf "/%s/$schema" docsUrl = (path req)
            || sprintf "%s/$schema" docsUrl = (path req)
            ->
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

  /// Build HttpResponseData from single builder function and HttpRequestData
  let fromRequestHandler
    (req: HttpRequestData)
    (fn: HttpRequestData -> Task<HttpResponseData option>)
    : Task<HttpResponseData> =
    task {
      match! fn req with
      | Some r -> return r
      | None -> return HttpResponseData(StatusCode = int HttpStatusCode.NotFound, Body = "")
    }
