using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;

namespace Fable.Remoting.Reflection
{
    public static class FSharpRecord
    {
        static B Pipe<A, B>(this A x, Func<A, B> f) => f(x);

        public static dynamic Invoke(string methodName, object implementation, object arg)
        {
              return implementation
                       .GetType()
                       .GetProperty(methodName)
                       .GetValue(implementation, null)
                       .Pipe((dynamic fsFunc) => fsFunc.Invoke((dynamic)arg));
        }
    }
}
