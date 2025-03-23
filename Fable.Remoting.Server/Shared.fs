namespace Fable.Remoting.ClientServer

/// This is the error type serialized when using ErrorResult.Propagate in an error handler on the server side.
/// This serialized error type is retrievable on the client side through ProxyRequestException.ResponseText.
type CustomErrorResult<'userError> =
    { error: 'userError;
      ignored: bool;
      handled: bool; }
