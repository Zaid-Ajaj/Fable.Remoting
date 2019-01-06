# Functional Dependency Injection

> This section applies to Giraffe, Saturn and ASP.NET Core middleware adapters.

Since remoting uses records of functions as the building blocks of your application, we will be applying dependency injection to functions when building the implementation of the protocol and see how to make the protocol unit-testable. 

Lets take an example too see how this looks like starting with a simple to-do list protocol:
```fs
type Todo = {
    Id: int
    Description: string
    Done: bool
}

type ITodoApi = {
    getTodos: unit -> Async<Todo list>
}
```
As you can see, the protocol only has one function that returns a list of `Todo` items. An implementation of such protocol can be simply constructed from a static list of this type:
```fs
let todoApi = {
    getTodos = fun () ->
        async {
            return [
                { Id = 0; Description = "Fall in love with F#"; Done = true }
                { Id = 1; Description = "Learn SAFE stack"; Done = false }
            ]
        }
}
```
## Defining and implementing dependencies
So far so good, this is the basic way of constructing an implementation of the protocol but now lets look at it from a point of view of *dependencies* so you can use them to build the implementation. For this protocol the only dependency is some kind of *storage* for the `Todo` items. The store might be an "in-memory" implementation returning the static list as we did above but another implementation can be reading the `Todo` items from a database. Either way, we need an interface for the store so that multiple implementation are possbile:
```fs
type ITodoStore = 
    abstract getAllTodos: unit -> Async<Todo list>
``` 
An in-memory store might look like this:
```fs
type InMemoryTodoStore() = 
    interface ITodoStore = 
        member this.getAllTodos() = 
            async {
                return [
                    { Id = 0; Description = "Learn F#"; Done = true }
                    { Id = 1; Description = "Learn SAFE stack"; Done = false }
                ]
            }
```
## Registering dependencies
Now we want to use the store from the protocol implementation of the `ITodoApi`. First we will tap into the built-in dependency injection mechanism of ASP.NET Core ([docs](https://docs.microsoft.com/en-us/aspnet/core/fundamentals/dependency-injection?view=aspnetcore-2.2)) to register the dependencies, we do this part using `IServiceCollection` where you configure the services:

In Giraffe
```fs
let configureServices (services : IServiceCollection) =
    // register which service implementation goes to which inteface 
    services.AddSingleton<ITodoStore, InMemoryTodoStore>() |> ignore
    services.AddGiraffe() |> ignore

WebHost
    .CreateDefaultBuilder()
    .UseWebRoot(publicPath)
    .UseContentRoot(publicPath)
    .Configure(Action<IApplicationBuilder> configureApp)
    .ConfigureServices(configureServices)
    .UseUrls("http://0.0.0.0:" + port.ToString() + "/")
    .Build()
    .Run()
```
In Saturn, we use the `service_config` helper function at the `application` level:
```fs
let configureServices (services : IServiceCollection) =
    services.AddSingleton<ITodoStore, InMemoryTodoStore>()

application {
    router webApp
    service_config configureServices
    // other config options
}
```
the important part is this:
```fs
services.AddSingleton<ITodoStore, InMemoryTodoStore>()
```
Here is where you register your dependencies, with the function above you are saying: "When a dependency of type `ITodoStore` is required, return an instance of `InMemoryTodoStore`". 

Notice that we are using `AddSingleton` which means the `InMemoryTodoStore` instance is created once and is re-used everytime you require `ITodoStore` through out the lifetime of the application. `AddSingleton` is one way of controlling the lifetime of a dependency. In ASP.NET Core there are other ways as well:

 - `AddTransient`: an instance of the dependency is created every time the dependency is required which can happen multiple times during a single request. 
 - `AddScoped`: an instance of the dependency is created once and re-used *per request*. 

Read more on [Service lifetime](https://docs.microsoft.com/en-us/aspnet/core/fundamentals/dependency-injection?view=aspnetcore-2.2#service-lifetimes) in ASP.NET Core.


### Requiring the dependencies
Now that the dependencies are registered, we can start using them from the implementation. Requiring one of the registered dependencies is commonly referred to as *resolving the services* or *requiring the services*. For resolving a registered dependency we will need to [access the HttpContext](request-context.md) and there will be a function `GetService<'T>` extension function (from Giraffe) that we will use. 

To effectively use the dependencies and make your protocol implmentation unit-testable we will need two functions:

- A function that constructs the protocol implementation from dependencies
- A function that requires dependencies from the `HttpContext`

Having two seperate function is necessary in order to make the protocol implementation unit-testable. The first function looks like this:
```fs
// create protocol implemention using the depndencies
let createTodoApi (store: ITodoStore) : ITodoApi = 
  {
    getTodos = fun () ->
        async { 
            return! store.getAllTodos() 
        }
  }
```
Now the second function:
```fs
let createTodoApiFromContext (httpContext: HttpContext) : ITodoApi = 
    let todoStore = httpContext.GetService<ITodoStore>()
    createTodoApi todoStore
```
And that is it, you can now construct the API using the `Remoting` module:
```fs
let webApi = 
    Remoting.createApi()
    |> Remoting.fromContext createTodoApiFromContext
    |> Remoting.buildHttpHander
```
## Requiring multiple dependencies
If you had more dependencies, say you need a logger, you only need to extend the definition of your first function with another dependency and require the dependency from the `HttpContext`:

> open Microsoft.Extensions.Logging

```fs
let createTodoApi (store: ITodoStore) (logger: ILogger<ITodoApi>) : ITodoApi = 
  {
    getTodos = fun () -> 
        async {
            do logger.LogInformation("Reading todo items from the store")
            return! store.getAllTodos()
        }
  }
```
and then require the logger from the `HttpContext`:
```fs
let createTodoApiFromContext (httpContext: HttpContext) : ITodoApi = 
    let todoStore = httpContext.GetService<ITodoStore>()
    let logger = httpContext.GetService<ILogger<ITodoApi>>()
    createTodoApi todoStore logger
```
You might be wondering where this `ILogger<'T>` is coming from since you only registered one dependency. `ILogger<'T>` is one of the [Framework-provided](https://docs.microsoft.com/en-us/aspnet/core/fundamentals/dependency-injection?view=aspnetcore-2.2#framework-provided-services) services from ASP.NET Core that your application can simply require and they will be ready to use.

## Nested dependencies

Your dependencies, like `InMemoryTodoStore`, can have other (nested) dependencies themselves. Lets say that you want to do some logging from within `InMemoryTodoStore`, you only need to specify the logger in the constructor of the type:
```fs
type InMemoryTodoStore(logger: ILogger<InMemoryTodoStore>) = 
    interface ITodoStore = 
        member this.getAllTodos() = 
            async {
                do logger.LogInformation("Returning a static list of to-do items")

                return [
                    { Id = 0; Description = "Learn F#"; Done = true }
                    { Id = 1; Description = "Learn SAFE stack"; Done = false }
                ]
            }
```
The reason that we have `InMemoryTodoStore` as class and not a function is so that ASP.NET Core will understand how to resolve these nested dependencies from the constructor.

## Requiring dependencies for seperate protocol functions

Previously, we have used two functions for the protocol implementation, one that uses the dependencies to create the protocol and one that requires the dependencies from the `HttpContext`. This way your protocol implementation becomes unit-testable because in your unit-tests you construct the test dependencies yourself and construct the protocol without requiring them from `HttpContext`. However, this can become very messy if you have a lot of dependencies, say 3 different dependencies per function, so that you have to construct *every* dependency if you want to test a single part (function) of the protocol. 

A better way is making your little functions unit-testable instead of the making the whole protocol unit-testable. You define each function *seperately* along with the dependencies it requires, then resolve these from the `HttpContext`:
```fs
let getTodos (store: ITodoStore) (logger: ILogger<ITodoApi>) = 
    fun () -> 
        async {
            do logger.LogInformation("reading to-do items")
            return! store.getAllTodos()
        }

let createTodoApi (httpContext: HttpContext) : ITodoApi = 
    let todoStore = httpContext.GetService<ITodoStore>()
    let logger = httpContext.GetService<ILogger<ITodoApi>>()
    let todoApi = {
        getTodos = getTodos todoStore logger
    }

    todoApi

// constuct the API
Remoting.createApi()
|> Remoting.fromContext createTodoApi
|> Remoting.buildHttpHander
``` 
Now the `getTodos` function is unit-testable on it's own and the same idea holds if your protocol had more functions:
```fs
type ITodoApi = {
    getTodos : unit -> Async<Todo list>
    getTodoById : int -> Async<Option<Todo>>
} 

let getTodos (store: ITodoStore) (logger: ILogger<ITodoApi>) = 
    fun () -> 
        async {
            do logger.LogInformation("reading to-do items")
            return! store.getAllTodos()
        }

let getTodoById (store: ITodoStore) = 
    fun todoId ->
        async {
            let! todos = store.getAllTodos()
            let foundTodo = todos |> List.tryFind (fun todo -> todo.Id = todoId)
            return foundTodo
        }

let createTodoApi (httpContext: HttpContext) : ITodoApi = 
    let todoStore = httpContext.GetService<ITodoStore>()
    let logger = httpContext.GetService<ILogger<ITodoApi>>()
    let todoApi = {
        getTodos = getTodos todoStore logger
        getTodoById = getTodoById todoStore
    }

    todoApi
```
And so on and so forth. This way makes for a clean and simple approach to dependency injection.

## Going even further: using the built-in Reader monad. 

Ever since version 3.x of remoting, the final rewrite, remoting has included the `reader` monad which is way of resolving dependencies in a functional manner. With the `reader` monad, the above example can be refactored in two ways:

- Writing a single *reader function* for the `ITodoApi`
- Writing a reader function for each seperate protocol function

The first one looks like this:
```fs
type ITodoApi = {
    getTodos : unit -> Async<Todo list>
    getTodoById : int -> Async<Option<Todo>>
} 

let getTodos (store: ITodoStore) (logger: ILogger<ITodoApi>) = 
    fun () -> 
        async {
            do logger.LogInformation("reading to-do items")
            return! store.getAllTodos()
        }

let getTodoById (store: ITodoStore) = 
    fun todoId ->
        async {
            let! todos = store.getAllTodos()
            let foundTodo = todos |> List.tryFind (fun todo -> todo.Id = todoId)
            return foundTodo
        }       

let todoApiReader = 
    reader {
        let! store = resolve<ITodoStore>()
        let! logger = resolve<ILogger<ITodoApi>>()
        return { 
            getTodos = getTodos store logger
            getTodoById = getTodoById store
        }
    }
```
This simplifies the construction of the `ITodoApi` so that you don't need to work with `HttpContext` or even think about it being there. Now you can the use `fromReader` function in the `Remoting` module to expose the API to Http:
```fs
let webApi = 
  Remoting.createApi()
  |> Remoting.fromReader todoApiReader
  |> Remoting.buildHttpHandler
```
The second way of refactoring looks like this:

```fs
type ITodoApi = {
    getTodos : unit -> Async<Todo list>
    getTodoById : int -> Async<Option<Todo>>
} 

let getTodos (store: ITodoStore) (logger: ILogger<ITodoApi>) = 
    fun () -> 
        async {
            do logger.LogInformation("reading to-do items")
            return! store.getAllTodos()
        }

// reader for getTodos
let getTodosReader = 
    reader {
        let! store = resolve<ITodoStore>()
        let! logger = resolve<ILogger<ITodoApi>>()
        return getTodos store logger
    }

let getTodoById (store: ITodoStore) = 
    fun todoId ->
        async {
            let! todos = store.getAllTodos()
            let foundTodo = todos |> List.tryFind (fun todo -> todo.Id = todoId)
            return foundTodo
        }

let getTodoByIdReader = 
    reader {
        let! store = resolve<ITodoStore>()
        return getTodoById store
    }

let todoApiReader = 
    reader {
        let! getTodos = getTodosReader
        let! getTodoById = getTodoByIdReader 
        return {
            getTodos = getTodos
            getTodoById = getTodoById
        }
    }

let webApi = 
  Remoting.createApi()
  |> Remoting.fromReader todoApiReader
  |> Remoting.buildHttpHandler
``` 