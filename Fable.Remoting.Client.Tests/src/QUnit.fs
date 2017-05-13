module QUnit 

open Fable.Core
open Fable.Core.JsInterop


type Assert = 
    abstract equal : obj -> obj -> unit

[<Emit("QUnit.module($0)")>]
let Module (name: string) : unit = jsNative


[<Emit("QUnit.test($0, $1)")>]
let Test (description: string, x : Assert -> unit) : unit = jsNative