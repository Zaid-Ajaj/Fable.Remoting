namespace Fable.Remoting.Client

open System
open Browser.Types
open Fable.Core
open Fable.Core.JsInterop
open System.Runtime.CompilerServices

[<AutoOpenAttribute>]
module BrowserFileExtensions =

    [<Emit("new FileReader()")>]
    /// Creates a new instance of a FileReader
    let createFileReader() : FileReader = jsNative
    [<Emit("new Uint8Array($0)")>]
    let createUInt8Array(x: 'a) : byte[]  = jsNative
    /// Creates a Blob from the given input string
    [<Emit("new Blob([$0.buffer], { type: $1 })")>]
    let createBlobFromBytesAndMimeType (value: byte[]) (mimeType: string) : Blob = jsNative
    [<Emit("window.URL.createObjectURL($0)")>]
    /// Creates an object URL (also known as data url) from a Blob
    let createObjectUrl (blob: Blob) : string = jsNative
    [<Emit "URL.revokeObjectURL($0)">]
    /// Releases an existing object URL which was previously created by calling createObjectURL(). Call this method when you've finished using an object URL to let the browser know not to keep the reference to the file any longer.
    let revokeObjectUrl (dataUrl: string) : unit = jsNative

    type File with

        /// Asynchronously reads the File content as byte[]
        member instance.ReadAsByteArray() =
            Async.FromContinuations <| fun (resolve, _, _) ->
                let reader = createFileReader()
                reader.onload <- fun _ ->
                    if reader.readyState = FileReaderState.DONE
                    then resolve(createUInt8Array(reader.result))

                reader.readAsArrayBuffer(instance)

        /// Asynchronously reads the File content as a data url string
        member instance.ReadAsDataUrl() =
            Async.FromContinuations <| fun (resolve, _, _) ->
                let reader = createFileReader()
                reader.onload <- fun _ ->
                    if reader.readyState = FileReaderState.DONE
                    then resolve(unbox<string> reader.result)

                reader.readAsDataURL(instance)

        /// Asynchronously reads the File contents as text
        member instance.ReadAsText() =
            Async.FromContinuations <| fun (resolve, _, _) ->
                let reader = createFileReader()
                reader.onload <- fun _ ->
                    if reader.readyState = FileReaderState.DONE
                    then resolve(unbox<string> reader.result)

                reader.readAsText(instance)

[<Extension>]
type ByteArrayExtensions =
    [<Extension>]
    /// Saves the binary content as a file using the provided file name.
    static member SaveFileAs(content: byte[], fileName: string) =

        if String.IsNullOrWhiteSpace(fileName) then
            ()
        else
        let mimeType = "application/octet-stream"
        let blob = createBlobFromBytesAndMimeType content mimeType
        let dataUrl = createObjectUrl blob
        let anchor =  (Browser.Dom.document.createElement "a")
        anchor?style <- "display: none"
        anchor?href <- dataUrl
        anchor?download <- fileName
        anchor?rel <- "noopener"
        anchor.click()
        // clean up
        anchor.remove()
        // clean up the created object url because it is being kept in memory
        Browser.Dom.window.setTimeout(unbox(fun () -> revokeObjectUrl(dataUrl)), 40 * 1000)
        |> ignore

    [<Extension>]
    /// Saves the binary content as a file using the provided file name.
    static member SaveFileAs(content: byte[], fileName: string, mimeType: string) =

        if String.IsNullOrWhiteSpace(fileName) then
            ()
        else
        let blob = createBlobFromBytesAndMimeType content mimeType
        let dataUrl = createObjectUrl blob
        let anchor =  (Browser.Dom.document.createElement "a")
        anchor?style <- "display: none"
        anchor?href <- dataUrl
        anchor?download <- fileName
        anchor?rel <- "noopener"
        anchor.click()
        // clean up element
        anchor.remove()
        // clean up the created object url because it is being kept in memory
        Browser.Dom.window.setTimeout(unbox(fun () -> revokeObjectUrl(dataUrl)), 40 * 1000)
        |> ignore


    [<Extension>]
    /// Converts the binary content into a data url by first converting it to a Blob of type "application/octet-stream" and reading it as a data url.
    static member AsDataUrl(content: byte[]) : string =
        let blob = createBlobFromBytesAndMimeType content "application/octet-stream"
        let dataUrl = createObjectUrl blob
        dataUrl

    [<Extension>]
    /// Converts the binary content into a data url by first converting it to a Blob of the provided mime-type and reading it as a data url.
    static member AsDataUrl(content: byte[], mimeType:string) : string =
        let blob = createBlobFromBytesAndMimeType content mimeType
        let dataUrl = createObjectUrl blob
        dataUrl