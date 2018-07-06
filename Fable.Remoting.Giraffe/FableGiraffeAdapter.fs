namespace Fable.Remoting.Giraffe

open Microsoft.AspNetCore.Http

open Giraffe
open System.IO
open System.Threading.Tasks
open Fable.Remoting.Server

[<AutoOpen>]
module Extensions = 
    type HttpContext with 
        member self.GetService<'t>() = self.RequestServices.GetService(typeof<'t>) :?> 't 

module GiraffeUtil = 
    let setResponseBody (response: obj) : HttpHandler = 
        fun (next : HttpFunc) (ctx : HttpContext) -> 
            task {
                let responseBody = DynamicRecord.serialize response 
                return! text responseBody next ctx 
            }

    let setStatusCode (code: int) : HttpHandler = 
        fun (next : HttpFunc) (ctx : HttpContext) -> 
            task {
                ctx.Response.StatusCode <- code 
                return Some ctx 
            }

    let setContentType (contentType: string) : HttpHandler = 
        fun (next : HttpFunc) (ctx : HttpContext) -> 
            task {
                ctx.Response.ContentType <- contentType
                return Some ctx 
            }

    let success value : HttpHandler = 
        setResponseBody value 
        >=> setStatusCode 200 
        >=> setContentType "application/json; charset=utf-8"

    let failure error = 
       setResponseBody error
       >=> setStatusCode 500 
       >=> setContentType "application/json; charset=utf-8"
    
    /// Used to halt the forwarding of the Http context
    let halt : HttpHandler = 
      fun (next : HttpFunc) (ctx : HttpContext) ->
        task { return None }

    let fail (ex: exn) (routeInfo: RouteInfo<HttpContext>) (options: RemotingOptions<HttpContext, 't>) : HttpHandler = 
      fun (next : HttpFunc) (ctx : HttpContext) -> 
        task {
            match options.ErrorHandler with 
            | None -> return! failure (Errors.unhandled routeInfo.methodName) next ctx 
            | Some errorHandler -> 
                match errorHandler ex routeInfo with 
                | Ignore -> return! failure (Errors.ignored routeInfo.methodName) next ctx  
                | Propagate error -> return! failure (Errors.propagated error) next ctx  
        }

    let runFunction func impl options args : HttpHandler = 
      fun (next : HttpFunc) (ctx : HttpContext) -> 
        task {
            let! functionResult = Async.StartAsTask (Async.Catch (DynamicRecord.invokeAsync func impl args)) 
            match functionResult with
            | Choice.Choice1Of2 output -> 
                ctx.Response.StatusCode <- 200
                return! success output next ctx 
            | Choice.Choice2Of2 ex -> 
                ctx.Response.StatusCode <- 500
                let routeInfo = { methodName = func.FunctionName; path = ctx.Request.Path.ToString(); httpContext = ctx }
                return! fail ex routeInfo options next ctx 
        }

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
            | "POST", SingleArgument(input, _) when input = typeof<unit> -> 
                return! runFunction func impl options [|  |] next ctx  
            | "POST", _ ->      
                let requestBodyStream = ctx.Request.Body
                use streamReader = new StreamReader(requestBodyStream)
                let! inputJson = streamReader.ReadToEndAsync()
                let inputArgs = DynamicRecord.createArgsFromJson func inputJson 
                return! runFunction func impl options inputArgs next ctx
            | _ -> 
                return! halt next ctx
      }

module Remoting =

  /// Builds the API using a function that takes the incoming Http context and returns a protocol implementation. You can use the Http context to read information about the incoming request and also use the Http context to resolve dependencies using the underlying dependency injection mechanism.
  let fromContext (f: HttpContext -> 't) options = 
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