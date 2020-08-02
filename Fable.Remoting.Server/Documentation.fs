namespace Fable.Remoting.Server

open Microsoft.FSharp.Quotations
open Fable.Remoting.Json
open Newtonsoft.Json
open Newtonsoft.Json.Linq
open System
open FSharp.Reflection

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

    let private fableConverter = new FableJsonConverter() :> JsonConverter

    let serialize result = JsonConvert.SerializeObject(result, [| fableConverter |])

    let routeMethod fieldType =
        match TypeInfo.flattenFuncTypes fieldType with
        | [| simpleAsyncValue |] when simpleAsyncValue.FullName.StartsWith("Microsoft.FSharp.Control.FSharpAsync`1") -> "GET"
        | [| input; _ |] when input = typeof<unit> -> "GET"
        | _ -> "POST"

    let makeDocsSchema (recordType: Type) (Documentation(docsName, routesDefs)) (routeBuilder: string -> string -> string) =
        let schema = JObject()
        let routes = JArray()
        for fieldInfo in FSharpType.GetRecordFields recordType do
            let routeDocs = List.tryFind (fun routeDocs -> routeDocs.Route = Some fieldInfo.Name) routesDefs
            let route = JObject()
            route.Add(JProperty("remoteFunction", fieldInfo.Name))
            route.Add(JProperty("httpMethod", routeMethod fieldInfo.PropertyType))
            route.Add(JProperty("route", routeBuilder recordType.Name fieldInfo.Name))

            let description =
                routeDocs
                |> Option.bind (fun route -> route.Description)
                |> Option.defaultValue ""

            let alias =
                routeDocs
                |> Option.bind (fun route -> route.Alias)
                |> Option.defaultValue fieldInfo.Name

            route.Add(JProperty("description", description))
            route.Add(JProperty("alias", alias))

            let examplesJson = JArray()
            match routeDocs with
            | None -> ()
            | Some routeDocs ->
                for (exampleArgs, description) in routeDocs.Examples do
                    let argsJson = JArray()
                    for arg in exampleArgs do argsJson.Add(JToken.Parse(serialize arg))
                    let exampleJson = JObject()
                    exampleJson.Add(JProperty("description", description))
                    exampleJson.Add(JProperty("arguments", argsJson))
                    examplesJson.Add(exampleJson)

            route.Add(JProperty("examples", examplesJson))
            routes.Add(route)

        schema.Add(JProperty("name", docsName))
        schema.Add(JProperty("routes", routes))
        schema
