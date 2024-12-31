namespace Fable.Remoting.Server

open System
open System.Text

module Diagnostics =
    let writeLn (text: string) (builder: StringBuilder)  = builder.AppendLine(text)
    let toLogger logf = string >> logf

    /// Simplifes the name of the type that is to be deserialized
    let rec typePrinter (valueType: Type) =
        let simplifyGeneric = function
            | "Microsoft.FSharp.Core.FSharpOption" -> "Option"
            | "Microsoft.FSharp.Collections.FSharpList" -> "FSharpList"
            | "Microsoft.FSharp.Core.FSharpResult" -> "Result"
            | "Microsoft.FSharp.Collections.FSharpMap" -> "Map"
            | otherwise -> otherwise

        match valueType.FullName.Replace("+", ".") with
        | "System.String" -> "string"
        | "System.Boolean" -> "bool"
        | "System.Int32" -> "int"
        | "System.Double" -> "double"
        | "System.Numerics.BigInteger" -> "bigint"
        | "Microsoft.FSharp.Core.Unit" -> "unit"
        | other ->
            match valueType.GetGenericArguments() with
            | [|  |] -> other
            | genericTypeArguments ->
                let typeParts = other.Split('`')
                let typeName = typeParts.[0]
                Array.map typePrinter genericTypeArguments
                |> String.concat ", "
                |> sprintf "%s<%s>" (simplifyGeneric typeName)

    let runPhase logger text =
        logger |> Option.iter (fun logf ->
            StringBuilder()
            |> writeLn (sprintf "Fable.Remoting: invoking function %s" text)
            |> toLogger logf
        )

    /// Logs the JSON input and the corresponding types that the JSON will be converter into.
    let deserializationPhase logger (text: unit -> string) (inputTypes: System.Type[]) =
        logger |> Option.iter(fun logf ->
            StringBuilder()
            |> writeLn "Fable.Remoting:"
            |> writeLn "About to deserialize JSON:"
            |> writeLn (text())
            |> writeLn "Into .NET Types:"
            |> writeLn (sprintf "[%s]" (inputTypes |> Array.map typePrinter |> String.concat ", "))
            |> writeLn ""
            |> toLogger logf)

    /// Logs the serialized output from the server
    let outputPhase logger value =
        logger |> Option.iter(fun logf ->
            StringBuilder()
            |> writeLn "Fable.Remoting: Returning serialized result back to client"
            |> writeLn value
            |> toLogger logf)
