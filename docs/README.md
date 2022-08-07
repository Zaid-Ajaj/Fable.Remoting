# Fable.Remoting

Fable.Remoting is a [RPC](https://en.wikipedia.org/wiki/Remote_procedure_call) communication layer for Fable and .NET apps featuring [Suave](https://github.com/SuaveIO/suave), [Giraffe](https://github.com/giraffe-fsharp/Giraffe), [Saturn](https://github.com/SaturnFramework/Saturn) or any [Asp.net core](https://docs.microsoft.com/en-us/aspnet/core/?view=aspnetcore-2.1) application on the server. On the client you can have either a [Fable](http://fable.io/) project or [.NET Apps](src/dotnet-client.md) like Xamarin or WPF. This library lets you think about your client-server interactions in terms of pure stateless functions by defining a shared interface that is used by both the client and server.

As the name suggests, the library is inspired by the awesomeness of [Websharper's Remoting](https://developers.websharper.com/docs/v4.x/fs/remoting) feature but it uses different approach to achieve type-safety.

### Quick Start
Use the [SAFE Stack](https://safe-stack.github.io/docs/) where Fable.Remoting is already set up and ready to go for full stack F# web development. 

Learn more in-depth about Fable.Remoting from my talk at NDC Oslo 2021 where I explain and demonstrate how Fable.Remoting works and the class of problems that it solved. 

<iframe width="560" height="315" src="https://www.youtube.com/embed/6bkZeR0ptqc" title="YouTube video player" frameborder="0" allow="accelerometer; autoplay; clipboard-write; encrypted-media; gyroscope; picture-in-picture" allowfullscreen></iframe>