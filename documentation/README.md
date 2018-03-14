# Fable.Remoting

Fable.Remoting is a library that enables type-safe client-server communication for F# featuring the popular web frameworks on the server and [Fable](http://fable.io/) on the client.This library lets you think about your client-server interactions in terms of pure stateless functions while using HTTP internally for communication.  

As the name suggests, it is inspired by [Websharper's Remoting](https://developers.websharper.com/docs/v4.x/fs/remoting) feature but it uses fundementally different mechanism to achieve the same goal. 

Supported Web Frameworks
 - [Suave](https://github.com/SuaveIO/suave)
 - [Giraffe](https://github.com/giraffe-fsharp/Giraffe)
 - [Saturn](https://github.com/SaturnFramework/Saturn)

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

## Scaffold from scratch
You can also create a project from scratch and add Fable.Remoting yourself, start off by [modeling the shared API](src/modeling-api.md).