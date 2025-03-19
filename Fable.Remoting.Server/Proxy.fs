module Fable.Remoting.Server.Proxy

open Fable.Remoting.Json
open Newtonsoft.Json
open TypeShape
open Fable.Remoting
open System
open Newtonsoft.Json.Linq
open System.IO
open System.Collections.Concurrent
open System.Text
open System.Threading.Tasks
open Microsoft.Net.Http.Headers
open Microsoft.AspNetCore.WebUtilities

let private fableConverter = new FableJsonConverter() :> JsonConverter

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

let private typeNames inputTypes =
    inputTypes
    |> Array.map Diagnostics.typePrinter
    |> String.concat ", "
    |> sprintf "[%s]"

let private (|FSharpAsync|_|) (s: TypeShape) =
    match s.ShapeInfo with
    | Generic (td, ta) when td = typedefof<Async<_>> -> Activator.CreateInstanceGeneric<ShapeFSharpAsyncOrTask<_>>(ta) :?> IShapeFSharpAsyncOrTask |> Some
    | _ -> None

let private (|Task|_|) (s: TypeShape) =
    match s.ShapeInfo with
    | Generic (td, ta) when td = typedefof<Task<_>> -> Activator.CreateInstanceGeneric<ShapeFSharpAsyncOrTask<_>>(ta) :?> IShapeFSharpAsyncOrTask |> Some
    | _ -> None

let private readMultipartArgs (contentType: string) s = task {
    let mediaType = MediaTypeHeaderValue.Parse contentType
    let boundary = HeaderUtilities.RemoveQuotes mediaType.Boundary

    if Microsoft.Extensions.Primitives.StringSegment.IsNullOrEmpty boundary || boundary.Length > 70 then
        failwith "Multipart boundary missing or too long"

    let reader = MultipartReader (boundary.ToString (), s)
    let parts = ResizeArray ()
    let mutable go = true
    
    while go do
        let! section = reader.ReadNextSectionAsync ()

        if isNull section then
            go <- false
        else
            if section.ContentType.Equals ("application/octet-stream", StringComparison.Ordinal) then
                use ms = new MemoryStream ()
                do! section.Body.CopyToAsync ms
                parts.Add (ms.ToArray () |> Choice1Of2)
            else
                use sr = new StreamReader (section.Body)
                let! text = sr.ReadToEndAsync ()
                let token = JToken.Parse text
                parts.Add (Choice2Of2 token)

    return Seq.toList parts
}

let rec private makeEndpointProxy<'fieldPart> (makeProps: MakeEndpointProps): 'fieldPart -> InvocationPropsInt -> Task<InvocationResult> =
    let wrap (p: 'a -> InvocationPropsInt -> Task<InvocationResult>) = unbox<'fieldPart -> InvocationPropsInt -> Task<InvocationResult>> p

    // Check that no arguments are left
    let validateArgumentCount props makeProps =
        match props.Arguments with
        | _ :: _ ->
            let typeInfo = typeNames makeProps.FlattenedTypes.[ 0 .. makeProps.FlattenedTypes.Length - 2]
            failwithf "The record function '%s' expected %d argument(s) of the types %s but got %d argument(s) in the input JSON array" makeProps.FieldName (makeProps.FlattenedTypes.Length - 1) typeInfo props.Arguments.Length
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
            new ITypeVisitor<'fieldPart -> InvocationPropsInt -> Task<InvocationResult>> with
                member _.Visit<'result> () =
                    let isBinaryOutput = typeof<'result> = typeof<byte[]>

                    wrap (fun (s: Async<'result>) props -> task {
                        validateArgumentCount props makeProps
                        let! result = s
                        writeToOutputMemoryStream isBinaryOutput props result                       
                        return Success isBinaryOutput
                    })
        }
    | Task t ->
        t.Element.Accept {
            new ITypeVisitor<'fieldPart -> InvocationPropsInt -> Task<InvocationResult>> with
                member _.Visit<'result> () =
                    let isBinaryOutput = typeof<'result> = typeof<byte[]>

                    wrap (fun (s: Task<'result>) props -> task {
                        validateArgumentCount props makeProps
                        let! result = s
                        writeToOutputMemoryStream isBinaryOutput props result                       
                        return Success isBinaryOutput
                    })
        }
    | Shape.FSharpFunc func ->
        func.Accept {
            new IFSharpFuncVisitor<'fieldPart -> InvocationPropsInt -> Task<InvocationResult>> with
                member _.Visit<'inp, 'out> () =
                    let outp = makeEndpointProxy<'out> makeProps

                    wrap (fun (f: 'inp -> 'out) props ->
                        match props.Arguments with
                        | Choice1Of2 bytes :: t ->
                            if typeof<'inp> <> typeof<byte[]> then
                                failwithf "The record function '%s' expected an argument of type %s, but got binary input" makeProps.FieldName typeof<'inp>.Name

                            let inp = box bytes :?> 'inp
                            outp (f inp) { props with Arguments = t }
                        | Choice2Of2 json :: t ->
                            let inp = json.ToObject<'inp> fableSerializer
                            outp (f inp) { props with Arguments = t }
                        | [] when typeof<'inp> = typeof<unit> ->
                            let inp = box () :?> _
                            outp (f inp) { props with Arguments = [] }
                        | [] ->
                            let typeInfo = typeNames makeProps.FlattenedTypes.[ 0 .. makeProps.FlattenedTypes.Length - 2]
                            failwithf "The record function '%s' expected %d argument(s) of the types %s but got %d argument(s) in the input" makeProps.FieldName (makeProps.FlattenedTypes.Length - 1) typeInfo props.Arguments.Length)
        }
    | _ ->
        failwithf "The type '%s' of the record field '%s' for record type '%s' is not valid. It must either be Async<'t>, Task<'t> or a function that returns either (i.e. 'u -> Async<'t>)" typeof<'fieldPart>.Name makeProps.FieldName makeProps.RecordName

