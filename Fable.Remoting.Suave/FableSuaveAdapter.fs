namespace Fable.Remoting.Suave

open Suave
open Suave.Operators
open Fable.Remoting.Server

module SuaveUtil = 
  
  let outputContent (json: string) = 
    HttpContent.Bytes (System.Text.Encoding.UTF8.GetBytes(json))  

  let setResponseBody (asyncResult: obj) =
    fun (ctx: HttpContext) -> async {
      let json = DynamicRecord.serialize asyncResult 
      return Some { ctx with response = { ctx.response with content = outputContent json  } } 
    }

  /// Sets the status code of the response
  let setStatusCode code =
    fun ctx -> async { 
      let nextStatus = { ctx.response.status with code = code }
      let nextResponse = { ctx.response with status = nextStatus }
      return Some { ctx with response = nextResponse } 
    } 

  /// Returns output from dynamic functions as JSON
  let success value = 
    setResponseBody value 
    >=> setStatusCode 200
    >=> Writers.setMimeType "application/json; charset=utf-8"
  
  /// Used to halt the forwarding of the Http context
  let halt : WebPart = 
    fun (context: HttpContext) -> 
      async { return None }

  let sendError error = 
    setResponseBody error
    >=> setStatusCode 500 
    >=> Writers.setMimeType "application/json; charset=utf-8"

  let fail (ex: exn) (routeInfo: RouteInfo<HttpContext>) (options: RemotingOptions<HttpContext, 't>) : WebPart = 
    fun (context: HttpContext) -> async {
      match options.ErrorHandler with 
      | None -> return! sendError (Errors.unhandled routeInfo.methodName) context 
      | Some errorHandler -> 
          match errorHandler ex routeInfo with 
          | Ignore -> return! sendError (Errors.ignored routeInfo.methodName) context 
          | Propagate error -> return! sendError (Errors.propagated error) context 
    }

  let runFunction func impl options args : WebPart = 
    fun context -> async {
      let! functionResult = Async.Catch (DynamicRecord.invokeAsync func impl args) 
      match functionResult with
      | Choice.Choice1Of2 output -> return! success output context 
      | Choice.Choice2Of2 ex -> 
        let routeInfo = { methodName = func.FunctionName; path = context.request.path; httpContext = context }
        return! fail ex routeInfo options context
    }

  let buildFromImplementation impl options = 
    let dynamicFunctions = DynamicRecord.createRecordFuncInfo impl
    let typeName = impl.GetType().Name   
    fun (context: HttpContext) -> async {
      let foundFunction = 
        dynamicFunctions 
        |> Map.tryFindKey (fun funcName _ -> context.request.path = options.RouteBuilder typeName funcName) 
      match foundFunction with 
      | None -> return! halt context   
      | Some funcName -> 
          let func = Map.find funcName dynamicFunctions
          match context.request.method, func.Type with  
          | HttpMethod.GET, NoArguments _ ->  
              return! runFunction func impl options [|  |] context  
          | HttpMethod.GET, SingleArgument(input, _) when input = typeof<unit> ->
              return! runFunction func impl options [|  |] context    
          | HttpMethod.POST, SingleArgument(input, _) when input = typeof<unit> -> 
              return! runFunction func impl options [|  |] context  
          | HttpMethod.POST, _ ->      
              let inputJson = System.Text.Encoding.UTF8.GetString(context.request.rawForm)
              let inputArgs = DynamicRecord.createArgsFromJson func inputJson 
              return! runFunction func impl options inputArgs context
          | _ -> 
              return! halt context
    }

module Remoting = 
  
  /// Builds the API using a function that takes the incoming Http context and returns a protocol implementation. You can use the Http context to read information about the incoming request and also use the Http context to resolve dependencies using the underlying dependency injection mechanism.
  let fromContext (f: HttpContext -> 't) options = 
    { options with Implementation = FromContext f } 

  /// Builds a WebPart from the given implementation and options  
  let buildWebPart (options: RemotingOptions<HttpContext, 't>) : WebPart = 
    match options.Implementation with 
    | Empty -> SuaveUtil.halt
    | StaticValue impl -> SuaveUtil.buildFromImplementation impl options 
    | FromContext createImplementationFrom -> 
        Writers.setUserData "Fable.Remoting.IoCContainer" options.IoCContainer 
        >=> fun (context: HttpContext) -> async {
          let impl = createImplementationFrom context 
          return! SuaveUtil.buildFromImplementation impl options context
        }