using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Reflection;
using Microsoft.CSharp;
using Microsoft.FSharp.Collections;
using Microsoft.FSharp.Control;
using Microsoft.FSharp.Core;

namespace Fable.Remoting.Reflection
{
    public static class FSharpRecord
    {
        static B Pipe<A, B>(this A x, Func<A, B> f) => f(x);

        public static dynamic Invoke(string methodName, object implementation, object arg, bool hasArg)
        {
            object funcObj =
                 implementation
                    .GetType()
                    .GetProperty(methodName)
                    .GetValue(implementation, null);

            var funcMethods =
                funcObj.GetType()
                       .GetMethods();

            var func = funcMethods.First(x => x.Name == "Invoke");        

            return hasArg ? func.Invoke(funcObj, new object[] { (dynamic)arg })
                          : func.Invoke(funcObj, new object[] { null });

                     //.Pipe((FSharpFunc<dynamic, dynamic> fsFunc) => hasArg ? fsFunc.Invoke((dynamic)arg) : fsFunc.Invoke(null));
        }
    }
}
