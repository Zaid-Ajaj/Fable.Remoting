namespace Fable.Remoting.Client

open System
open Browser.Types
open Fable.Core
open Fable.Core.JsInterop
open System.Runtime.CompilerServices

/// Utilities for working with binary data types in the browser
module InternalUtilities =
    /// Creates a new instance of a FileReader
    [<Emit("new FileReader()")>]
    let createFileReader() : FileReader = jsNative
    [<Emit("new Uint8Array($0)")>]
    let createUInt8Array(x: 'a) : byte[]  = jsNative
    /// Creates a Blob from the given input string
    [<Emit("new Blob([$0.buffer], { type: $1 })")>]
    let createBlobFromBytesAndMimeType (value: byte[]) (mimeType: string) : Blob = jsNative
    /// Creates an object URL (also known as data url) from a Blob
    [<Emit("window.URL.createObjectURL($0)")>]
    let createObjectUrl (blob: Blob) : string = jsNative
    /// Releases an existing object URL which was previously created by calling createObjectURL(). Call this method when you've finished using an object URL to let the browser know not to keep the reference to the file any longer.
    [<Emit "URL.revokeObjectURL($0)">]
    let revokeObjectUrl (dataUrl: string) : unit = jsNative
    /// Returns whether the input byte array is a typed array of type Uint8Array
    [<Emit "$0 instanceof Uint8Array">]
    let isUInt8Array (data: byte[]) : bool = jsNative
    /// Creates a typed byte array of binary data if it not already typed
    let toUInt8Array(data: byte[]) : byte[] =
        if isUInt8Array data
        then data
        else createUInt8Array data

[<AutoOpenAttribute>]
module BrowserFileExtensions =

    type File with

        /// Asynchronously reads the File content as byte[]
        member instance.ReadAsByteArray() =
            Async.FromContinuations <| fun (resolve, _, _) ->
                let reader = InternalUtilities.createFileReader()
                reader.onload <- fun _ ->
                    if reader.readyState = FileReaderState.DONE
                    then resolve(InternalUtilities.createUInt8Array(reader.result))

                reader.readAsArrayBuffer(instance)

        /// Asynchronously reads the File content as a data url string
        member instance.ReadAsDataUrl() =
            Async.FromContinuations <| fun (resolve, _, _) ->
                let reader = InternalUtilities.createFileReader()
                reader.onload <- fun _ ->
                    if reader.readyState = FileReaderState.DONE
                    then resolve(unbox<string> reader.result)

                reader.readAsDataURL(instance)

        /// Asynchronously reads the File contents as text
        member instance.ReadAsText() =
            Async.FromContinuations <| fun (resolve, _, _) ->
                let reader = InternalUtilities.createFileReader()
                reader.onload <- fun _ ->
                    if reader.readyState = FileReaderState.DONE
                    then resolve(unbox<string> reader.result)

                reader.readAsText(instance)

[<Extension>]
type ByteArrayExtensions =
    /// Saves the binary content as a file using the provided file name.
    [<Extension>]
    static member SaveFileAs(content: byte[], fileName: string) =

        if String.IsNullOrWhiteSpace(fileName) then
            ()
        else
        let mimeType = "application/octet-stream"
        let binaryData = InternalUtilities.toUInt8Array content
        let blob = InternalUtilities.createBlobFromBytesAndMimeType binaryData mimeType
        let dataUrl = InternalUtilities.createObjectUrl blob
        let anchor =  (Browser.Dom.document.createElement "a")
        anchor?style <- "display: none"
        anchor?href <- dataUrl
        anchor?download <- fileName
        anchor?rel <- "noopener"
        anchor.click()
        // clean up
        anchor.remove()
        // clean up the created object url because it is being kept in memory
        Browser.Dom.window.setTimeout(unbox(fun () -> InternalUtilities.revokeObjectUrl(dataUrl)), 40 * 1000)
        |> ignore

    /// Saves the binary content as a file using the provided file name.
    [<Extension>]
    static member SaveFileAs(content: byte[], fileName: string, mimeType: string) =

        if String.IsNullOrWhiteSpace(fileName) then
            ()
        else
        let binaryData = InternalUtilities.toUInt8Array content
        let blob = InternalUtilities.createBlobFromBytesAndMimeType binaryData mimeType
        let dataUrl = InternalUtilities.createObjectUrl blob
        let anchor =  Browser.Dom.document.createElement "a"
        anchor?style <- "display: none"
        anchor?href <- dataUrl
        anchor?download <- fileName
        anchor?rel <- "noopener"
        anchor.click()
        // clean up element
        anchor.remove()
        // clean up the created object url because it is being kept in memory
        Browser.Dom.window.setTimeout(unbox(fun () -> InternalUtilities.revokeObjectUrl(dataUrl)), 40 * 1000)
        |> ignore

    
    /// Converts the binary content into a data url by first converting it to a Blob of type "application/octet-stream" and reading it as a data url.
    [<Extension>]
    static member AsDataUrl(content: byte[]) : string =
        let binaryData = InternalUtilities.toUInt8Array content
        let blob = InternalUtilities.createBlobFromBytesAndMimeType binaryData "application/octet-stream"
        let dataUrl = InternalUtilities.createObjectUrl blob
        dataUrl

    /// Converts the binary content into a data url by first converting it to a Blob of the provided mime-type and reading it as a data url.
    [<Extension>]
    static member AsDataUrl(content: byte[], mimeType:string) : string =
        let binaryData = InternalUtilities.toUInt8Array content
        let blob = InternalUtilities.createBlobFromBytesAndMimeType binaryData mimeType
        let dataUrl = InternalUtilities.createObjectUrl blob
        dataUrl