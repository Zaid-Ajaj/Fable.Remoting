# Modeling Authentication

When it comes to web applications, authentication and authorization are usually implemented in terms of cookies and headers of requests and responses, this is not the case when using `Fable.Remoting` to implement security features. This is because we are only using stateless pure functions and thus we need to *"forget"* about HTTP (headers/cookies/session state) when modeling authentication and authorization for our application.  

> Note: the following modeling technique is the *recommended* implementation of auth when using Fable.Remoting. However, it is not the only way and it is still possible to use Http  mechanisms (headers/cookies) to secure your end points, see [Implicit Authentication](implicit-authentication.md).

The following API models a book store interface with some of the functions requiring the user to be logged in and other functions to publicly available. We use stateless tokens that we pass around to authenticate or authorize the users:

```fs
// Shared.fs

type AuthToken = AuthToken of string
type LoginInfo = { Username: string; Password: string }

// possible errors when logging in
type LoginError = 
    | UserDoesNotExist
    | PasswordIncorrect
    | AccountBanned

// a request with a token
type SecureRequest<'t> = { Token : string; Content : 't }

// possible authentication/authorization errors     
type AuthError = 
   | UserTokenExpired
   | TokenInvalid
   | UserDoesNotHaveAccess

type BookId = BookId of int
// domain model
type Book = { Id: int; Title: string; (* other propeties *) }

// things that could go wrong 
// when removing a book from a users wishlist
type BookRemovalFromWithList = 
    | BookSuccessfullyRemoved
    | BookDoesNotExist

// the book store protocol
type IBookStoreApi = {
    // login to acquire an auth token   
    login : LoginInfo -> Async<Result<AuthToken, LoginError>>
    // "public" function: no auth needed
    searchBooksByTitle : string -> Async<list<Book>> 
    // secure function, requires a token
    booksOnWishlist : AuthToken -> Async<Result<list<Book>, AuthError>>, 
    // secure function, requires a token and a book id
    removeBookFromWishlist : SecureRequest<BookId> -> Async<Result<BookRemovalFromWithList, AuthError>>
    // etc . . . 
}
```
Just by looking at the types you can tell what the application does. Implementing such interface on the server requires that you generate an authorization/authentication token when invoking the `login` function, [JSON Web Tokens](https://jwt.io/) are good match for this type of scenario's and then use the resulting token in subsequent requests. There is no *implicit* logged in user nor some "user context", everything is stateless and explicit. 

The drawback of this approach is that you have to validate the auth token yourself for every secure function. 