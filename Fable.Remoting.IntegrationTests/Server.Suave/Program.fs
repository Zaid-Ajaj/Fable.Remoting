open Suave
open Fable.Remoting.Server
open Fable.Remoting.Suave
open ServerImpl
open SharedTypes
open System.IO
open Suave.Files
open Suave.Operators
open Suave.Filters

let fableWebPart = 
  Remoting.createApi()
  |> Remoting.fromValue server
  |> Remoting.withRouteBuilder routeBuilder 
  |> Remoting.withErrorHandler (fun ex routeInfo -> Propagate ex.Message) 
  |> Remoting.withDiagnosticsLogger (printfn "%s")
  |> Remoting.buildWebPart 

let simpleServerWebPart =   
  Remoting.createApi()
  |> Remoting.fromValue simpleServer 
  |> Remoting.withRouteBuilder routeBuilder
  |> Remoting.withDiagnosticsLogger (printfn "%s")
  |> Remoting.buildWebPart 

let webApp = 
  choose [ GET >=> browseHome
           fableWebPart  
           simpleServerWebPart ]

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