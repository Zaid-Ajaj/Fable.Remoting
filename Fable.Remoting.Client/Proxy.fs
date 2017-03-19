module Proxy 
        
open FSharp.Reflection
open Fable.Core
open Fable.Core.JsInterop
open Fable.PowerPack
open Fetch.Fetch_types

let proxyFetch typeName methodName =
    fun data -> 
        let url = sprintf "/%s/%s" typeName methodName
        promise {
            let requestProps = [
                Body (unbox (toJson data))
                Method HttpMethod.POST
                Headers [ContentType "application/json"]
            ] 
            let! response = Fetch.fetch url requestProps 
            let! text = response.text()
            return ofJson<obj> text
        }
        |> Async.AwaitPromise


[<PassGenerics>]
let fields<'t> = 
    FSharpType.GetRecordFields typeof<'t>
    |> Seq.map (fun propInfo -> propInfo.Name)
    |> List.ofSeq

[<Emit("$2[$0] = $1")>]
let setProp<'t> (propName: string) (propValue: 't) (any: obj) : unit = failwith "JS"


[<PassGenerics>]
let createAn<'t> : 't = 
    // create an empty object literal
    let proxy = obj()
    let typeName = typeof<'t>.Name
    let fields = fields<'t>
    fields
    |> List.iter (fun field -> setProp<obj> field (proxyFetch typeName field) proxy)
    unbox proxy