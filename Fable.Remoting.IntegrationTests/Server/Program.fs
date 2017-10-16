open Suave 
open Fable.Remoting.Suave
open ServerImpl
open SharedTypes

let fableWebPart = FableSuaveAdapter.webPartWithBuilderFor implementation routeBuilder

[<EntryPoint>]
let main argv = 
    FableSuaveAdapter.logger <- Some (printfn "%s")
    printfn "%A" argv
    startWebServer defaultConfig fableWebPart
    0 