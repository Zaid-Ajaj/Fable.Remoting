#r "../Fable.Remoting.Client/node_modules/fable-core/Fable.Core.dll"
#r "../Fable.Remoting.Client/node_modules/fable-powerpack/Fable.PowerPack.dll"

// Load the models
#load "../MetaApp.Shared/Models.fs"
// Load the proxy generator (the client implementation)
#load "../Fable.Remoting.Client/Proxy.fs"


open Shared
open Fable.Import.Browser

module Main = 
    // creates a fake IServer
    let Server = Proxy.createAn<IServer>

    async {
        let! result = Server.getLength "test-hello"
        do console.log result
    } 
    |> Async.StartImmediate