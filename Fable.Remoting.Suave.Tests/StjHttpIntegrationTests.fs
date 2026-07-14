module StjHttpIntegrationTests

// HTTP integration tests for the System.Text.Json opt-in path through Suave.
// Spins up Suave servers wired with `Remoting.withSerializerOptions
// (FableConverters.create())` and round-trips representative shapes through
// the full Server → wire → Client cycle. End-to-end proof the STJ opt-in
// works on Suave the same way it works on Giraffe (verified in Phase 4b).
//
// The existing Newtonsoft-default tests in FableSuaveAdapterTests.fs
// continue to pass unchanged (no behaviour change without opting in).

open System
open System.Text
open System.Text.Json
open System.Net.Http
open Suave
open Suave.Http
open Fable.Remoting.Server
open Fable.Remoting.Suave
open Fable.Remoting.Json.SystemTextJson
open SuaveTester
open Expecto
open Types

let private stjOptions = FableConverters.create ()

let private stjApp =
    Remoting.createApi()
    |> Remoting.fromValue implementation
    |> Remoting.withSerializerOptions stjOptions
    |> Remoting.buildWebPart

let private postContent (input: string) =
    new StringContent(sprintf "[%s]" input, Encoding.UTF8)

let private toJson (x: 'a) = JsonSerializer.Serialize<'a>(x, stjOptions)
let private ofJson<'a> (s: string) = JsonSerializer.Deserialize<'a>(s, stjOptions)

let private getConfig =
    let mutable port = 9024
    fun () ->
        { Suave.Web.defaultConfig
            with bindings = [ HttpBinding.createSimple HTTP "127.0.0.1" (System.Threading.Interlocked.Increment &port) ] }

let stjSuaveIntegrationTests =
    testList "Phase 4d — STJ HTTP integration (Suave)" [
        testCase "Int round-trip via STJ" <| fun () ->
            let cfg = getConfig ()
            runWith cfg stjApp
            |> req POST "/IProtocol/echoInteger" (Some (postContent (toJson 21)))
            |> fun result ->
                let value = ofJson<int> result
                Expect.equal value 42 "echoInteger doubles the input"

        testCase "String round-trip via STJ" <| fun () ->
            let cfg = getConfig ()
            runWith cfg stjApp
            |> req POST "/IProtocol/echoString" (Some (postContent (toJson "hello")))
            |> fun result ->
                let value = ofJson<string> result
                Expect.equal value "hello" "string echoes through STJ"

        testCase "Option<int> Some round-trip via STJ" <| fun () ->
            let cfg = getConfig ()
            runWith cfg stjApp
            |> req POST "/IProtocol/echoOption" (Some (postContent (toJson (Some 5))))
            |> fun result ->
                let value = ofJson<int> result
                Expect.equal value 10 "Some 5 round-trips and gets doubled"

        testCase "Option<int> None round-trip via STJ" <| fun () ->
            let cfg = getConfig ()
            runWith cfg stjApp
            |> req POST "/IProtocol/echoOption" (Some (postContent (toJson (None : int option))))
            |> fun result ->
                let value = ofJson<int> result
                Expect.equal value 0 "None round-trips as 0 (per implementation)"

        testCase "Record with None field round-trip via STJ" <| fun () ->
            let cfg = getConfig ()
            let input : Record = { Prop1 = "x"; Prop2 = 5; Prop3 = None }
            runWith cfg stjApp
            |> req POST "/IProtocol/recordEcho" (Some (postContent (toJson input)))
            |> fun result ->
                let value = ofJson<Record> result
                Expect.equal value { Prop1 = "x"; Prop2 = 15; Prop3 = None } "record echoes with Prop2 + 10"

        testCase "DU Maybe<int> Just round-trip via STJ" <| fun () ->
            let cfg = getConfig ()
            runWith cfg stjApp
            |> req POST "/IProtocol/genericUnionInput" (Some (postContent (toJson (Just 42))))
            |> fun result ->
                let value = ofJson<int> result
                Expect.equal value 42 "Just 42 round-trips"

        testCase "DU Maybe<int> Nothing round-trip via STJ" <| fun () ->
            let cfg = getConfig ()
            runWith cfg stjApp
            |> req POST "/IProtocol/genericUnionInput" (Some (postContent (toJson (Nothing : Maybe<int>))))
            |> fun result ->
                let value = ofJson<int> result
                Expect.equal value 0 "Nothing round-trips as 0"

        testCase "Simple DU AB round-trip via STJ" <| fun () ->
            let cfg = getConfig ()
            runWith cfg stjApp
            |> req POST "/IProtocol/simpleUnionInputOutput" (Some (postContent (toJson A)))
            |> fun result ->
                let value = ofJson<AB> result
                Expect.equal value B "A → B per implementation"

        testCase "int list round-trip via STJ" <| fun () ->
            let cfg = getConfig ()
            runWith cfg stjApp
            |> req POST "/IProtocol/listIntegers" (Some (postContent (toJson [1; 2; 3; 4; 5])))
            |> fun result ->
                let value = ofJson<int> result
                Expect.equal value 15 "list sum round-trips"

        testCase "Map<string,int> round-trip via STJ" <| fun () ->
            let cfg = getConfig ()
            let input = Map.ofList ["a", 1; "b", 2; "c", 3]
            runWith cfg stjApp
            |> req POST "/IProtocol/echoMap" (Some (postContent (toJson input)))
            |> fun result ->
                let value = ofJson<Map<string, int>> result
                Expect.equal value input "Map<string,int> echoes through STJ"

        testCase "bigint list round-trip via STJ" <| fun () ->
            let cfg = getConfig ()
            let inputs = [1I; 2I; 3I]
            runWith cfg stjApp
            |> req POST "/IProtocol/echoBigInteger" (Some (postContent (toJson inputs)))
            |> fun result ->
                let value = ofJson<bigint> result
                Expect.equal value 6I "bigint sum round-trips"

        testCase "Result<int,string> Ok round-trip via STJ" <| fun () ->
            let cfg = getConfig ()
            runWith cfg stjApp
            |> req POST "/IProtocol/echoResult" (Some (postContent (toJson (Ok 42 : Result<int, string>))))
            |> fun result ->
                let value = ofJson<Result<int, string>> result
                Expect.equal value (Ok 42) "Ok 42 echoes through STJ"

        testCase "Result<int,string> Error round-trip via STJ" <| fun () ->
            let cfg = getConfig ()
            runWith cfg stjApp
            |> req POST "/IProtocol/echoResult" (Some (postContent (toJson (Error "fail" : Result<int, string>))))
            |> fun result ->
                let value = ofJson<Result<int, string>> result
                Expect.equal value (Error "fail") "Error \"fail\" echoes through STJ"
    ]
