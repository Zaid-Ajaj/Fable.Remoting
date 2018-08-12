open Suave
open Fable.Remoting.Server
open Fable.Remoting.Suave
open ServerImpl
open SharedTypes
open System.IO
open Suave.Files
open Suave.Operators
open Suave.Filters

let docs = Docs.createFor<IServer>()


let serverDocs = 
  let inputMap = Map.ofList [ "input one", 10 ]
  Remoting.documentation "Server Docs" [
    docs.route <@ fun api -> api.getLength @>
    |> docs.alias "Get Length"
    |> docs.description "Returns the length of the input string"
    |> docs.example <@ fun api -> api.getLength "example string" @>
    |> docs.example <@ fun api -> api.getLength "" @>
    
    docs.route <@ fun api -> api.simpleUnit @>
    |> docs.alias "Simple Unit"
    |> docs.description "Unit as input"

    docs.route <@ fun api -> api.pureAsync @>
    |> docs.alias "Pure Async"
    |> docs.description "Returns a static integer"

    docs.route <@ fun api -> api.echoMap @>
    |> docs.alias "Echo Map"
    |> docs.description "Returns the input Map<string, int> as output"
    |> docs.example <@ fun api -> api.echoMap inputMap @>
  ]

let simpleServerDocs = Remoting.documentation "Simple Server" [ ]
let fableWebPart = 
  Remoting.createApi()
  |> Remoting.fromValue server
  |> Remoting.withRouteBuilder routeBuilder 
  |> Remoting.withErrorHandler (fun ex routeInfo -> Propagate ex.Message) 
  |> Remoting.withDiagnosticsLogger (printfn "%s")
  |> Remoting.withDocs "/api/server/docs" serverDocs
  |> Remoting.buildWebPart 

let simpleServerWebPart =   
  Remoting.createApi()
  |> Remoting.fromValue simpleServer 
  |> Remoting.withRouteBuilder routeBuilder
  |> Remoting.withDiagnosticsLogger (printfn "%s")
  |> Remoting.withDocs "/api/simple-server/docs" simpleServerDocs
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