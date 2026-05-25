module Fable.Remoting.Server.Proxy

open Fable.Remoting.Json
open Newtonsoft.Json
open TypeShape
open Fable.Remoting
open System
open System.Buffers
open Newtonsoft.Json.Linq
open System.IO
open System.Text
open System.Threading.Tasks
open Microsoft.Net.Http.Headers
open Microsoft.AspNetCore.WebUtilities

let private settings = JsonSerializerSettings(DateParseHandling = DateParseHandling.None)

let private fableSerializer =
    let serializer = JsonSerializer()
    serializer.Converters.Add (FableJsonConverter ())
    serializer

let private jsonEncoding = UTF8Encoding false

let jsonSerialize (o: 'a) (stream: Stream) =
    use sw = new StreamWriter (stream, jsonEncoding, 1024, true)
    use writer = new JsonTextWriter (sw, CloseOutput = false)
    fableSerializer.Serialize (writer, o)

/// Serialise the value to the output stream using the configured backend.
/// `NewtonsoftJson` → existing FableJsonConverter path; `SystemTextJson opts`
/// → System.Text.Json.JsonSerializer.Serialize with the provided options.
///
/// Public so sibling adapters (Suave, Falco, AspNetCore, AwsLambda,
/// AzureFunctions.Worker, Giraffe) can route their response-path serialisation
/// (docs schema, error bodies, etc.) through the same backend-aware path the
/// main proxy uses. Without this, those adapters' helper functions would
/// silently fall back to Newtonsoft for docs / error responses even when
/// the consumer opted in to STJ.
let jsonSerializeWithBackend (backend: JsonSerializerBackend) (o: 'a) (stream: Stream) =
    match backend with
    | NewtonsoftJson ->
        jsonSerialize o stream
    | SystemTextJson stjOptions ->
        System.Text.Json.JsonSerializer.Serialize<'a>(stream, o, stjOptions)

/// Parse the outer arguments-array JSON text into a list of raw per-argument
/// JSON strings, branching on backend. The result is backend-agnostic — any
/// parser the per-argument deserialise path picks can re-parse each element's
/// text. STJ consumers therefore exercise no Newtonsoft code path at runtime.
let private parseArgumentArray (backend: JsonSerializerBackend) (functionName: string) (expectedArgCount: int) (text: string) : string list =
    match backend with
    | NewtonsoftJson ->
        let token = JsonConvert.DeserializeObject<JToken>(text, settings)
        if token.Type <> JTokenType.Array then
            failwithf "The record function '%s' expected %d argument(s) to be received in the form of a JSON array but the input JSON was not an array" functionName expectedArgCount
        token :?> JArray
        |> Seq.map (fun el -> el.ToString(Formatting.None))
        |> Seq.toList
    | SystemTextJson _ ->
        use doc = System.Text.Json.JsonDocument.Parse(text)
        if doc.RootElement.ValueKind <> System.Text.Json.JsonValueKind.Array then
            failwithf "The record function '%s' expected %d argument(s) to be received in the form of a JSON array but the input JSON was not an array" functionName expectedArgCount
        doc.RootElement.EnumerateArray()
        |> Seq.map (fun el -> el.GetRawText())
        |> Seq.toList

// Settings for per-argument Newtonsoft deserialisation. Mirrors the
// `settings` value (DateParseHandling.None — required to preserve
// DateTimeOffset original offsets) but also includes the FableJsonConverter
// so F# types deserialise correctly. Constructed lazily once per process.
let private newtonsoftArgSettings =
    let s = JsonSerializerSettings(DateParseHandling = DateParseHandling.None)
    s.Converters.Add(FableJsonConverter())
    s

/// Parse one already-extracted argument's raw JSON text into 'inp using the
/// configured backend. STJ path doesn't touch Newtonsoft; Newtonsoft path
/// uses the existing FableJsonConverter + DateParseHandling.None to preserve
/// DateTimeOffset offsets and other date-handling semantics that the
/// pre-Phase-4f JToken-based path inherited from the outer `settings`.
let private deserialiseArgWithBackend<'inp> (backend: JsonSerializerBackend) (argText: string) : 'inp =
    match backend with
    | NewtonsoftJson ->
        JsonConvert.DeserializeObject<'inp>(argText, newtonsoftArgSettings)
    | SystemTextJson stjOptions ->
        System.Text.Json.JsonSerializer.Deserialize<'inp>(argText, stjOptions)

type private MsgPackSerializer<'a> =
    static let serializer = MsgPack.Write.makeSerializer<'a> ()
    static member Serialize (o, stream) = serializer.Invoke (o, stream)

let private recyclableMemoryStreamManager = Lazy<Microsoft.IO.RecyclableMemoryStreamManager> ()

let getRecyclableMemoryStreamManager options = options.RmsManager |> Option.defaultWith (fun _ -> recyclableMemoryStreamManager.Value)

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

let private readMultipartArgs props options = task {
    let mediaType = MediaTypeHeaderValue.Parse props.InputContentType
    let boundary = HeaderUtilities.RemoveQuotes mediaType.Boundary

    if Microsoft.Extensions.Primitives.StringSegment.IsNullOrEmpty boundary || boundary.Length > 70 then
        failwith "Multipart boundary missing or too long"

    let reader = MultipartReader (boundary.ToString (), props.Input)
    let parts = ResizeArray ()
    let mutable go = true
    
    while go do
        let! section = reader.ReadNextSectionAsync ()

        if isNull section then
            go <- false
        else
            if section.ContentType.Equals ("application/octet-stream", StringComparison.Ordinal) then
                use buffer = (getRecyclableMemoryStreamManager options).GetStream "remoting-input-multipart"
                do! section.Body.CopyToAsync buffer
                parts.Add (buffer.GetReadOnlySequence().ToArray () |> Choice1Of2)
            else
                use sr = new StreamReader (section.Body)
                let! text = sr.ReadToEndAsync ()
                // Multipart JSON sections are single values (one argument per
                // multipart part), so the section's text IS the raw JSON text
                // for that argument — no outer array unwrap required.
                parts.Add (Choice2Of2 text)

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
        if isBinaryOutput && props.IsProxyHeaderPresent && makeProps.ResponseSerialization.IsJson then
            let data = box result :?> byte[]
            props.Output.Write (data, 0, data.Length)
        elif makeProps.ResponseSerialization.IsJson then
            jsonSerializeWithBackend makeProps.JsonSerializer result props.Output
        else
            MsgPackSerializer.Serialize (result, props.Output)

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
                        | Choice2Of2 argText :: t ->
                            // Per-Phase 4f: argText is the raw JSON text for
                            // this single argument. The outer array was
                            // already parsed (in the request body parser or
                            // multipart reader, branched on backend), so the
                            // per-arg path is also fully backend-agnostic —
                            // STJ consumers exercise no Newtonsoft code path.
                            let inp = deserialiseArgWithBackend<'inp> makeProps.JsonSerializer argText
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
                let fieldProxy = makeEndpointProxy<'field> { FieldName = shape.MemberInfo.Name; RecordName = typeof<'impl>.Name; ResponseSerialization = options.ResponseSerialization; JsonSerializer = options.JsonSerializer; FlattenedTypes = flattenedTypes }
                let isNoArg = flattenedTypes.Length = 1 || (flattenedTypes.Length = 2 && flattenedTypes.[0] = typeof<unit>)

                wrap (fun (props: InvocationProps<'impl>) -> task {
                    let mutable requestBodyText = None

                    try
                        if not (props.HttpVerb.Equals ("POST", StringComparison.OrdinalIgnoreCase)) && not (isNoArg && props.HttpVerb.Equals ("GET", StringComparison.OrdinalIgnoreCase)) then
                            return InvalidHttpVerb
                        elif props.InputContentType.StartsWith ("multipart/form-data", StringComparison.Ordinal) then
                            let! args = readMultipartArgs props options
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
                                    parseArgumentArray
                                        options.JsonSerializer
                                        shape.MemberInfo.Name
                                        (flattenedTypes.Length - 1)
                                        text
                                    |> List.map Choice2Of2

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