let makeApiProxy<'impl, 'ctx> (options: RemotingOptions<'ctx, 'impl>): InvocationProps<'impl> -> Task<InvocationResult> =
    let wrap (p: InvocationProps<'a> -> Task<InvocationResult>) = unbox<InvocationProps<'impl> -> Task<InvocationResult>> p

    let memberVisitor (shape: IShapeMember<'impl>, flattenedTypes: Type[]) =
        shape.Accept { new IReadOnlyMemberVisitor<'impl, InvocationProps<'impl> -> Task<InvocationResult>> with
            member _.Visit (shape: ReadOnlyMember<'impl, 'field>) =
                let fieldProxy = makeEndpointProxy<'field> { FieldName = shape.MemberInfo.Name; RecordName = typeof<'impl>.Name; ResponseSerialization = options.ResponseSerialization; FlattenedTypes = flattenedTypes }
                let isNoArg = flattenedTypes.Length = 1 || (flattenedTypes.Length = 2 && flattenedTypes.[0] = typeof<unit>)

                wrap (fun (props: InvocationProps<'impl>) -> task {
                    let mutable requestBodyText = None

                    try
                        if not (props.HttpVerb.Equals ("POST", StringComparison.OrdinalIgnoreCase)) && not (isNoArg && props.HttpVerb.Equals ("GET", StringComparison.OrdinalIgnoreCase)) then
                            return InvalidHttpVerb
                        elif props.InputContentType.StartsWith ("multipart/form-data", StringComparison.Ordinal) then
                            let! args = readMultipartArgs props.InputContentType props.Input
                            let props' = { Arguments = args; IsProxyHeaderPresent = props.IsProxyHeaderPresent; Output = props.Output }
                            return! fieldProxy (props.ImplementationBuilder () |> shape.Get) props'
                        else
                            use sr = new StreamReader (props.Input)
                            let! text = sr.ReadToEndAsync ()

                            let args =
                                if String.IsNullOrEmpty text then
                                    []
                                else
                                    requestBodyText <- Some text
                                    let token = JToken.Parse text
                                    if token.Type <> JTokenType.Array then
                                        failwithf "The record function '%s' expected %d argument(s) to be received in the form of a JSON array but the input JSON was not an array" shape.MemberInfo.Name (flattenedTypes.Length - 1)

                                    token
                                    :?> JArray
                                    |> Seq.map Choice2Of2
                                    |> Seq.toList

                            let props' = { Arguments = args; IsProxyHeaderPresent = props.IsProxyHeaderPresent; Output = props.Output }
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
            | _ -> Task.FromResult EndpointNotFound)
    | _ ->
        failwithf "Protocol definition must be encoded as a record type. The input type '%s' was not a record." typeof<'impl>.Name
