namespace Fable.Remoting.Suave

open Suave
open Suave.Filters
open Suave.Operators
open Suave.Successful

open FSharp.Reflection
open Fable.Remoting.Server
open System.Text

[<AutoOpen>]
module FableSuaveAdapter =
  /// Legacy logger for backward compatibility. Use `use_logger` on the computation expression instead
  let mutable logger : (string -> unit) option = None
  /// Legacy ErrorHandler for backward compatibility. Use `use_error_handler` on the computation expression instead
  let mutable private onErrorHandler : ErrorHandler<HttpContext> option = None

  /// Global error handler that intercepts server errors and decides whether or not to propagate a message back to the client for backward compatibility
  let onError (handler: ErrorHandler<HttpContext>) =
        onErrorHandler <- Some handler
  type RemoteBuilder(implementation) =
   inherit RemoteBuilderBase<HttpContext,WebPart<HttpContext>>()
   
   override builder.Run(options:SharedCE.BuilderOptions<HttpContext>) =
    let getResourceFromReq (req : HttpRequest) (ctx : HttpContext) (inputType: System.Type[]) (genericTypes: System.Type[])  =
        let json = System.Text.Encoding.UTF8.GetString req.rawForm
        builder.Deserialize options json inputType ctx genericTypes

    let handleRequest methodName serverImplementation genericTypes routePath =
        let inputType = ServerSide.getInputType methodName serverImplementation
        let hasArg =
            match inputType with
            |[|inputType;_|] when inputType.FullName = "Microsoft.FSharp.Core.Unit" -> false
            |_ -> true
        fun (req: HttpRequest) (ctx:HttpContext) ->
          let handlerOverride =
            options.CustomHandlers |> Map.tryFind methodName |> Option.map (fun f ->
                    Option.iter (fun logf -> logf (sprintf "Fable.Remoting: Invoking custom handler for method %s" methodName)) options.Logger
                    f ctx) |> Option.flatten
          let (statusCodeOverride, bodyOverride, headersOverride, abort) =
                match handlerOverride with
                |Some ({StatusCode = sc; Body = b; Headers = hd; Abort = abort} as overrides) ->
                    Option.iter (fun logf -> logf (sprintf "Fable.Remoting: Overrides: %0A" overrides)) options.Logger
                    (sc,b,hd,abort)
                |None -> (None, None, None, false)
          if abort then
            async {return None}
          else
            let setHeaders =
                  match headersOverride with
                  |Some headers ->
                    Option.iter (fun logf -> logf "Fable.Remoting: Setting headers") options.Logger
                    headers |> Map.fold (fun ctx k v -> ctx >=> Writers.addHeader k v) (fun ctx -> async {return Some ctx})
                  |None -> (fun ctx -> async {return Some ctx})
            let setStatus code =
                    match statusCodeOverride with
                    |Some statusCode ->
                        Option.iter (fun logf -> logf (sprintf "Fable.Remoting: Setting status %i" statusCode)) options.Logger
                        fun ctx -> async {return Some {ctx with response = {ctx.response with status = {ctx.response.status with code = statusCode}}}}
                    |None -> Writers.setStatus code
            match bodyOverride with
            |Some b -> (setHeaders >=> setStatus HTTP_200 >=> (fun ctx -> async {return Some {ctx with response = {ctx.response with content = HttpContent.Bytes (System.Text.Encoding.UTF8.GetBytes b)}}})) ctx
            |None ->    
                let flow code response  = 
                    setHeaders
                    >=>
                    OK response                     
                    >=> setStatus code
                    >=> Writers.setMimeType "application/json; charset=utf-8"
                Option.iter (fun logf -> logf (sprintf "Fable.Remoting: Invoking method %s" methodName)) options.Logger
                let requestBodyData =
                    // if input is unit
                    // then don't bother getting any input from request
  