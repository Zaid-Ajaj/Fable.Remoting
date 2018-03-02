namespace Fable.Remoting.Giraffe

open FSharp.Reflection
open Microsoft.AspNetCore.Http

open Giraffe
open System.IO
open System.Text

open Fable.Remoting.Server

[<AutoOpen>]
module FableGiraffeAdapter =
  /// Legacy logger for backward compatibility
  let mutable logger : (string -> unit) option = None
  /// Legacy ErrorHandler for backward compatibility

  let mutable private onErrorHandler : ErrorHandler option = None

  /// Global error handler that intercepts server errors and decides whether or not to propagate a message back to the client for backward compatibility
  let onError (handler: ErrorHandler) =
        onErrorHandler <- Some handler

  type RemoteBuilder<'a> with
   member builder.Run(options:SharedCE.BuilderOptions) =

    // Get data from request body and deserialize.
    // getResourceFromReq : HttpRequest -> obj
    let getResourceFromReq (ctx : HttpContext) (inputType: System.Type[]) =
        let requestBodyStream = ctx.Request.Body
        use streamReader = new StreamReader(requestBodyStream)
        let requestBodyContent = streamReader.ReadToEnd()
        builder.Deserialize options requestBodyContent inputType

    let handleRequest methodName serverImplementation routePath =
        let inputType = ServerSide.getInputType methodName serverImplementation
        let hasArg =
            match inputType with
            |[|inputType;_|] when inputType.FullName = "Microsoft.FSharp.Core.Unit" -> false
            |_ -> true
        fun (next : HttpFunc) (ctx : HttpContext) ->
            Option.iter (fun logf -> logf (sprintf "Fable.Remoting: Invoking method %s" methodName)) options.Logger
            let requestBodyData =
                match hasArg with
                | true  -> getResourceFromReq ctx inputType
                | false -> [|null|]

            let result = ServerSide.dynamicallyInvoke methodName serverImplementation requestBodyData

            task {
                try
                  let! unwrappedFromAsync = Async.StartAsTask result
                  let serializedResult = builder.Json options unwrappedFromAsync
                  ctx.Response.StatusCode <- 200
                  return! text serializedResult next ctx
                with
                  | ex ->
                     ctx.Response.StatusCode <- 500
                     Option.iter (fun logf -> logf (sprintf "Server error at %s" routePath)) options.Logger
                     match options.ErrorHandler with
                     | Some handler ->
                        let routeInfo = { path = routePath; methodName = methodName }
                        match handler ex routeInfo with
                        | Ignore ->
                            let result = { error = "Server error: ignored"; ignored = true; handled = true }
                            return! text (builder.Json options result) next ctx
                        | Propagate value ->
                            let result = { error = value; ignored = false; handled = true }
                            return! text (builder.Json options result) next ctx
                     | None ->
                        let result = { error = "Server error: not handled"; ignored = false; handled = false }
                        return! text (builder.Json options result) next ctx
            }

    let sb = StringBuilder()
    let typeName = builder.Implementation.GetType().Name
    sb.AppendLine(sprintf "Building Routes for %s" typeName) |> ignore
    builder.Implementation.GetType()
        |> FSharpType.GetRecordFields
        |> Seq.map (fun propInfo ->
        let methodName = propInfo.Name
        let fullPath = options.Builder typeName methodName
        sb.AppendLine(sprintf "Record field %s maps to route %s" methodName fullPath) |> ignore
        POST >=> route fullPath
             >=> warbler (fun _ -> handleRequest methodName builder.Implementation fullPath)
    )
    |> List.ofSeq
    |> fun routes ->
        options.Logger |> Option.iter (fun logf -> string sb |> logf)
        choose routes

  let httpHandlerWithBuilderFor<'t> (implementation : 't) builder =
    remoting implementation {
        with_builder builder
        use_some_logger logger
        use_some_error_handler onErrorHandler
    }

  let httpHandlerFor<'t> (implementation : 't) : HttpHandler =
        httpHandlerWithBuilderFor implementation (sprintf "/%s/%s")