open Suave
open Fable.Remoting.Server
open Fable.Remoting.Suave
open ServerImpl
open SharedTypes
open System.IO
open Suave.Files
open Suave.Operators
open Suave.Filters
let fableWebPart = remoting server {
    use_route_builder routeBuilder
    use_logger (printfn "%s")
    use_error_handler (fun ex routeInfo ->
      printfn "Error at: %A" routeInfo
      Propagate ex.Message)
    use_custom_handler_for "overriddenFunction" (fun _ -> ResponseOverride.Default.withBody "42" |> Some)
    use_custom_handler_for "customStatusCode" (fun _ -> ResponseOverride.Default.withStatusCode 204 |> Some)
}

let isVersion v (ctx:HttpContext) =
  if ctx.request.headers |> List.contains ("version",v) then
    None
  else
    Some {ResponseOverride.Default with Abort = true}
let versionTestWebPart =
  remoting versionTestServer {
    use_logger (printfn "%s")
    use_route_builder versionTestBuilder
    use_custom_handler_for "v4" (isVersion "4")
    use_custom_handler_for "v3" (isVersion "3")
    use_custom_handler_for "v2" (isVersion "2")
  }

let contextTestWebApp =
    remoting {callWithCtx = fun (ctx:HttpContext) -> async{return ctx.request.path}} {
        use_logger (printfn "%s")
        use_route_builder routeBuilder
    }

let webApp = 
  choose [ GET >=> browseHome
           fableWebPart 
           versionTestWebPart
           contextTestWebApp ]

let rec findRoot dir =
    if File.Exists(System.IO.Path.Combine(dir, "paket.dependencies"))
    then dir
    else
        let parent = Directory.GetParent(dir)
        if isNull parent then
            failwith "Couldn't find root directory"
        findRoot parent.FullName

let root = findRoot (Directory.GetCurrentDirectory())
let (</>) x y = Path.Combine(x, y)

let client = root </> "Fable.Remoting.IntegrationTests" </> "client-dist"

let config = { defaultConfig with homeFolder = Some client }

startWebServer config webApp