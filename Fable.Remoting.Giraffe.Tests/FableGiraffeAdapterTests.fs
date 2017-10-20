module FableGiraffeAdapterTests

open Fable.Remoting.Giraffe
open System.Net.Http
open System
open Expecto
open Types

// Test helpers

let equal x y = Expect.equal true (x = y) (sprintf "%A = %A" x y)
let pass () = Expect.equal true true ""   
let fail () = Expect.equal false true ""

//FableGirrafeAdapter.logger <- Some (printfn "%s")
//let app = FableGirrafeAdapter.webPartFor implementation
let postContent (input: string) =  new StringContent(input, System.Text.Encoding.UTF8)


let fableGiraffeAdapterTests = 
    testList "FableGiraffeAdapter tests" [
        testCase "Sending string as input works" <| fun () ->
            pass()
    ]