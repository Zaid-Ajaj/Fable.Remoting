namespace Fable.Remoting.DotnetClient

open System.Runtime.CompilerServices
open Fable.Remoting.ClientServer

[<Extension>]
type ProxyRequestExceptionExtensions =

    /// Tries to parse the ResponseText into a typed error result.
    [<Extension>]
    static member ParseCustomErrorResult<'userError>(proxyRequestException: Http.ProxyRequestException)
        : CustomErrorResult<'userError> option
        =
        try
            Proxy.parseAs(proxyRequestException.ResponseText) |> Some
        with | _ -> None
