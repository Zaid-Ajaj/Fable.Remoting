namespace Fable.Remoting.Saturn

open FSharp.Reflection
open Giraffe.HttpHandlers
open Fable.Remoting.Giraffe.FableGiraffeAdapter

[<RequireQualifiedAccess>]
[<AutoOpen>]
module FableSaturnAdapter =
    
    open System.Text
    let private write  (sb: StringBuilder) text   = sb.AppendLine(text) |> ignore
   
    let private toLogger (sb: StringBuilder) = 
        logger |> Option.iter(fun logf -> 
            sb
            |> string
            |> logf
        )
    
    open Saturn.Router
    let private httpHandlerWithBuilderFor<'t> (scope:ScopeBuilder) state (implementation: 't) (routeBuilder: string -> string -> string) : ScopeState = 
            let builder = StringBuilder()
            let typeName = implementation.GetType().Name
            write builder (sprintf "Building Routes for %s" typeName)
            let state =
             implementation.GetType()
             |> FSharpType.GetRecordFields
             |> Seq.fold (fun state propInfo -> 
                let methodName = propInfo.Name
                let fullPath = routeBuilder typeName methodName
                write builder (sprintf "Record field %s maps to route %s" methodName fullPath)               
                scope.Post(state, fullPath,  warbler (fun _ -> handleRequest methodName implementation fullPath))            
             ) state
            builder |> toLogger
            state
     
    type ScopeBuilder with
        [<CustomOperation("handlerFor")>]
        member t.HttpHandlerWithBuilderFor<'t>(state, implementation:'t, routeBuilder) : ScopeState =
            httpHandlerWithBuilderFor t state implementation routeBuilder
        [<CustomOperation("defaultHandlerFor")>]
        member t.HttpHandlerFor<'t>(state, implementation:'t) : ScopeState =
            httpHandlerWithBuilderFor t state implementation (sprintf "/%s/%s")