module FableClientTests

open FSharp.Core
open Fable.Import

QUnit.registerModule "FableClient Tests"

QUnit.test "Square works" <| fun test ->
    let square x = x * x
    test.equal (square 3) 9

