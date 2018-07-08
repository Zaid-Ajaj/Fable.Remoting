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

    /// Builds the API using a function that takes the incoming Http context and returns a protocol implementation. You can use the Http context to read information about the incoming request and also use the Http context to resolve dependencies using the underlying dependency injection mechanism.
    let fromContext (f: HttpContext -> 't) (options: RemotingOptions<HttpContext, 't>) = 
        { options with Implementation = FromContext f } 

    let fromReader inputReader (options: RemotingOptions<HttpContext, 't>) = 
        fromContext (fun ctx -> Reader.run ctx inputReader) options
        