using System;
using System.Reflection;
using Microsoft.CSharp;

namespace Fable.Remoting.Reflection
{
    public static class FSharpRecord
    {
        static B Pipe<A, B>(this A x, Func<A, B> f) => f(x);

        public static dynamic Invoke(string methodName, object implementation, object arg, bool hasArg)
        {
            return implementation
                     .GetType()
                     .GetProperty(methodName)
                     .GetValue(implementation, null)
                     .Pipe((dynamic fsFunc) => hasArg ? fsFunc.Invoke((dynamic)arg) : fsFunc.Invoke(null));
        }
    }
}
