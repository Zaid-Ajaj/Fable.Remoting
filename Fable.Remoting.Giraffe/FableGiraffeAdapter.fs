namespace Fable.Remoting.Giraffe

open Microsoft.AspNetCore.Http

open Giraffe
open System.IO
open System.Threading.Tasks
open Fable.Remoting.Server


module GiraffeUtil = 
    let setResponseBody (response: obj) (logger: Option<string -> unit>) : HttpHandler = 
        fun (next : HttpFunc) (ctx : HttpContext) -> 
            task {
                let responseBody = DynamicRecord.serialize response 
                Diagnostics.outputPhase logger responseBody
                return! text responseBody next ctx 
            }

    let setContentType (contentType: string) : HttpHandler = 
        fun (next : HttpFunc) (ctx : HttpContext) -> 
            task {
                ctx.Response.ContentType <- contentType
                return Some ctx 
            }

    let setJsonBody error logger = 
       setResponseBody error logger 
       >=> setContentType "application/json; charset=utf-8"
    
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
            let! functionResult = Async.StartAsTask (Async.Catch (DynamicRecord.invokeAsync func impl args)) 
            match functionResult with
            | Choice.Choice1Of2 output -> 
                ctx.Response.StatusCode <- 200
                return! setJsonBody output logger next ctx 
            | Choice.Choice2Of2 ex -> 
                ctx.Response.StatusCode <- 500
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
        | None -> return! halt next ctx   
        | Some funcName -> 
            let func = Map.find funcName dynamicFunctions
            match ctx.Request.Method.ToUpper(), func.Type with  
            | "GET", NoArguments _ ->  
                return! runFunction func impl options [|  |] next ctx  
            | "GET", SingleArgument(input, _) when input = typeof<unit> ->
                return! runFunction func impl options [|  |] next ctx    
            | "POST", NoArguments _ ->
                return! runFunction func impl options [|  |] next ctx
            | "POST", SingleArgument(input, _) when input = typeof<unit> -> 
                return! runFunction func impl options [|  |] next ctx  
            | "POST", _ ->      
                let requestBodyStream = ctx.Request.Body
                use streamReader = new StreamReader(requestBodyStream)
                let! inputJson = streamReader.ReadToEndAsync()
                let inputArgs = DynamicRecord.createArgsFromJson func inputJson options.DiagnosticsLogger
                return! runFunction func impl options inputArgs next ctx
            | _ -> 
                return! halt next ctx
      }

module Remoting =

  /// Builds the API using a function that takes the incoming Http context and returns a protocol implementation. You can use the Http context to read information about the incoming request and also use the Http context to resolve dependencies using the underlying dependency injection mechanism.
  let fromContext (f: HttpContext -> 't) (options: RemotingOptions<HttpContext, 't>) = 
    { options with Implementation = FromContext f } 

  /// Builds a WebPart from the given implementation and options 
  let buildHttpHanlder (options: RemotingOptions<HttpContext, 't>) = 
    match options.Implementation with 
    | Empty -> GiraffeUtil.halt
    | StaticValue impl -> GiraffeUtil.buildFromImplementation impl options 
    | FromContext createImplementationFrom -> 
        fun (next : HttpFunc) (ctx : HttpContext) -> 
            task {
              let impl = createImplementationFrom ctx
              return! GiraffeUtil.buildFromImplementation impl options next ctx
            } 