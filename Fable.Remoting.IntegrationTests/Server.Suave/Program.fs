open Suave
open Fable.Remoting.Server
open Fable.Remoting.Suave
open ServerImpl
open SharedTypes

let fableWebPart = remoting server {
    with_builder routeBuilder
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
    with_builder versionTestBuilder
    use_custom_handler_for "v4" (isVersion "4")
    use_custom_handler_for "v3" (isVersion "3")
    use_custom_handler_for "v2" (isVersion "2")
  }

startWebServer defaultConfig (choose [fableWebPart;versionTestWebPart])