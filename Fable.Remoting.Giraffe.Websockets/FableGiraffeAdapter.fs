namespace Fable.Remoting.Giraffe

open FSharp.Reflection
open Microsoft.AspNetCore.Http

open Giraffe
open System.IO
open System.Text

open Fable.Remoting.Server
open Fable.Remoting.Server.SharedCE

[<AutoOpen>]
module FableGiraffeAdapter =
    open System
    open Giraffe
    open FSharp.Control.Tasks.ContextInsensitive
    open System.Net.WebSockets
    open Microsoft.AspNetCore.Builder
    open Microsoft.AspNetCore.Http
    open System.Threading
  
    type SocketBuilder(implementation)=
        inherit SocketBuilderBase<HttpHandler>(implementation)

        let mutable socket : WebSocket option = None
        
        override __.Send s =
            match socket with
            | None -> async.Return()
            | Some webSocket ->
                 let resp = s |> System.Text.Encoding.UTF8.GetBytes |> ArraySegment
                 webSocket.SendAsync(resp,WebSocketMessageType.Text, true, CancellationToken.None) |> Async.AwaitTask


        override builder.CreateWebSocket mb fullPath =
         let ws (next:HttpFunc) (ctx:HttpContext) =
               task {
                 if ctx.WebSockets.IsWebSocketRequest then
                     let! webSocket = ctx.WebSockets.AcceptWebSocketAsync()
                     socket <- Some webSocket
                     let skt =
                       task {
                         let buffer = Array.zeroCreate 4096
                         let mutable loop = true
                         while loop do
                             let! msg = webSocket.ReceiveAsync(ArraySegment(buffer), CancellationToken.None )
                             match msg.MessageType,buffer.[0..msg.Count-1],msg.EndOfMessage,msg.CloseStatus with
                             |_,_,_,s when s.HasValue ->
                                 do! webSocket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null,CancellationToken.None)
                                 loop <- false
                             |WebSocketMessageType.Text, data, true, _ ->
                                 let str = System.Text.Encoding.UTF8.GetString data
                                 let input = builder.Deserialize str
                                 mb.Post input
                             | _ -> ()
                       }
                     do! skt
                     return Some ctx
                 else
                     return None
                 }
         
         route fullPath
                  >=> ws

  /// Computation expression to create a remoting server. Needs to open Fable.Remoting.Suave or Fable.Remoting.Giraffe for actual implementation
  /// Usage:
  /// `let server = remoting implementation {()}` for default options at /typeName/methodName
  /// `let server = remoting implementation = remoting {`
  /// `    with_builder builder` to set a `builder : (string -> string)`
  /// `}`
    let remoting = SocketBuilder
