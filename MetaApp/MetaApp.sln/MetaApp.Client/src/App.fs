module MetaApp.Client

open System
open SharedModelsOne

open Fable.Core
open Fable.Core.JsInterop
open Fable.Import

open Fable.Remoting.Client

let server = Proxy.create<IServer>

let getNameFromServer() = 


  async {
      let! person = server.getPerson()
      do Browser.console.log(person)
  }


getNameFromServer()
|> Async.StartImmediate