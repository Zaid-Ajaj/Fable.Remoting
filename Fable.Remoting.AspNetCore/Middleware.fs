namespace Fable.Remoting.AspNetCore

open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http

open System.IO
open System.Threading.Tasks 
open Fable.Remoting.Server
open FSharp.Control.Tasks
open Newtonsoft.Json

type HttpFuncResult = Task<HttpContext option>
type HttpFunc = HttpContext -> HttpFuncResult
type HttpHandler = HttpFunc -> HttpFunc

[<AutoOpen>]
module Extensions = 
    type HttpContext with 
        member self.GetService<'t>() = self.RequestServices.GetService(typeof<'t>) :?> 't 


/// The parts from Giraffe needed to simplify the middleware implementation 
module internal Middleware = 
    let writeStringAsync (input: string) (ctx: HttpContext) (logger: Option<string -> unit>) = 
        task {
            Diagnostics.outputPhase logger input
            let bytes = System.Text.Encoding.UTF8.GetBytes(input)
            do! ctx.Response.Body.WriteAsync(bytes, 0, bytes.Length)
            return Some ctx
        }

    let text (input: string) = 
        fun (next : HttpFunc) (ctx : HttpContext) ->
            task {
                let bytes = System.Text.Encoding.UTF8.GetBytes(input)
                do! ctx.Response.Body.WriteAsync(bytes, 0, bytes.Length)
                return Some ctx
            }

    let compose (handler1 : HttpHandler) (handler2 : HttpHandler) : HttpHandler =
        fun (final : HttpFunc) ->
            let func = final |> handler2 |> handler1
            fun (ctx : HttpContext) ->
                match ctx.Response.HasStarted with
                | true  -> final ctx
                | false -> func ctx
    let (>=>) = compose
    let setResponseBody (response: obj) logger : HttpHandler = 
        fun (next : HttpFunc) (ctx : HttpContext) -> 
            task {
                let responseBody = DynamicRecord.serialize response 
                return! writeStringAsync responseBody ctx logger
            }
    
    /// Sets the content type of the Http response
    let setContentType (contentType: string) : HttpHandler = 
        fun (next : HttpFunc) (ctx : HttpContext) -> 
            task {
                ctx.Response.ContentType <- contentType
                return Some ctx 
            }

    /// Sets the body of the response to type of JSON
    let setBody value logger : HttpHandler = 
        setResponseBody value logger
        >=> setContentType "application/json; charset=utf-8"
    
    /// Used to forward of the Http context
    let halt : HttpHandler = 
      fun (next : HttpFunc) (ctx : HttpContext) ->
        task { return None }

    let fail (ex: exn) (routeInfo: RouteInfo<HttpContext>) (options: RemotingOptions<HttpContext, 't>) : HttpHandler = 
      let logger = options.DiagnosticsLogger
      fun (next : HttpFunc) (ctx : HttpContext) -> 
        task {
            match options.ErrorHandler with 
            | None -> return! setBody (Errors.unhandled routeInfo.methodName) logger next ctx 
            | Some errorHandler -> 
                match errorHandler ex routeInfo with 
                | Ignore -> return! setBody (Errors.ignored routeInfo.methodName) logger next ctx  
                | Propagate error -> return! setBody (Errors.propagated error) logger next ctx  
        }

    let runFunction func impl options args : HttpHandler = 
      let logger = options.DiagnosticsLogger
      fun (next : HttpFunc) (ctx : HttpContext) -> 
        task {
            Diagnostics.runPhase logger func.FunctionName
            let! functionResult = Async.StartAsTask (Async.Catch (DynamicRecord.invokeAsync func impl args)) 
            match functionResult with
            | Choice.Choice1Of2 output -> 
                ctx.Response.StatusCode <- 200
                return! setBody output logger next ctx 
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
        | None -> 
            // route didn't match with any of the functions
            // try match route with docs application
            match ctx.Request.Method.ToUpper(), options.Docs with  
            | "GET", (Some docsUrl, Some docs) when docsUrl = ctx.Request.Path.Value -> 
                let (Documentation(docsName, docsRoutes)) = docs
                let schema = DynamicRecord.makeDocsSchema (impl.GetType()) docs options.RouteBuilder
                let docsApp = DocsApp.embedded docsName docsUrl schema
                return! (text docsApp >=> setContentType "text/html") next ctx
            | "OPTIONS", (Some docsUrl, Some docs) 
                when sprintf "/%s/$schema" docsUrl = ctx.Request.Path.Value
                  || sprintf "%s/$schema" docsUrl = ctx.Request.Path.Value -> 
                let schema = DynamicRecord.makeDocsSchema (impl.GetType()) docs options.RouteBuilder
                let serializedSchema = schema.ToString(Formatting.None)
                return! (text serializedSchema >=> setContentType "application/json; charset=utf-8") next ctx   
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
                    return! setBody error options.DiagnosticsLogger next ctx
            | _ -> 
                return! halt next ctx
      }

type RemotingMiddleware<'t>(next          : RequestDelegate,
                            options       : RemotingOptions<HttpContext, 't>) =
    
    do if isNull next then nullArg "next"
    member __.Invoke (ctx : HttpContext) =
      let handler = 
          match options.Implementation with 
          | Empty -> Middleware.halt  
          | StaticValue impl -> Middleware.buildFromImplementation impl options  
          | FromContext createImplementation -> 
              let impl = createImplementation ctx 
              Middleware.buildFromImplementation impl options
      let func : HttpFunc = handler (Some >> Task.FromResult)
      task {
          let! result = func ctx
          if (result.IsNone) then return! next.Invoke ctx
      }

[<AutoOpen>]
module AppBuilderExtensions = 
    type IApplicationBuilder with
      member this.UseRemoting(options:RemotingOptions<HttpContext, 't>) =
          this.UseMiddleware<RemotingMiddleware<'t>> options |> ignore