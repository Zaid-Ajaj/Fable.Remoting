open Suave 
open Fable.Remoting.Suave
open ServerImpl
open SharedTypes

let fableWebPart = FableSuaveAdapter.webPartWithBuilderFor implementation routeBuilder

FableSuaveAdapter.onError <| fun ex routeInfo ->
    printfn "Error at: %A" routeInfo
    Propagate ex.Message

[<EntryPoint>]
let main argv = 
    FableSuaveAdapter.logger <- Some (printfn "%s")
    printfn "%A" argv
    startWebServer defaultConfig fableWebPart
    0 