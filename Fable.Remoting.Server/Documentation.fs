namespace Fable.Remoting.Server

open Microsoft.FSharp.Quotations

/// Helper class that constructs documented routes
type ApiDocs<'t>() = 
    /// Document a route
    member this.route<'u>(expr: Expr<'t -> Async<'u>>) = 
        match expr with 
        | Patterns.ProxyLambda (name, []) -> 
            { Route = Some name;
              Alias = None 
              Description = None; 
              Examples = [] } 
        | _ -> 
            { Route = None;
              Alias = None 
              Description = None; 
              Examples = [] } 
    
    /// Document a route
    member this.route<'v, 'u>(expr: Expr<'t -> ('v -> Async<'u>)>) = 
        match expr with 
        | Patterns.ProxyLambda (name, []) -> 
            { Route = Some name;
              Alias = None 
              Description = None; 
              Examples = [] } 
        | _ -> 
            { Route = None;
              Alias = None 
              Description = None; 
              Examples = [] } 
    
    /// Adds a description to the route definition
    member this.description (desc: string) (route: RouteDocs) = 
        { route with Description = Some desc } 
    
    /// Adds example to the route definition form the way you would use the remote function
    member this.example (expr: Expr<'t -> Async<'u>>) (route: RouteDocs) = 
        match expr with 
        | Patterns.ProxyLambda (name, args) when Some name = route.Route ->
            { route with Examples = List.append route.Examples [(args, "")] }
        | _ -> route 

    /// Add human-friendly alias for the remote function name
    member this.alias (name: string) (route: RouteDocs) = 
        { route with Alias = Some name }

module Docs = 
    
    let createFor<'t>() = ApiDocs<'t>()