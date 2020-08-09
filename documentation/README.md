# Fable.Remoting

Fable.Remoting is a [RPC](https://en.wikipedia.org/wiki/Remote_procedure_call) communication layer for Fable and .NET apps featuring [Suave](https://github.com/SuaveIO/suave), [Giraffe](https://github.com/giraffe-fsharp/Giraffe), [Saturn](https://github.com/SaturnFramework/Saturn) or any [Asp.net core](https://docs.microsoft.com/en-us/aspnet/core/?view=aspnetcore-2.1) application on the server. On the client you can have either a [Fable](http://fable.io/) project or [.NET Apps](src/dotnet-client.md) like Xamarin or WPF. This library lets you think about your client-server interactions in terms of pure stateless functions by defining a shared interface (see [Getting started](src/basics.md)) that is used by both the client and server.

As the name suggests, the library is inspired by the awesomeness of [Websharper's Remoting](https://developers.websharper.com/docs/v4.x/fs/remoting) feature but it uses different mechanism to achieve type-safety.

## Quick Start
Use the [SAFE Simplified template](https://github.com/Zaid-Ajaj/SAFE.Simplified) where Fable.Remoting is already set up and ready to go

See `Fable.Remoting` in action demonstrated in the awesome talk at [FSharp eXchange 2018](https://skillsmatter.com/skillscasts/11308-safe-apps-with-f-web-stack) by [Tomasz Heimowski](https://github.com/theimowski)