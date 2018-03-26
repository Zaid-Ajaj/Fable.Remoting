open Microsoft.AspNetCore.Http
open Saturn
open Giraffe
open ServerImpl
open SharedTypes
open Fable.Remoting.Server
open Fable.Remoting.Giraffe

let webApp = remoting server {
    use_route_builder routeBuilder
    use_logger (printfn "%s")
    use_custom_handler_for "overriddenFunction" (fun _ -> ResponseOverride.Default.withBody "42" |> Some)
    use_custom_handler_for "customStatusCode" (fun _ -> ResponseOverride.Default.withStatusCode 204 |> Some)
}

let isVersion v (ctx:HttpContext) =
  match ctx.TryGetRequestHeader "version" with
  |Some value when value = v ->
    None
  |_ -> Some { ResponseOverride.Default with Abort = true } 

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
        use_route_builder routeBuilder
    }

let saturnApp = 
  choose [webApp; versionTestWebApp; contextTestWebApp]

let app = application {
    url "http://127.0.0.1:8080/"
    router saturnApp
}


run app