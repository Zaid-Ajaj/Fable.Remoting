namespace Fable.Remoting.DotnetClient

open Quotations.Patterns
open Quotations.DerivedPatterns
open FSharp.Reflection
open System.ComponentModel

module Patterns =
    let (| AsyncField |_|) = function 
        | PropertyGet (Some (_), method, []) -> 
            Some(method.Name)
        | _ -> None 
    let (| NoArgs |_|) = function 
        | Lambda(_, AsyncField(methodName)) -> 
            Some (methodName, [ ])
        | _ -> None

    let rec (|UnionValue|_|) = function 
        | NewUnionCase(info, [ ]) -> 
            FSharpValue.MakeUnion(info, [|  |]) |> Some
        | NewUnionCase(info, [ ProvidedValue(value) ]) -> 
            FSharpValue.MakeUnion(info, [| value |]) |> Some
        | NewUnionCase(info, [ ProvidedValue(arg1);  ProvidedValue(arg2); ]) -> 
            FSharpValue.MakeUnion(info, [| arg1; arg2; |]) |> Some
        | NewUnionCase(info, [ ProvidedValue(arg1);  ProvidedValue(arg2);  ProvidedValue(arg3) ]) -> 
            FSharpValue.MakeUnion(info, [| arg1; arg2; arg3 |]) |> Some
        | NewUnionCase(info, [ ProvidedValue(arg1);  ProvidedValue(arg2);  ProvidedValue(arg3); ProvidedValue(arg4) ]) -> 
            FSharpValue.MakeUnion(info, [| arg1; arg2; arg3; arg4 |]) |> Some
        | NewUnionCase(info, [ ProvidedValue(arg1);  ProvidedValue(arg2);  ProvidedValue(arg3); ProvidedValue(arg4); ProvidedValue(arg5) ]) -> 
            FSharpValue.MakeUnion(info, [| arg1; arg2; arg3; arg4; arg4 |]) |> Some
        | _ -> None

    and (|RecordValue|_|) = function 
        | NewRecord(recordType, [ ProvidedValue(field) ]) -> 
            FSharpValue.MakeRecord(recordType, [| field |]) |> Some
        | NewRecord(recordType, [ ProvidedValue(arg1); ProvidedValue(arg2); ]) -> 
            FSharpValue.MakeRecord(recordType, [| arg1; arg2; |]) |> Some
        | NewRecord(recordType, [ ProvidedValue(arg1);  ProvidedValue(arg2);  ProvidedValue(arg3); ]) -> 
            FSharpValue.MakeRecord(recordType, [| arg1; arg2; arg3 |]) |> Some
        | NewRecord(recordType, [ ProvidedValue(arg1); ProvidedValue(arg2);  ProvidedValue(arg3); ProvidedValue(arg4) ]) -> 
            FSharpValue.MakeRecord(recordType, [| arg1; arg2; arg3; arg4 |]) |> Some
        | NewRecord(recordType, [ ProvidedValue(arg1);  ProvidedValue(arg2);  ProvidedValue(arg3); ProvidedValue(arg4); ProvidedValue(arg5) ]) -> 
            FSharpValue.MakeRecord(recordType, [| arg1; arg2; arg3; arg4; arg4 |]) |> Some
        | _ -> None

    and (|Tuples|_|) = function 
        | NewTuple [ProvidedValue(arg1); ProvidedValue(arg2)]  ->
            Some (box [arg1; arg2])
        | NewTuple [ProvidedValue(arg1); ProvidedValue(arg2); ProvidedValue(arg3)]  ->
            Some (box [arg1; arg2; arg3]) 
        | NewTuple [ProvidedValue(arg1); ProvidedValue(arg2); ProvidedValue(arg3); ProvidedValue(arg4)]  ->
            Some (box [arg1; arg2; arg3; arg4])
        | NewTuple [ProvidedValue(arg1); ProvidedValue(arg2); ProvidedValue(arg3); ProvidedValue(arg4); ProvidedValue(arg5)]  ->
            Some (box [arg1; arg2; arg3; arg4; arg5])
        | _ -> None
    and (| ProvidedValue |_|) = function 
        | Value(value, _ ) -> Some value 
        | ValueWithName(value, _, _) -> Some value
        | UnionValue value -> Some value
        | RecordValue value -> Some value
        | Tuples value -> Some value
        | _ -> None
    let (| OneArg |_|) = function 
        | Application (AsyncField(methodName), ProvidedValue(value)) ->
            Some (methodName, [ value ])
        | _ -> None 
    let (| TwoArgs |_|) = function 
        | Application (OneArg(methodName, args) , ProvidedValue(arg)) ->
            Some (methodName, [ yield! args; yield arg ]) 
        | _ -> None 
    let (| ThreeArgs |_|) = function 
        | Application (TwoArgs(methodName, args) , ProvidedValue(arg)) ->
            Some (methodName, [ yield! args; yield arg ]) 
        | _ -> None 
    let (| FourArgs |_|) = function 
        | Application (ThreeArgs(methodName, args) , ProvidedValue(arg)) ->
            Some (methodName, [ yield! args; yield arg ]) 
        | _ -> None 
    let (| FiveArgs |_|) = function 
        | Application (FourArgs(methodName, args) , ProvidedValue(arg)) ->
            Some (methodName, [ yield! args; yield arg ]) 
        | _ -> None 

    let (| SixArgs |_|) = function 
        | Application (FiveArgs(methodName, args) , ProvidedValue(arg)) ->
            Some (methodName, [ yield! args; yield arg ]) 
        | _ -> None 

    let (| SevenArgs |_|) = function 
        | Application (SixArgs(methodName, args) , ProvidedValue(arg)) ->
            Some (methodName, [ yield! args; yield arg ]) 
        | _ -> None 
    let (| EightArgs |_|) = function 
        | Application (SevenArgs(methodName, args) , ProvidedValue(arg)) ->
            Some (methodName, [ yield! args; yield arg ]) 
        | _ -> None 
    let (| OneArgLambda |_|) = function 
        | Lambda(_, OneArg(methodName, args)) ->
            Some (methodName, args)
        | _ -> None 
    let (| TwoArgLambda |_|) = function 
        | Lambda(_, TwoArgs(methodName, args)) ->
            Some (methodName, args)
        | _ -> None     
    let (| ThreeArgLambda |_|) = function 
        | Lambda(_, ThreeArgs(methodName, args)) ->
            Some (methodName, args)
        | _ -> None 
    let (| FourArgLambda |_|) = function 
        | Lambda(_, FourArgs(methodName, args)) ->
            Some (methodName, args)
        | _ -> None 
    let (| FiveArgLambda |_|) = function 
        | Lambda(_, FiveArgs(methodName, args)) ->
            Some (methodName, args)
        | _ -> None 
    let (| SixArgLambda |_|) = function 
        | Lambda(_, SixArgs(methodName, args)) ->
            Some (methodName, args)
        | _ -> None 

    let (| SevenArgLambda |_|) = function 
        | Lambda(_, SevenArgs(methodName, args)) ->
            Some (methodName, args)
        | _ -> None 
    let (| EightArgLambda |_|) = function 
        | Lambda(_, OneArg(methodName, args)) ->
            Some (methodName, args)
        | _ -> None 


    let (|ProxyLambda|_|) = function 
        | NoArgs (methodName, args) 
        | OneArgLambda (methodName, args)
        | TwoArgLambda (methodName, args)
        | ThreeArgLambda (methodName, args) 
        | FourArgLambda (methodName, args)
        | FiveArgLambda (methodName, args) 
        | SixArgLambda (methodName, args)
        | SevenArgLambda (methodName, args)
        | EightArgLambda (methodName, args) -> Some (methodName, args) 
        | _ -> None