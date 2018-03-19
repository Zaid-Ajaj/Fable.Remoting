namespace Fable.Remoting.Reflection

type FSharpRecord =
    static member Invoke(methodName, implementation, args) =
        let propInfo = implementation.GetType().GetProperty(methodName)
        let innerValue = propInfo.GetValue(implementation,null)
        let func =
            innerValue.GetType().GetMethods()
            |> Array.find (fun m -> m.Name = "Invoke")
        func.Invoke(innerValue,
            match args with
            |[||] -> [|null|]
            |args -> args)