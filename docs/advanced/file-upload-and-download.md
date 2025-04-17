# File Upload And Download

With Fable.Remoting, it is really simple to upload and download files and other binary content and it goes like this:
 - Use `byte[]` as a top level parameter (rather than storing it in a record) of a remoting function for _upload_ `Upload: FileMetadata -> UserId -> byte[] -> Async<UploadResult>`
 - Use `Async<byte[]>` as output of a remoting function to _download_ `Download: string -> Async<byte[]>`

> When using `byte[]` as output, the HTTP response is automatically sent as `application/octet-stream`, bypassing JSON serialization entirely.

> When using `byte[]` as input, the client proxy may be configured with `Remoting.withMultipartOptimization`, so that the HTTP request is sent as `multipart/form-data` with minimal overhead for binary data.
> If this option is not enabled, the byte array will be encoded in JSON instead, which is much more inefficient.
>
> !!! `Fable.Remoting.Suave` **does not** support reading multipart requests, so if your backend runs on Suave, do not call `Remoting.withMultipartOptimization`.

However, that is of course not the full story, because first of all, how do you get a `byte[]` from a file coming through the browser for upload. For downloading, once you get `byte[]` from the backend, how do you save it as a file on the user system?

### From `File` to `byte[]`

When you use an `<input type="file" />` from the browser, you can obtain a `File` instance of the selected file which you can upload to the backend. The `Fable.Remoting.Client` package includes an extension function called `ReadAsByteArray()` which does what it says: converts the file into raw `byte[]` that can be used as input for uploading:
```fsharp
open Fable.Remoting.Client
open Browser.Types

let BackendApi = 
    Remoting.createApi()
    |> Remoting.withMultipartOptimization
    |> Remoting.buildProxy<Api>

let uploadProfilePicture (image: File) userId = async {
    let! fileBytes = image.ReadAsByteArray()
    let! output = BackendApi.Upload({ ImageAuthor = "blah" }, userId, fileBytes)
    return output
}
```

### Downloading and Saving Files

When you receive `byte[]` from the backend (for a download), you can call the `SaveFileAs(fileName: string) : unit` extension function on the binary data to save it as a file on the users system:
```fsharp
open Fable.Remoting.Client

let download (fileName: string) = async {
    let! downloadedFile = BackendApi.Download(fileName)
    downloadedFile.SaveFileAs(fileName)
}
```
The `SaveFileAs` funcion detects the mime-type/content-type automatically based on the file extension of the file input. Alternatively, you can provide your own content type if you wish as follows:
```fsharp
let download (fileName: string) = async {
    let! downloadedFile = BackendApi.Download(fileName)
    downloadedFile.SaveFileAs(fileName, "images/png")
}
```

### Data URLs From `byte[]`

`Fable.Remoting.Client` includes another extension function for `byte[]` which converts it to a [Data URI](https://developer.mozilla.org/en-US/docs/Web/HTTP/Basics_of_HTTP/Data_URIs) as follows. This is useful for example if you are returning images from the backend and you want to show them easily:
```fsharp
open Fable.Remoting.Client

// data
let imageData : byte[] = ...
let dataUrl = imageData.AsDataUrl()

// view
Html.img [ prop.src dataUrl ]
```