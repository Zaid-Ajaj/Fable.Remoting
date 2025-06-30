namespace Fable.Remoting.Server

open System 
open FSharp.Reflection
open TypeShape
open System.IO
open Newtonsoft.Json.Linq

[<RequireQualifiedAccess>]
module TypeInfo = 
    let rec flattenFuncTypes (typeDef: Type) = 
        [| if FSharpType.IsFunction typeDef 
           then let (domain, range) = FSharpType.GetFunctionElements typeDef 
                yield! flattenFuncTypes domain 
                yield! flattenFuncTypes range
           else yield typeDef |]

type ParsingArgumentsError = { ParsingArgumentsError: string }

/// Route information that is propagated to error handler when exceptions are thrown
type RouteInfo<'ctx> = {
    /// The full path of the request
    path: string
    /// The last part of the path of the request
    methodName: string
    /// The HttpContext of the request
    httpContext: 'ctx
    /// The text content of the request, if any
    requestBodyText: string option
}

type CustomErrorResult<'a> =
    { error: 'a;
      ignored: bool;
      handled: bool; }

/// The ErrorResult lets you choose whether you want to propagate a custom error back to the client or to ignore it. Either case, an exception is thrown on the call-site from the client
type ErrorResult =
    | Ignore
    | Propagate of obj

type ErrorHandler<'context> = System.Exception -> RouteInfo<'context> -> ErrorResult

/// A protocol implementation can be a static value provided or it can be generated from the Http context on every request.
type ProtocolImplementation<'context, 'serverImpl> = 
    | Empty 
    | StaticValue of 'serverImpl 
    | FromContext of ('context -> 'serverImpl)

type SerializationType =
    | Json
    | MessagePack

type internal IShapeFSharpAsyncOrTask  =
    abstract Element: TypeShape

type internal ShapeFSharpAsyncOrTask<'T> () =
    interface IShapeFSharpAsyncOrTask  with
        member _.Element = shapeof<'T> :> _

type internal InvocationPropsInt = {
    Arguments: Choice<byte[], byte[], JToken> list
    IsProxyHeaderPresent: bool
    Output: Stream
}

type InvocationProps<'impl> = {
    Input: Stream
    Output: Stream
    ImplementationBuilder: unit -> 'impl
    EndpointName: string
    HttpVerb: string
    InputContentType: string
    IsProxyHeaderPresent: bool
}

type MakeEndpointProps = {
    FieldName: string
    RecordName: string
    ResponseSerialization: SerializationType
    FlattenedTypes: Type[]
}

type InvocationResult =
    | Success of isBinaryOutput: bool
    | EndpointNotFound
    | InvalidHttpVerb
    | Exception of exn * functionName: string * requestBodyText: string option

// an example is a list of arguments and the description of the example
type Example = obj list * string

type RouteDocs = 
    { Route : string option
      /// An alias for the method name
      Alias : string option
      /// The description of the method
      Description : string option
      /// Examples are objects and optionally, their description
      Examples : Example list }

/// Contains documented routes for an API
type Documentation = Documentation of string * RouteDocs list

type RemotingOptions<'context, 'serverImpl> = {
    Implementation: ProtocolImplementation<'context, 'serverImpl> 
    RouteBuilder : string -> string -> string 
    ErrorHandler : ErrorHandler<'context> option 
    DiagnosticsLogger : (string -> unit) option 
    Docs : string option * Option<Documentation>
    ResponseSerialization : SerializationType
    RmsManager : Microsoft.IO.RecyclableMemoryStreamManager option
}
