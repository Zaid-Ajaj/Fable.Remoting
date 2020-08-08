[<AutoOpen>]
module Extensions 

open Microsoft.AspNetCore.Http
open Fable.Remoting.Server

let reader<'t> = ReaderBuilder<HttpContext, 't>() 

type HttpContext with 
    member self.GetService<'t>() = self.RequestServices.GetService(typeof<'t>) :?> 't 

let getService<'t> (context : HttpContext) : 't = 
  if typeof<'t>.GUID = typeof<HttpContext>.GUID
  then context |> unbox<'t>
  else context.RequestServices.GetService(typeof<'t>) :?> 't

let resolve<'t>() = Reader (fun (httpContext: HttpContext) -> getService<'t>(httpContext)) 

module Remoting = 
    let fromReader inputReader (options: RemotingOptions<HttpContext, 't>) = 
        Remoting.fromContext (fun ctx -> Reader.run ctx inputReader) options
        