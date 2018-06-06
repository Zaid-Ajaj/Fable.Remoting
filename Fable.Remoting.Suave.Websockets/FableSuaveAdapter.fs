namespace Fable.Remoting.Suave

open Suave
open Suave.Filters
open Suave.Sockets.Control
open Suave.Operators
open Suave.Successful

open FSharp.Reflection
open Fable.Remoting.Server
open System.Text
open Suave.WebSocket
open Suave
open Suave.Filters
open Suave.Operators
open Suave.Sockets
open Suave.Sockets.Control
open Suave.WebSocket
[<AutoOpen>]
module FableSuaveAdapter =

  type SocketBuilder(implementation) =
   inherit SocketBuilderBase<WebPart<HttpContext>>(implementation)
   
   let mutable webSocket : WebSocket option = None

   override builder.CreateWebSocket mb fullPath =
    let ws (webSocket:WebSocket) _ =
        socket {
            let mutable loop = true
            while loop do
                let! msg = webSocket.read()
                match msg with
                |Text, data, true ->
                    let str = UTF8.toString data
                    let input = builder.Deserialize str
                    mb.Post input
                | (Close, _, _) ->
                    let emptyResponse = [||] |> ByteSegment
                    do! webSocket.send Close emptyResponse true                        
                    loop <- false                        
                | _ -> ()}

    path fullPath >=> handShake ws

    override __.Send s=
        match webSocket with
        |None -> async.Return ()
        |Some webSocket ->
            let resp = s |> System.Text.Encoding.UTF8.GetBytes |> ByteSegment
            webSocket.send Text resp true |> Async.Ignore

  /// Computation expression to create a remoting server. Needs to open Fable.Remoting.Suave or Fable.Remoting.Giraffe for actual implementation
  /// Usage:
  /// `let server = remoting implementation {()}` for default options at /typeName/methodName
  /// `let server = remoting implementation = remoting {`
  /// `    with_builder builder` to set a `builder : (string -> string -> string)`
  /// `}`
  let remoting = SocketBuilder
  