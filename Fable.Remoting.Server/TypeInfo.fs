namespace Fable.Remoting.Server 

open System
open FSharp.Reflection

[<RequireQualifiedAccess>]
module TypeInfo = 
    
    let (|RecordType|_|) (t: Type) = 
        if FSharpType.IsRecord t 
        then 
            FSharpType.GetRecordFields t
            |> Array.map (fun prop -> prop.Name, prop.PropertyType)
            |> Some 
        else None  

    let (|SetType|_|) (t: Type) = 
        if t.FullName.StartsWith "Microsoft.FSharp.Collections.FSharpSet`1"
        then t.GetGenericArguments().[0] |> Some 
        else None 

    let (|AsyncType|_|) (t: Type) =     
        if t.FullName.StartsWith("Microsoft.FSharp.Control.FSharpAsync`1") 
        then  t.GetGenericArguments().[0] |> Some 
        else None

    let (|UnionType|_|) (t: Type) = 
        if FSharpType.IsUnion t 
        then 
            FSharpType.GetUnionCases t 
            |> Array.map (fun info -> 
                let caseName = info.Name 
                let caseTypes = info.GetFields() |> Array.map (fun prop -> prop.PropertyType)
                caseName, info, caseTypes)
            |> Some 
        else None 
    
    let (|MapType|_|) (t: Type) = 
        if (t.FullName.StartsWith "Microsoft.FSharp.Collections.FSharpMap`2")
        then 
            let genArgs = t.GetGenericArguments()
            Some (genArgs.[0], genArgs.[1])
        else None 

    let (|ListType|_|) (t: Type) = 
        if (t.FullName.StartsWith "Microsoft.FSharp.Collections.FSharpList`1") 
        then t.GetGenericArguments().[0] |> Some 
        else None

    let rec flattenFuncTypes (typeDef: Type) = 
        [| if FSharpType.IsFunction typeDef 
           then let (domain, range) = FSharpType.GetFunctionElements typeDef 
                yield! flattenFuncTypes domain 
                yield! flattenFuncTypes range
           else yield typeDef |]

    let (|FuncType|_|) (t: Type) = 
        if FSharpType.IsFunction t 
        then flattenFuncTypes t |> Some 
        else None 

    let (|ArrayType|_|) (t:Type) = 
        if (t.FullName.EndsWith "[]")
        then t.GetElementType() |> Some 
        else None 
    
    let (|OptionType|_|) (t:Type) = 
        if (t.FullName.StartsWith "Microsoft.FSharp.Core.FSharpOption`1")
        then t.GetGenericArguments().[0] |> Some 
        else None 

    let (|TupleType|_|) (t: Type) = 
        if t.FullName.StartsWith "System.Tuple`" 
        then FSharpType.GetTupleElements(t) |> Some 
        else None 
    
    let (|SeqType|_|) (t: Type) = 
        if t.FullName.StartsWith "System.Collections.Generic.IEnumerable`1"
        then  t.GetGenericArguments().[0] |> Some 
        else None 
