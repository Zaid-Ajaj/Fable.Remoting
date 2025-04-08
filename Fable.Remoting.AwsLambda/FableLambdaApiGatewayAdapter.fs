namespace Fable.Remoting.AwsLambda.ApiGateway

open System
open System.Net
open System.Text
open System.Threading.Tasks
open System.IO
open Fable.Remoting.Server
open Fable.Remoting.Server.Proxy
open Amazon.Lambda.APIGatewayEvents
open Newtonsoft.Json

type HttpRequestData = APIGatewayProxyRequest
type HttpResponseData = APIGatewayProxyResponse

module private FuncsUtil =

  let private htmlString (html: string) (req: HttpRequestData) : Task<HttpResponseData option> =
    task {
      let resp = HttpResponseData(
        StatusCode = int HttpStatusCode.OK, 
        Body = html, 
        Headers = dict [("Content-Type", "text/html; charset=utf-8")]
      )
      return Some resp
    }

  let text (str: string) (req: HttpRequestData) : Task<HttpResponseData option> =
    task {
      let resp = HttpResponseData(
        StatusCode = int HttpStatusCode.OK, 
        Body = str,
        Headers = dict [("Content-Type", "text/plain; charset=utf-8")]
      )
      return Some resp
    }

  let private path (r: HttpRequestData) = r.Path

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
      res.Headers <- dict [("Content-Type", "application/json; charset=utf-8" )]
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

    let rmsManager = getRecyclableMemoryStreamManager options

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
            HttpVerb = req.HttpMethod
            InputContentType = contentType
            Output = output }

        match! proxy props with
        | Success isBinaryOutput ->
          let resp = HttpResponseData(StatusCode = int HttpStatusCode.OK)

          if isBinaryOutput && isProxyHeaderPresent then
            resp.Headers <- dict [("Content-Type", "application/octet-stream")]
          elif options.ResponseSerialization.IsJson then
            resp.Headers <- dict [("Content-Type", "application/json; charset=utf-8")]
          else
            resp.Headers <- dict [("Content-Type", "application/vnd.msgpack")]

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
          match req.HttpMethod.ToUpper(), options.Docs with
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
