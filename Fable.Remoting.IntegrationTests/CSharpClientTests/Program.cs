using System;
using Fable.Remoting.DotnetClient; 
using CSharpClientDefs;
using TinyTest;
using Microsoft.FSharp.Core;

namespace CSharpClientTests
{
    class Program
    {
        static int Main(string[] args)
        {
            var proxy = Proxy.CreateFromBuilder<IServer>((typeName, funcName) => {
                return $"http://localhost:8080/api/{typeName}/{funcName}";
            }); 

            Test.Module("CSharp Client Tests"); 

            Test.CaseAsync("IServer.echoLongInGenericUnion", async () => 
            {
                var input = Maybe<long>.NewJust(10); 
                var output = await proxy.Call(server => server.echoLongInGenericUnion, input);
                Test.Equal(input, output);
            }); 

            Test.CaseAsync("IServer.echoRecord works", async () => 
            {
                var input = new Record("one", 20, FSharpOption<int>.None);
                var output = await proxy.Call(server => server.echoRecord, input);
                Test.Equal(input, output);
            });

            Test.CaseAsync("IServer.multiArgFunc", async () => 
            {
                var output = await proxy.Call(server => server.multiArgFunc, "hello", 10, false);
                Test.Equal(15, output);
            }); 

            Test.CaseAsync("IServer.pureAsync", async () => 
            {
                var output = await proxy.Call(server => server.pureAsync);
                Test.Equal(42, output);
            });

            return Test.Report();
        }
    }
}
