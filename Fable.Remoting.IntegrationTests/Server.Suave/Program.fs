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
}

startWebServer defaultConfig fableWebPart