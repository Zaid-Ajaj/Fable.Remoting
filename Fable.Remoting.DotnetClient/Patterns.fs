namespace Fable.Remoting.DotnetClient

open Quotations.Patterns
open Quotations.DerivedPatterns
open FSharp.Reflection

module Patterns =
    let (| AsyncField |_|) = function 
        | PropertyGet (Some (_), method, []) -> 
            Some(method.Name)
        | _ -> None 
    let (| NoArgs |_|) = function 
        | Lambda(_, AsyncField(methodName)) -> 
            Some (methodName, [ ])
        | _ -> None

    let (|UnionValue|_|) = function 
        | NewUnionCase(info, [ ]) -> 
            FSharpValue.MakeUnion(info, [|  |]) |> Some
        | NewUnionCase(info, [ Value(value, _) ]) -> 
            FSharpValue.MakeUnion(info, [| value |]) |> Some
        | NewUnionCase(info, [ Value(arg1, _);  Value(arg2, _); ]) -> 
            FSharpValue.MakeUnion(info, [| arg1; arg2; |]) |> Some
        | NewUnionCase(info, [ Value(arg1, _);  Value(arg2, _);  Value(arg3, _); ]) -> 
            FSharpValue.MakeUnion(info, [| arg1; arg2; arg3 |]) |> Some
        | NewUnionCase(info, [ Value(arg1, _);  Value(arg2, _);  Value(arg3, _); Value(arg4, _) ]) -> 
            FSharpValue.MakeUnion(info, [| arg1; arg2; arg3; arg4 |]) |> Some
        | NewUnionCase(info, [ Value(arg1, _);  Value(arg2, _);  Value(arg3, _); Value(arg4, _); Value(arg5, _) ]) -> 
            FSharpValue.MakeUnion(info, [| arg1; arg2; arg3; arg4; arg4 |]) |> Some
        | _ -> None

    let (|RecordValue|_|) = function 
        | NewRecord(recordType, [ Value(field, _) ]) -> 
            FSharpValue.MakeRecord(recordType, [| field |]) |> Some
        | NewRecord(recordType, [ Value(arg1, _);  Value(arg2, _); ]) -> 
            FSharpValue.MakeRecord(recordType, [| arg1; arg2; |]) |> Some
        | NewRecord(recordType, [ Value(arg1, _);  Value(arg2, _);  Value(arg3, _); ]) -> 
            FSharpValue.MakeRecord(recordType, [| arg1; arg2; arg3 |]) |> Some
        | NewRecord(recordType, [ Value(arg1, _);  Value(arg2, _);  Value(arg3, _); Value(arg4, _) ]) -> 
            FSharpValue.MakeRecord(recordType, [| arg1; arg2; arg3; arg4 |]) |> Some
        | NewRecord(recordType, [ Value(arg1, _);  Value(arg2, _);  Value(arg3, _); Value(arg4, _); Value(arg5, _) ]) -> 
            FSharpValue.MakeRecord(recordType, [| arg1; arg2; arg3; arg4; arg4 |]) |> Some
        | _ -> None
    let (| ProvidedValue |_|) = function 
        | Value(value, _ ) -> Some value 
        | UnionValue value -> Some value
        | _ -> None
    let (| OneArg |_|) = function 
        | Lambda(_, Application (AsyncField(methodName), ProvidedValue(value))) ->
            Some (methodName, [ value ])
        | _ -> None 
    let (| TwoArgs |_|) = function 
        | Lambda(_, Application (OneArg(methodName, args) , ProvidedValue(arg))) ->
            Some (methodName, [ yield! args; yield arg ]) 
        | _ -> None 
    let (| ThreeArgs |_|) = function 
        | Lambda(_, Application (TwoArgs(methodName, args) , ProvidedValue(arg))) ->
            Some (methodName, [ yield! args; yield arg ]) 
        | _ -> None 
    let (| FourArgs |_|) = function 
        | Lambda(_, Application (ThreeArgs(methodName, args) , ProvidedValue(arg))) ->
            Some (methodName, [ yield! args; yield arg ]) 
        | _ -> None 
    let (| FiveArgs |_|) = function 
        | Lambda(_, Application (FourArgs(methodName, args) , ProvidedValue(arg))) ->
            Some (methodName, [ yield! args; yield arg ]) 
        | _ -> None 

    let (| SixArgs |_|) = function 
        | Lambda(_, Application (FiveArgs(methodName, args) , ProvidedValue(arg))) ->
            Some (methodName, [ yield! args; yield arg ]) 
        | _ -> None 

    let (| SevenArgs |_|) = function 
        | Lambda(_, Application (SixArgs(methodName, args) , ProvidedValue(arg))) ->
            Some (methodName, [ yield! args; yield arg ]) 
        | _ -> None 
    let (| EightArgs |_|) = function 
        | Lambda(_, Application (SevenArgs(methodName, args) , ProvidedValue(arg))) ->
            Some (methodName, [ yield! args; yield arg ]) 
        | _ -> None 