namespace Fable.Remoting.Suave

open Suave
open Suave.Operators
open Fable.Remoting.Server
open Newtonsoft.Json

module SuaveUtil = 
  
  let outputContent (json: string) = 
    HttpContent.Bytes (System.Text.Encoding.UTF8.GetBytes(json))  

  let setResponseBody (asyncResult: obj) (logger: Option<string -> unit>) =
    fun (ctx: HttpContext) -> async {
      let json = DynamicRecord.serialize asyncResult 
      Diagnostics.outputPhase logger json  
      return Some { ctx with response = { ctx.response with content = outputContent json  } } 
    }

  let setBinaryResponseBody (content: byte[]) = 
    fun (ctx: HttpContext) -> async {
      return Some { ctx with response = { ctx.response with content = HttpContent.Bytes content  } } 
    }

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

  /// Returns output from dynamic functions as binary content
  let successBytes value = 
    setBinaryResponseBody value 
    >=> setStatusCode 200
    >=> Writers.setMimeType "application/octet-stream"
  
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

  /// Runs the given dynamic function and catches unhandled exceptions, sending them off to the configured error handler, if any. Returns 200 (OK) status code for successful runs and 500  (Internal Server Error) when an exception is thrown 
  let runFunction func impl options args : WebPart = 
    let logger = options.DiagnosticsLogger
    fun context -> async {
      Diagnostics.runPhase logger func.FunctionName
      let! functionResult = Async.Catch (DynamicRecord.invokeAsync func impl args) 
      match functionResult with
      | Choice.Choice1Of2 output ->
          let isBinaryOutput = 
            match func.Type with 
            | NoArguments t when t = typeof<Async<byte[]>> -> true
            | SingleArgument (i, t) when t = typeof<Async<byte[]>> -> true
            | ManyArguments (i, t) when t = typeof<Async<byte[]>> -> true
            | otherwise -> false
             
          let isFableProxyRequest = 
            context.request.headers
            |> Map.ofList
            |> Map.containsKey "x-remoting-proxy" 

          if isBinaryOutput && isFableProxyRequest then 
            let binaryContent = unbox<byte[]> output
            return! successBytes binaryContent context
          else
            return! success output logger context 
      | Choice.Choice2Of2 ex -> 
          let routeInfo = { methodName = func.FunctionName; path = context.request.path; httpContext = context }
          return! fail ex routeInfo options context
    }

  /// Builds the entire WebPart from implementation record, handles routing and dynamic running of record functions
  let buildFromImplementation impl options = 
    let typ = impl.GetType()
    let dynamicFunctions = DynamicRecord.createRecordFuncInfo typ
    fun (context: HttpContext) -> async {
      let foundFunction = 
        dynamicFunctions 
        |> Map.tryFindKey (fun funcName _ -> context.request.path = options.RouteBuilder typ.Name funcName) 
      match foundFunction with 
      | None -> 
          match context.request.method, options.Docs with 
          | HttpMethod.GET, (Some docsUrl, Some docs) when docsUrl = context.request.path -> 
              let (Documentation(docsName, docsRoutes)) = docs
              let schema = DynamicRecord.makeDocsSchema typ docs options.RouteBuilder
              let docsApp = DocsApp.embedded docsName docsUrl schema
              return! html docsApp context
          | HttpMethod.OPTIONS, (Some docsUrl, Some docs) 
                when sprintf "/%s/$schema" docsUrl = context.request.path
                  || sprintf "%s/$schema" docsUrl = context.request.path ->
              let schema = DynamicRecord.makeDocsSchema typ docs options.RouteBuilder
              let serializedSchema =  schema.ToString(Formatting.None)
              return! success serializedSchema None context   
          | _ -> 
              return! halt context     
      | Some funcName -> 
          let contentIsBinaryEncoded = 
            context.request.headers
            |> List.tryFind (fun (key, _) -> key.ToLowerInvariant() = "content-type")
            |> Option.map (fun (_, value) -> value)
            |> function 
              | Some "application/octet-stream" -> true 
              | otherwise -> false 
 
          let func = Map.find funcName dynamicFunctions
          
          match context.request.method, func.Type with  
          | (HttpMethod.GET | HttpMethod.POST), NoArguments _ ->  
              return! runFunction func impl options [|  |] context  
          
          | (HttpMethod.GET | HttpMethod.POST), SingleArgument(input, _) when input = typeof<unit> ->
              return! runFunction func impl options [|  |] context   

          | HttpMethod.POST, SingleArgument(input, _) when input = typeof<byte[]> && contentIsBinaryEncoded ->
              let inputBytes = context.request.rawForm
              let inputArgs = [| box inputBytes |]
              return! runFunction func impl options inputArgs context
          
          | HttpMethod.POST, _ ->      
              let inputJson = System.Text.Encoding.UTF8.GetString(context.request.rawForm)
              let inputArgs = DynamicRecord.tryCreateArgsFromJson func inputJson options.DiagnosticsLogger
              match inputArgs with 
              | Ok inputArgs -> return! runFunction func impl options inputArgs context
              | Result.Error error -> return! sendError error options.DiagnosticsLogger context  
          | _ -> 
              return! halt context
    }

module Remoting = 
  
  /// Builds the API using a function that takes the incoming Http context and returns a protocol implementation. You can use the Http context to read information about the incoming request and also use the Http context to resolve dependencies using the underlying dependency injection mechanism.
  let fromContext (f: HttpContext -> 't) (options: RemotingOptions<HttpContext, 't>) : RemotingOptions<HttpContext, 't> = 
    { options with Implementation = FromContext f } 

  /// Builds a WebPart from the given implementation and options  
  let buildWebPart (options: RemotingOptions<HttpContext, 't>) : WebPart = 
    match options.Implementation with 
    | Empty -> SuaveUtil.halt
    | StaticValue impl -> SuaveUtil.buildFromImplementation impl options 
    | FromContext createImplementationFrom -> 
        fun (context: HttpContext) -> async {
          let impl = createImplementationFrom context 
          return! SuaveUtil.buildFromImplementation impl options context
        }