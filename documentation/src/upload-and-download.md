# File Upload And Download

With Fable.Remoting, it is really simple to upload and download files and other binary content and it goes like this:
 - Use `byte[]` as input of a remoting function for _upload_ `Upload : byte[] -> Async<unit>`
 - Use `Async<byte[]>` as output of a remoting function to _download_ `Download : string -> Async<byte[]>`

> When using `byte[]` as input or output, the data transport is automatically optimized for binary content and will bypass the JSON serialization phase entirely.

However, that is of course not the full story, because first of all, how do you get a `byte[]` from a file coming through the browser for upload. For downloading, once you get `byte[]` from the backend, how do you save it as a file on the user system?

### From `File` to `byte[]`

When you use an `<input type="file" />` from the browser, you can obtain a `File` instance of the selected file which you can upload to the backend. The `Fable.Remoting.Client` package includes an extension function called `ReadAsByteArray()` which does what it says: converts the file into raw `byte[]` that can be used as input for uploading:
```fs
open Fable.Remoting.Client
open Browser.Types

let upload (file: File) = async {
    let! fileBytes = file.ReadAsByteArray()
    let! output = BackendApi.Upload(fileBytes)
    return output
}
```

### Downloading and Saving Files

When you receive `byte[]` from the backend (for a download), you can call the `SaveFileAs(fileName: string) : unit` extension function on the binary data to save it as a file on the users system:
```fs
open Fable.Remoting.Client

let download (fileName: string) = async {
    let! downloadedFile = BackendApi.Download(fileName)
    downloadedFile.SaveFileAs(fileName)
}
```
The `SaveFileAs` funcion detects the mime-type/content-type automatically based on the file extension of the file input. Alternatively, you can provide your own content type if you wish as follows:
```fs
let download (fileName: string) = async {
    let! downloadedFile = BackendApi.Download(fileName)
    downloadedFile.SaveFileAs(fileName, "images/png")
}
```

### Data URLs From `byte[]`

`Fable.Remoting.Client` includes another extension function for `byte[]` which converts it to a [Data URI](https://developer.mozilla.org/en-US/docs/Web/HTTP/Basics_of_HTTP/Data_URIs) as follows. This is useful for example if you are returning images from the backend and you want to show them easily:
```fs
open Fable.Remoting.Client

// data
let imageData : byte[] = ...
let dataUrl = imageData.AsDataUrl()

// view
Html.img [ prop.src dataUrl ]
```