module Fable.Remoting.Proxy

open Fable.Remoting.Json
open Newtonsoft.Json
open TypeShape.Core
open Fable.Remoting.Server
open System
open Newtonsoft.Json.Linq
open System.IO
open FSharp.Reflection
open System.Collections.Concurrent

let private fableConverter = new FableJsonConverter() :> JsonConverter

let private settings = JsonSerializerSettings(DateParseHandling = DateParseHandling.None)

let private fableSerializer =
    let serializer = JsonSerializer()
    serializer.Converters.Add fableConverter
    serializer

let private jsonSerialize (o: 'a) (stream: Stream) =
    use sw = new StreamWriter (stream)
    use writer = new JsonTextWriter (sw)
    fableSerializer.Serialize (writer, o)

let private msgPackSerializerCache = ConcurrentDictionary<Type, obj -> Stream -> unit> ()

let private msgPackSerialize (o: 'a) (stream: Stream) =
    match msgPackSerializerCache.TryGetValue typeof<'a> with
    | true, s -> s o stream
    | _ ->
        let s = MsgPack.Write.makeSerializer<'a> ()
        let s = fun (o: obj) stream -> s.Invoke (o :?> 'a, stream)
        msgPackSerializerCache.[typeof<'a>] <- s
        s o stream

type IShapeFSharpAsync =
    abstract Element : TypeShape

type private ShapeFSharpAsync<'T> () =
    interface IShapeFSharpAsync with
        member _.Element = shapeof<'T> :> _

let (|FSharpAsync|_|) (s : TypeShape) =
    match s.ShapeInfo with
    | Generic (td, ta) when td = typedefof<Async<_>> -> Activator.CreateInstanceGeneric<ShapeFSharpAsync<_>>(ta) :?> IShapeFSharpAsync |> Some
    | _ -> None

type private InvocationPropsInt = {
    Arguments: Choice<byte[], JToken list>
    HttpVerb: string
    Output: Stream
}

type InvocationProps<'impl> = {
    Input: Stream
    Implementation: 'impl
    EndpointName: string
    HttpVerb: string
    Output: Stream
    IsContentBinaryEncoded: bool
}

type MakeEndpointProps = {
    FieldName: string
    RecordName: string
    ResponseSerialization: SerializationType
}

type InvocationResult =
    | Success of isBinaryOutput: bool
    | EndpointNotFound
    | Exception of exn * functionName: string

let rec private makeEndpointProxy<'fieldPart> (makeProps: MakeEndpointProps): 'fieldPart -> InvocationPropsInt -> Async<InvocationResult> =
    let wrap (p: 'a -> InvocationPropsInt -> Async<InvocationResult>) = unbox<'fieldPart -> InvocationPropsInt -> Async<InvocationResult>> p

    match shapeof<'fieldPart> with
    | FSharpAsync a ->
        a.Element.Accept {
            new ITypeVisitor<'fieldPart -> InvocationPropsInt -> Async<InvocationResult>> with
                member _.Visit<'result> () =
                    let isBinaryOutput = typeof<'result> = typeof<byte[]>

                    wrap (fun (s: Async<'result>) props -> async {
                        match props.Arguments with
                        | Choice2Of2 (_ :: _) -> failwith "too many args"
                        | _ -> ()

                        let! res = s

                        if isBinaryOutput && makeProps.ResponseSerialization = SerializationType.Json then
                            let data = box res :?> byte[]
                            props.Output.Write (data, 0, data.Length)
                        elif makeProps.ResponseSerialization = SerializationType.Json then
                            jsonSerialize res props.Output
                        else
                            msgPackSerialize res props.Output

                        return Success isBinaryOutput
                    })
        }
    | Shape.FSharpFunc func ->
        func.Accept {
            new IFSharpFuncVisitor<'fieldPart -> InvocationPropsInt -> Async<InvocationResult>> with
                member _.Visit<'inp, 'out> () =
                    let outp = makeEndpointProxy<'out> makeProps

                    wrap (fun (f: 'inp -> 'out) props ->
                        match props.Arguments with
                        | Choice1Of2 bytes ->
                            if typeof<'inp> <> typeof<byte[]> then
                                failwith "did not expect a byte array"

                            let inp = box bytes :?> 'inp
                            outp (f inp) { props with Arguments = Choice1Of2 [||] }
                        | Choice2Of2 (h :: t) ->
                            let inp = h.ToObject<'inp> fableSerializer
                            outp (f inp) { props with Arguments = Choice2Of2 t }
                        | Choice2Of2 [] when typeof<'inp> = typeof<unit> ->
                            let inp = box () :?> _
                            outp (f inp) { props with Arguments = Choice2Of2 [] }
                        | _ ->
                            failwith "too few args")
        }
    | _ ->
        failwithf "The type '%s' of the record field '%s' for record type '%s' is not valid. It must either be Async<'t> or a function that returns Async<'t> (i.e. 'u -> Async<'t>)" typeof<'fieldPart>.Name makeProps.FieldName makeProps.RecordName

let makeApiProxy<'impl, 'ctx> (options: RemotingOptions<'ctx, 'impl>): InvocationProps<'impl> -> Async<InvocationResult> =
    let wrap (p: InvocationProps<'a> -> Async<InvocationResult>) = unbox<InvocationProps<'impl> -> Async<InvocationResult>> p

    let memberVisitor (shape: IShapeMember<'impl>) =
        shape.Accept { new IReadOnlyMemberVisitor<'impl, InvocationProps<'impl> -> Async<InvocationResult>> with
            member _.Visit (shape: ReadOnlyMember<'impl, 'field>) =
                let fieldProxy = makeEndpointProxy<'field> { FieldName = shape.MemberInfo.Name; RecordName = typeof<'impl>.Name; ResponseSerialization = options.ResponseSerialization }

                wrap (fun (props: InvocationProps<'impl>) -> async {
                    try
                        if props.IsContentBinaryEncoded then
                            use ms = new MemoryStream ()
                            do! props.Input.CopyToAsync ms |> Async.AwaitTask
                            let props' = { Arguments = Choice1Of2 (ms.ToArray ()); HttpVerb = props.HttpVerb; Output = props.Output } 
                            return! fieldProxy (shape.Get props.Implementation) props'
                        else
                            use sr = new StreamReader (props.Input)
                            let! text = sr.ReadToEndAsync () |> Async.AwaitTask

                            let args =
                                if String.IsNullOrEmpty text then
                                    []
                                else
                                    //todo error if not array
                                    JsonConvert.DeserializeObject<JArray> (text, settings) |> Seq.toList

                            let props' = { Arguments = Choice2Of2 args; HttpVerb = props.HttpVerb; Output = props.Output } 
                            return! fieldProxy (shape.Get props.Implementation) props'
                    with e ->
                        return Exception (e, shape.MemberInfo.Name) }) }

    match shapeof<'impl> with
    | Shape.FSharpRecord (:? ShapeFSharpRecord<'impl> as shape) ->
        let endpoints =
            shape.Fields
            |> Array.map (fun f -> options.RouteBuilder typeof<'impl>.Name f.MemberInfo.Name, memberVisitor f)
            |> Map.ofArray

        wrap (fun (props: InvocationProps<'impl>) ->
            match Map.tryFind props.EndpointName endpoints with
            | Some endpoint -> endpoint props
            | _ -> async { return EndpointNotFound })
    | _ ->
        failwithf "Protocol definition must be encoded as a record type. The input type '%s' was not a record." typeof<'impl>.Name
