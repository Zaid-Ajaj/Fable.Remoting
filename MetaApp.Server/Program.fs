open Suave
open Suave.Filters
open Suave.Operators

open Fable.Remoting.Server
open Shared


let server : IServer = { 
    getLength = fun input -> async { return input.Length }
}

let app = 
    choose [
        // Serve javascript files
        GET >=> pathScan "/js/%s" (fun jsFile -> Files.file (sprintf "public/%s" jsFile))
        // Serve root index page
        GET >=> path "/" >=> Files.file "public/index.html"
        // Automatic routes for client-server interop
        SuaveAdapter.webPartFor server
    ]

[<EntryPoint>]
let main argv = 
    startWebServer defaultConfig app
    0