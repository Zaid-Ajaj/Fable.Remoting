# Fable.Remoting

Fable.Remoting is a library that enables type-safe client-server communication for F# featuring [Suave](https://github.com/SuaveIO/suave), [Giraffe](https://github.com/giraffe-fsharp/Giraffe) or [Saturn](https://github.com/SaturnFramework/Saturn) on the server. On the client you can have either a [Fable](http://fable.io/) project or any [.NET App](src/dotnet-client.md) like Xamarin or WPF.This library lets you think about your client-server interactions in terms of pure stateless functions by defining a shared interface (see [Getting started](src/basics.md)) that is used by both the client and server.

As the name suggests, it is inspired by [Websharper's Remoting](https://developers.websharper.com/docs/v4.x/fs/remoting) feature but it uses different mechanism to achieve the same goal. 

## Quick Start
You start off using the [SAFE template](https://github.com/SAFE-Stack/SAFE-template) where Fable.Remoting is one of the scaffolding options:
```bash
# install the template
dotnet new -i SAFE.Template
# scaffold a new Fable/Suave project with Fable.Remoting
dotnet new SAFE --Remoting
# Giraffe as your server
dotnet new SAFE --Server giraffe --Remoting
# Saturn on the server
dotnet new SAFE --Server saturn --Remoting
``` 

See `Fable.Remoting` in action demonstrated in the awesome talk at [Lambda Days 2018](https://www.youtube.com/watch?v=LBekZt8QB4w) by [Tomasz Heimowski](https://github.com/theimowski)

## Scaffold from scratch
You can also create a project from scratch and add Fable.Remoting yourself, start off by [modeling the shared API](src/modeling-api.md).