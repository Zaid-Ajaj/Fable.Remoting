# Using .NET client from C#

The .NET client can also be used with C# in an idiomatic way. You will need to put the protocol definitions inside a F# project and reference the project in a C# project to have the types available from your C# application:
```cs
using System;
using System.Threading.Tasks;  
using Fable.Remoting.DotnetClient;

namespace CSharpRemoting 
{
    public class Program
    {
        static void Main(string[] args)
        {
            Task.Run(MainAsync).Wait();
        }

        static async Task MainAsync()
        {
            // create the proxy with route builder
            var proxy = Proxy.CreateFromBuilder<IServer>((typeName, funcName) => {
                return $"http://localhost:8080/api/{typeName}/{funcName}";
            });

            // Select a remote function to call and provide the arguments
            int output = await proxy.Call(server => server.getLength, "input string");

            // call a simple async value
            var result = await proxy.Call(server => server.justAsyncValue); 

            Console.WriteLine(result);
        }
    }

}
```
