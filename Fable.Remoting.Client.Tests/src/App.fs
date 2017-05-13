module Fable.Remoting.Client.Tests

open Fable.Remoting.Client
open FSharp.Reflection
open Fable.Import


type TestRecord =  { getLength : string -> int;  echo : string -> string  }

type TestAsyncRec = { get : string -> Async<int> }

QUnit.Module("Fable.Remoting.Client Tests")

QUnit.Test("Primitive Fields are retrieved correctly", fun test ->
    let fields = Proxy.fields<TestRecord> 
    let fieldNames = fields |> Seq.map fst |> Array.ofSeq
    test.equal "getLength" fieldNames.[0]
    test.equal "echo" fieldNames.[1]

    let fieldTypes = fields |> Seq.map snd |> Array.ofSeq
    let getLengthTypes = fieldTypes.[0]
    let echoTypes = fieldTypes.[1]
    test.equal "string" getLengthTypes.[0]
    test.equal "number" getLengthTypes.[1]
    test.equal "string" echoTypes.[0]
    test.equal "string" echoTypes.[1]
)

QUnit.Test("Async returning fields are retrieved correctly", fun test ->
    let fields = Proxy.fields<TestAsyncRec> 
    // Async<int>
    let asyncInt = fields |> Seq.head |> snd |> fun xs -> xs.[1]
    let intType = asyncInt.GenericTypeArguments.[0]
    test.equal intType "number" 
)

type Maybe<'a> = 
    | Just of 'a
    | Nothing


QUnit.Test("Proxy.makeTypeArgument works", fun test ->
    let input = "5"
    let typeArg = Proxy.makeTypeArgument (typeof<int>)
    Proxy.dynamicOfJson(input, typeArg)
    |> unbox<int>
    |> fun n -> test.equal 5 n
)

QUnit.Test("Proxy.dynamicOfJson works with primitive types", fun test -> 
    let input = "5"
    let typeArg = Proxy.makeTypeArgument typeof<int>
    Proxy.dynamicOfJson(input, typeArg) 
    |> unbox<int>
    |> fun n ->  test.equal 5 n
)

QUnit.Test("Proxy.dynamicOfJson works with options (Some)", fun test -> 
    let input = "5"
    let typeArg = Proxy.makeTypeArgument typeof<Option<int>>
    Proxy.dynamicOfJson(input, typeArg)
    |> unbox<Option<int>>
    |> function
        | Some n -> test.equal 5 n
        | None -> test.equal true false
)


QUnit.Test("Proxy.dynamicOfJson works with options (None)", fun test -> 
    let input = "null"
    let typeArg = Proxy.makeTypeArgument typeof<Option<int>>
    Proxy.dynamicOfJson(input, typeArg)
    |> unbox<Option<int>>
    |> function
        | Some _ -> test.equal false true
        | None -> test.equal true true
)

QUnit.Test("Proxy.dynamicOfJson works with generic types", fun test -> 
    let input = "{\"Just\": 5}"
    let typeArg = Proxy.makeTypeArgument typeof<Maybe<int>>
    Proxy.dynamicOfJson(input, typeArg) 
    |> unbox<Maybe<int>>
    |> fun x ->
        match x with
        | Just n -> test.equal 5 n
        | Nothing -> test.equal true false
)