namespace Fable.Remoting.Suave

open Fable.Remoting.Server

open Suave
open Suave.Filters
open Suave.Operators
open Suave.Successful

open FSharp.Reflection
open Newtonsoft.Json




module SuaveAdapter = 

    let internal fableConverter = new Fable.JsonConverter()

    let fromJson<'a> json =
        JsonConvert.DeserializeObject(json, typeof<'a>, fableConverter) :?> 'a

    let getResourceFromReq (req : HttpRequest) : obj =
       let getString rawForm = System.Text.Encoding.UTF8.GetString(rawForm)
       req.rawForm |> getString |> fromJson<obj>

    let json v =
      JsonConvert.SerializeObject(v, Formatting.Indented, fableConverter)
      |> OK
      >=> Writers.setMimeType "application/json; charset=utf-8"

    let handleRequest methodName serverImplementation = 
        fun (req: HttpRequest) ->
            let requestBodyData = getResourceFromReq req
            async {
                let! dynamicResult = Server.dynamicallyInvoke methodName serverImplementation requestBodyData
                return json dynamicResult
            }  |> Async.RunSynchronously
        
        
    let webPartFor implementation : WebPart = 
        let typeName = implementation.GetType().Name
        implementation.GetType()
        |> FSharpType.GetRecordFields
        |> Seq.map (fun propInfo -> propInfo.Name)
        |> Seq.map (fun methodName -> 
            let fullPath = sprintf "/%s/%s" typeName methodName
            POST >=> path fullPath >=> request (handleRequest methodName implementation)
        )
        |> List.ofSeq
        |> choose
