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
    use_custom_handler_for "overriddenFunction"
      (fun _ -> Some {Body=Some "42";Headers=None;StatusCode=None;Abort=false})
}

startWebServer defaultConfig fableWebPart