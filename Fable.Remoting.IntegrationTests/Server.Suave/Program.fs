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

startWebServer defaultConfig fableWebPart