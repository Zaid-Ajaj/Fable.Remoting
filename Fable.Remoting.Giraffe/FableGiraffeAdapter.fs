namespace Fable.Remoting.Giraffe

open Microsoft.AspNetCore.Http

open Giraffe
open System.IO
open System.Threading.Tasks
open Fable.Remoting.Server
open Newtonsoft.Json


module GiraffeUtil = 
    let setResponseBody (response: obj) (logger: Option<string -> unit>) : HttpHandler = 
        fun (next : HttpFunc) (ctx : HttpContext) -> 
            task {
                let responseBody = DynamicRecord.serialize response 
                Diagnostics.outputPhase logger responseBody
                return! Giraffe.ResponseWriters.setBodyFromString responseBody next ctx
            }

    let setContentType (contentType: string) : HttpHandler = 
        fun (next : HttpFunc) (ctx : HttpContext) -> 
            task {
                ctx.Response.ContentType <- contentType
                return Some ctx 
            }

    let setJsonBody error logger = 
        setResponseBody error logger 
    
    /// Used to halt the forwarding of the Http context
    let halt : HttpHandler = 
      fun (next : HttpFunc) (ctx : HttpContext) ->
        task { return None }

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

    /// Runs the given dynamic function and catches unhandled exceptions, sending them off to the configured error handler, if any. Returns 200 (OK) status code for successful runs and 500  (Internal Server Error) when an exception is thrown 
    let runFunction func impl options args : HttpHandler = 
      let logger = options.DiagnosticsLogger
      fun (next : HttpFunc) (ctx : HttpContext) -> 
        task {
            Diagnostics.runPhase logger func.FunctionName
            let! functionResult = Async.StartAsTask( (Async.Catch (DynamicRecord.invokeAsync func impl args)), cancellationToken=ctx.RequestAborted)
            match functionResult with
            | Choice.Choice1Of2 output -> 
                let isBinaryOutput = 
                    match func.Type with 
                    | NoArguments t when t = typeof<Async<byte[]>> -> true
                    | SingleArgument (i, t) when t = typeof<Async<byte[]>> -> true
                    | ManyArguments (i, t) when t = typeof<Async<byte[]>> -> true
                    | otherwise -> false
                  
                if isBinaryOutput && ctx.Request.Headers.ContainsKey("x-binary-content") then
                    let binaryResponse = unbox<byte[]> output
                    ctx.Response.StatusCode <- 200
                    ctx.Response.ContentType <- "application/octet-stream"
                    return! Giraffe.ResponseWriters.setBody binaryResponse next ctx
                else  
                    ctx.Response.StatusCode <- 200
                    ctx.Response.ContentType <- "application/json; charset=utf-8"
                    return! setJsonBody output logger next ctx 
            | Choice.Choice2Of2 ex -> 
                ctx.Response.StatusCode <- 500
                ctx.Response.ContentType <- "application/json; charset=utf-8"
                let routeInfo = { methodName = func.FunctionName; path = ctx.Request.Path.ToString(); httpContext = ctx }
                return! fail ex routeInfo options next ctx 
        }

    /// Builds the entire HttpHandler from implementation record, handles routing and dynamic running of record functions
    let buildFromImplementation impl options = 
      let dynamicFunctions = DynamicRecord.createRecordFuncInfo impl
      let typeName = impl.GetType().Name   
      fun (next : HttpFunc) (ctx : HttpContext) -> task {
        let foundFunction = 
          dynamicFunctions 
          |> Map.tryFindKey (fun funcName _ -> ctx.Request.Path.Value = options.RouteBuilder typeName funcName) 
        match foundFunction with 
        | None ->
            match ctx.Request.Method.ToUpper(), options.Docs with  
            | "GET", (Some docsUrl, Some docs) when docsUrl = ctx.Request.Path.Value -> 
                let (Documentation(docsName, docsRoutes)) = docs
                let schema = DynamicRecord.makeDocsSchema (impl.GetType()) docs options.RouteBuilder
                let docsApp = DocsApp.embedded docsName docsUrl schema
                return! htmlString docsApp next ctx
            | "OPTIONS", (Some docsUrl, Some docs) 
                when sprintf "/%s/$schema" docsUrl = ctx.Request.Path.Value
                  || sprintf "%s/$schema" docsUrl = ctx.Request.Path.Value -> 
                let schema = DynamicRecord.makeDocsSchema (impl.GetType()) docs options.RouteBuilder
                let serializedSchema = schema.ToString(Formatting.None)
                return! text serializedSchema next ctx   
            | _ -> 
                return! halt next ctx   
        | Some funcName -> 
            let func = Map.find funcName dynamicFunctions
            match ctx.Request.Method.ToUpper(), func.Type with  
            | ("GET" | "POST"), NoArguments _ ->  
                return! runFunction func impl options [|  |] next ctx  
            | ("GET" | "POST"), SingleArgument(input, _) when input = typeof<unit> ->
                return! runFunction func impl options [|  |] next ctx    
            | "POST", _ ->      
                let requestBodyStream = ctx.Request.Body
                use streamReader = new StreamReader(requestBodyStream)
                let! inputJson = streamReader.ReadToEndAsync()
                let inputArgs = DynamicRecord.tryCreateArgsFromJson func inputJson options.DiagnosticsLogger
                match inputArgs with 
                | Ok inputArgs -> return! runFunction func impl options inputArgs next ctx
                | Error error -> 
                    ctx.Response.StatusCode <- 500
                    return! setJsonBody error options.DiagnosticsLogger next ctx
            | _ -> 
                return! halt next ctx
      }

module Remoting =

  /// Builds a HttpHandler from the given implementation and options 
  let buildHttpHandler (options: RemotingOptions<HttpContext, 't>) = 
    match options.Implementation with 
    | Empty -> GiraffeUtil.halt
    | StaticValue impl -> GiraffeUtil.buildFromImplementation impl options 
    | FromContext createImplementationFrom -> 
        fun (next : HttpFunc) (ctx : HttpContext) -> 
            task {
              let impl = createImplementationFrom ctx
              return! GiraffeUtil.buildFromImplementation impl options next ctx
            } 
