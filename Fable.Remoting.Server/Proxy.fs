﻿module Fable.Remoting.Server.Proxy

open Fable.Remoting.Json
open Newtonsoft.Json
open TypeShape
open Fable.Remoting
open System
open Newtonsoft.Json.Linq
open System.IO
open System.Collections.Concurrent
open System.Text

let private fableConverter = new FableJsonConverter() :> JsonConverter

let private settings = JsonSerializerSettings(DateParseHandling = DateParseHandling.None)

let private fableSerializer =
    let serializer = JsonSerializer()
    serializer.Converters.Add fableConverter
    serializer

let private jsonEncoding = UTF8Encoding false

let jsonSerialize (o: 'a) (stream: Stream) =
    use sw = new StreamWriter (stream, jsonEncoding, 1024, true)
    use writer = new JsonTextWriter (sw, CloseOutput = false)
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

let recyclableMemoryStreamManager = Lazy<Microsoft.IO.RecyclableMemoryStreamManager> ()

let typeNames inputTypes =
    inputTypes
    |> Array.map Diagnostics.typePrinter
    |> String.concat ", "
    |> sprintf "[%s]"

let internal (|FSharpAsync|_|) (s: TypeShape) =
    match s.ShapeInfo with
    | Generic (td, ta) when td = typedefof<Async<_>> -> Activator.CreateInstanceGeneric<ShapeFSharpAsyncOrTask<_>>(ta) :?> IShapeFSharpAsyncOrTask |> Some
    | _ -> None

let internal (|Task|_|) (s: TypeShape) =
    match s.ShapeInfo with
    | Generic (td, ta) when td = typedefof<Threading.Tasks.Task<_>> -> Activator.CreateInstanceGeneric<ShapeFSharpAsyncOrTask<_>>(ta) :?> IShapeFSharpAsyncOrTask |> Some
    | _ -> None

let rec private makeEndpointProxy<'fieldPart> (makeProps: MakeEndpointProps): 'fieldPart -> InvocationPropsInt -> Async<InvocationResult> =
    let wrap (p: 'a -> InvocationPropsInt -> Async<InvocationResult>) = unbox<'fieldPart -> InvocationPropsInt -> Async<InvocationResult>> p

    let validateArgumentCount props makeProps =
        match props.Arguments with
        | Choice2Of2 (_ :: _) ->
            let typeInfo = typeNames makeProps.FlattenedTypes.[ 0 .. makeProps.FlattenedTypes.Length - 2]
            failwithf "The record function '%s' expected %d argument(s) of the types %s but got %d argument(s) in the input JSON array" makeProps.FieldName (makeProps.FlattenedTypes.Length - 1) typeInfo props.ArgumentCount
        | _ -> ()

    let writeToOutputMemoryStream isBinaryOutput (props: InvocationPropsInt) result =
        if isBinaryOutput && props.IsProxyHeaderPresent && makeProps.ResponseSerialization = SerializationType.Json then
            let data = box result :?> byte[]
            props.Output.Write (data, 0, data.Length)
        elif makeProps.ResponseSerialization = SerializationType.Json then
            jsonSerialize result props.Output
        else
            msgPackSerialize result props.Output

        props.Output.Position <- 0L

    match shapeof<'fieldPart> with
    | FSharpAsync a ->
        a.Element.Accept {
            new ITypeVisitor<'fieldPart -> InvocationPropsInt -> Async<InvocationResult>> with
                member _.Visit<'result> () =
                    let isBinaryOutput = typeof<'result> = typeof<byte[]>

                    wrap (fun (s: Async<'result>) props -> async {
                        validateArgumentCount props makeProps
                        let! result = s
                        writeToOutputMemoryStream isBinaryOutput props result                       
                        return Success isBinaryOutput
                    })
        }
    | Task t ->
        t.Element.Accept {
            new ITypeVisitor<'fieldPart -> InvocationPropsInt -> Async<InvocationResult>> with
                member _.Visit<'result> () =
                    let isBinaryOutput = typeof<'result> = typeof<byte[]>

                    wrap (fun (s: Threading.Tasks.Task<'result>) props -> async {
                        validateArgumentCount props makeProps
                        let! result = Async.AwaitTask s
                        writeToOutputMemoryStream isBinaryOutput props result                       
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
                                failwithf "The record function '%s' expected an argument of type %s, but got binary input" makeProps.FieldName typeof<'inp>.Name

                            let inp = box bytes :?> 'inp
                            outp (f inp) { props with Arguments = Choice1Of2 [||] }
                        | Choice2Of2 (h :: t) ->
                            let inp = h.ToObject<'inp> fableSerializer
                            outp (f inp) { props with Arguments = Choice2Of2 t }
                        | Choice2Of2 [] when typeof<'inp> = typeof<unit> ->
                            let inp = box () :?> _
                            outp (f inp) { props with Arguments = Choice2Of2 [] }
                        | _ ->
                            let typeInfo = typeNames makeProps.FlattenedTypes.[ 0 .. makeProps.FlattenedTypes.Length - 2]
                            failwithf "The record function '%s' expected %d argument(s) of the types %s but got %d argument(s) in the input JSON array" makeProps.FieldName (makeProps.FlattenedTypes.Length - 1) typeInfo props.ArgumentCount)
        }
    | _ ->
        failwithf "The type '%s' of the record field '%s' for record type '%s' is not valid. It must either be Async<'t>, Task<'t> or a function that returns it (i.e. 'u -> Async<'t>)" typeof<'fieldPart>.Name makeProps.FieldName makeProps.RecordName

let makeApiProxy<'impl, 'ctx> (options: RemotingOptions<'ctx, 'impl>): InvocationProps<'impl> -> Async<InvocationResult> =
    let wrap (p: InvocationProps<'a> -> Async<InvocationResult>) = unbox<InvocationProps<'impl> -> Async<InvocationResult>> p

    let memberVisitor (shape: IShapeMember<'impl>, flattenedTypes: Type[]) =
        shape.Accept { new IReadOnlyMemberVisitor<'impl, InvocationProps<'impl> -> Async<InvocationResult>> with
            member _.Visit (shape: ReadOnlyMember<'impl, 'field>) =
                let fieldProxy = makeEndpointProxy<'field> { FieldName = shape.MemberInfo.Name; RecordName = typeof<'impl>.Name; ResponseSerialization = options.ResponseSerialization; FlattenedTypes = flattenedTypes }
                let isNoArg = flattenedTypes.Length = 1 || (flattenedTypes.Length = 2 && flattenedTypes.[0] = typeof<unit>)

                wrap (fun (props: InvocationProps<'impl>) -> async {
                    let mutable requestBodyText = None

                    try
                        if props.HttpVerb <> "POST" && not (isNoArg && props.HttpVerb = "GET") then
                            return InvalidHttpVerb
                        elif props.IsContentBinaryEncoded then
                            use ms = new MemoryStream ()
                            do! props.Input.CopyToAsync ms |> Async.AwaitTask
                            let props' = { Arguments = Choice1Of2 (ms.ToArray ()); ArgumentCount = 1; IsProxyHeaderPresent = props.IsProxyHeaderPresent; Output = props.Output }
                            return! fieldProxy (props.ImplementationBuilder () |> shape.Get) props'
                        else
                            use sr = new StreamReader (props.Input)
                            let! text = sr.ReadToEndAsync () |> Async.AwaitTask

                            let args =
                                if String.IsNullOrEmpty text then
                                    []
                                else
                                    requestBodyText <- Some text
                                    let token = JsonConvert.DeserializeObject<JToken> (text, settings)
                                    if token.Type <> JTokenType.Array then
                                        failwithf "The record function '%s' expected %d argument(s) to be received in the form of a JSON array but the input JSON was not an array" shape.MemberInfo.Name (flattenedTypes.Length - 1)

                                    token :?> JArray |> Seq.toList

                            let props' = { Arguments = Choice2Of2 args; ArgumentCount = args.Length; IsProxyHeaderPresent = props.IsProxyHeaderPresent; Output = props.Output }
                            return! fieldProxy (props.ImplementationBuilder () |> shape.Get) props'
                    with e ->
                        return InvocationResult.Exception (e, shape.MemberInfo.Name, requestBodyText) }) }

    match shapeof<'impl> with
    | Shape.FSharpRecord (:? ShapeFSharpRecord<'impl> as shape) ->
        let endpoints =
            shape.Fields
            |> Array.map (fun f -> options.RouteBuilder typeof<'impl>.Name f.MemberInfo.Name, memberVisitor (f, TypeInfo.flattenFuncTypes f.Member.Type))
            |> Map.ofArray

        wrap (fun (props: InvocationProps<'impl>) ->
            match Map.tryFind props.EndpointName endpoints with
            | Some endpoint -> endpoint props
            | _ -> async { return EndpointNotFound })
    | _ ->
        failwithf "Protocol definition must be encoded as a record type. The input type '%s' was not a record." typeof<'impl>.Name
