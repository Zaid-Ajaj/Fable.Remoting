namespace Fable.Remoting.Suave

open Suave
open Suave.Operators
open Fable.Remoting.Server
open Newtonsoft.Json
open System.IO
open Fable.Remoting.Server.Proxy

module SuaveUtil = 
  
  let outputContent (json: string) = 
    HttpContent.Bytes (System.Text.Encoding.UTF8.GetBytes(json))  

  let setResponseBody (asyncResult: obj) (logger: Option<string -> unit>) =
    fun (ctx: HttpContext) -> async {
      use ms = new MemoryStream ()
      jsonSerialize asyncResult ms
      let json = System.Text.Encoding.UTF8.GetString (ms.ToArray ())
      Diagnostics.outputPhase logger json  
      return Some { ctx with response = { ctx.response with content = outputContent json  } } 
    }

  let setBinaryResponseBody (content: byte[]) statusCode mimeType = 
    fun (ctx: HttpContext) -> async {
      return Some { ctx with response = { ctx.response with content = HttpContent.Bytes content; status = { ctx.response.status with code = statusCode } } } 
    }
    >=> Writers.setMimeType mimeType

  /// Sets the status code of the response
  let setStatusCode code =
    fun ctx -> async { 
      let nextStatus = { ctx.response.status with code = code }
      let nextResponse = { ctx.response with status = nextStatus }
      return Some { ctx with response = nextResponse } 
    } 

  /// Returns output from dynamic functions as JSON
  let success value (logger: Option<string -> unit>) = 
    setResponseBody value logger 
    >=> setStatusCode 200
    >=> Writers.setMimeType "application/json; charset=utf-8"

  let html content : WebPart = 
    fun ctx -> async {
      return Some { ctx with response = { ctx.response with content = outputContent content  } } 
    } 
    >=> setStatusCode 200
    >=> Writers.setMimeType "text/html; charset=utf-8"

  /// Used to halt the forwarding of the Http context
  let halt : WebPart = 
    fun (_: HttpContext) -> 
      async { return None }

  /// Sets the error object in the response and makes the status code 500 (Internal Server Error)
  let sendError error logger = 
    setResponseBody error logger
    >=> setStatusCode 500 
    >=> Writers.setMimeType "application/json; charset=utf-8"

  /// Handles thrown exceptions
  let fail (ex: exn) (routeInfo: RouteInfo<HttpContext>) (options: RemotingOptions<HttpContext, 't>) : WebPart = 
    let logger = options.DiagnosticsLogger
    fun (context: HttpContext) -> async {
      match options.ErrorHandler with 
      | None -> return! sendError (Errors.unhandled routeInfo.methodName) logger context 
      | Some errorHandler -> 
          match errorHandler ex routeInfo with 
          | Ignore -> return! sendError (Errors.ignored routeInfo.methodName) logger context 
          | Propagate error -> return! sendError (Errors.propagated error) logger context 
    }

  let buildFromImplementation<'impl> (impl: 'impl) (options: RemotingOptions<HttpContext, 'impl>) =
      let proxy = makeApiProxy options
      
      fun (ctx: HttpContext) -> async {
          use ms = new MemoryStream ()
          use inp = new MemoryStream (ctx.request.rawForm)
          let isRemotingProxy = ctx.request.headers |> List.exists (fun x -> fst x = "x-remoting-proxy")
          let isContentBinaryEncoded = 
              ctx.request.headers
              |> List.tryFind (fun (key, _) -> key.ToLowerInvariant() = "content-type")
              |> Option.map (fun (_, value) -> value)
              |> function 
                | Some "application/octet-stream" -> true 
                | otherwise -> false
          let props = { Implementation = impl; EndpointName = ctx.request.path; Input = inp; Output = ms; HttpVerb = ctx.request.rawMethod.ToUpper ();
              IsContentBinaryEncoded = isContentBinaryEncoded; IsProxyHeaderPresent = isRemotingProxy }

          match! proxy props with
          | Success isBinaryOutput ->
              let mimeType =
                  if isBinaryOutput && isRemotingProxy then
                      "application/octet-stream"
                  elif options.ResponseSerialization = SerializationType.Json then
                      "application/json; charset=utf-8"
                  else
                      "application/msgpack"

              return! setBinaryResponseBody (ms.ToArray ()) 200 mimeType ctx
          | Exception (e, functionName) ->
              let routeInfo = { methodName = functionName; path = ctx.request.path; httpContext = ctx }
              return! fail e routeInfo options ctx
          | InvalidHttpVerb ->
              return! halt ctx
          | EndpointNotFound ->
              match ctx.request.method, options.Docs with 
              | HttpMethod.GET, (Some docsUrl, Some docs) when docsUrl = ctx.request.path -> 
                  let (Documentation(docsName, docsRoutes)) = docs
                  let schema = Docs.makeDocsSchema typeof<'impl> docs options.RouteBuilder
                  let docsApp = DocsApp.embedded docsName docsUrl schema
                  return! html docsApp ctx
              | HttpMethod.OPTIONS, (Some docsUrl, Some docs) 
                    when sprintf "/%s/$schema" docsUrl = ctx.request.path
                      || sprintf "%s/$schema" docsUrl = ctx.request.path ->
                  let schema = Docs.makeDocsSchema typeof<'impl> docs options.RouteBuilder
                  let serializedSchema =  schema.ToString(Formatting.None)
                  return! success serializedSchema None ctx   
              | _ -> 
                  return! halt ctx
      }

module Remoting = 

  /// Builds a WebPart from the given implementation and options  
  let buildWebPart (options: RemotingOptions<HttpContext, 't>) =
      match options.Implementation with
      | Empty -> SuaveUtil.halt
      | StaticValue impl -> SuaveUtil.buildFromImplementation impl options
      | FromContext createImplementationFrom ->
          fun (ctx : HttpContext) ->
              let impl = createImplementationFrom ctx
              SuaveUtil.buildFromImplementation impl options ctx
