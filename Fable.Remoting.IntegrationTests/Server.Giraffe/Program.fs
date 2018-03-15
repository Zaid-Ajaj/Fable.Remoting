open System
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Server.Kestrel
open Microsoft.Extensions.Logging
open Giraffe
open ServerImpl
open SharedTypes
open Fable.Remoting.Server
open Fable.Remoting.Giraffe

let errorHandler (ex : Exception) (logger : ILogger) =
    logger.LogError(EventId(), ex, "An unhandled exception has occurred while executing the request.")
    clearResponse >=> setStatusCode 500 >=> text ex.Message

let webApp = remoting server {
    with_builder routeBuilder
    use_logger (printfn "%s")
    use_custom_handler_for "overriddenFunction" (fun _ -> ResponseOverride.Default.withBody "42" |> Some)
    use_custom_handler_for "customStatusCode" (fun _ -> ResponseOverride.Default.withStatusCode 204 |> Some)
}

let isVersion v (ctx:HttpContext) =
  match ctx.TryGetRequestHeader "version" with
  |Some value when value = v ->
    None
  |_ -> Some {ResponseOverride.Default with Abort = true}
let versionTestWebApp =
  remoting versionTestServer {
    use_logger (printfn "%s")
    with_builder versionTestBuilder
    use_custom_handler_for "v4" (isVersion "4")
    use_custom_handler_for "v3" (isVersion "3")
    use_custom_handler_for "v2" (isVersion "2")
  }

let contextTestWebApp =
    remoting {callWithCtx = fun (ctx:HttpContext) -> async{return ctx.Request.Path.Value}} {
        use_logger (printfn "%s")
        with_builder routeBuilder
    }

let configureApp (app : IApplicationBuilder) =
    app.UseGiraffeErrorHandler(errorHandler)
       .UseGiraffe(choose [webApp;versionTestWebApp;contextTestWebApp])


[<EntryPoint>]
let main _ =
    WebHostBuilder()
        .UseKestrel()
        .Configure(Action<IApplicationBuilder> configureApp)
        .UseUrls("http://localhost:8080")
        .Build()
        .Run()
    0