module FableSuaveAdapterTests

open Fable.Remoting.Suave
open Fable.Remoting.Json

open Newtonsoft.Json

open System.Net.Http
open SuaveTester
open Suave.Web
open Suave.Http
open Suave.Successful
open System
open Expecto
open Types
open Expecto.CSharp.Runner
open Mono.Cecil

// Test helpers

let equal x y = Expect.equal true (x = y) (sprintf "%A = %A" x y)
let pass () = Expect.equal true true ""   
let fail () = Expect.equal false true ""

FableSuaveAdapter.logger <- Some (printfn "%s")
let app = FableSuaveAdapter.webPartFor implementation
let postContent (input: string) =  new StringContent(input, System.Text.Encoding.UTF8)

let converter : JsonConverter = FableJsonConverter() :> JsonConverter
let toJson (x: obj) = JsonConvert.SerializeObject(x, [| converter |])

let ofJson<'t> (input: string) = JsonConvert.DeserializeObject<'t>(input, [| converter |])

let getConfig port = 
    { Suave.Web.defaultConfig 
        with bindings = [ HttpBinding.createSimple HTTP "127.0.0.1" port ] }
let random = System.Random()
let fableSuaveAdapterTests = 
    testList "FableSuaveAdapter tests" [
        testCase "Sending string as input works" <| fun () ->
            let defaultConfig = getConfig (random.Next(1000, 9999))
            let input = "\"my-test-string\"";
            let content = postContent input
            runWith defaultConfig app
            |> req HttpMethod.POST "/IProtocol/getLength" (Some content)
            |> fun result -> equal result "14"

        testCase "Sending int as input works" <| fun () ->
            let defaultConfig = getConfig (random.Next(1000, 9999))
            let input = postContent "5" 
            runWith defaultConfig app
            |> req HttpMethod.POST "/IProtocol/echoInteger" (Some input)
            |> fun result -> equal "10" result

    
        testCase "Sending some option as input works" <| fun () ->
            let defaultConfig = getConfig (random.Next(1000, 9999))
            let someInput = postContent "5" // toJson (Some 5) => "5"
            let testApp = runWith defaultConfig app
            testApp
            |> req HttpMethod.POST "/IProtocol/echoOption" (Some someInput)
            |> fun result -> equal "10" result

        testCase "Sending none option as input works" <| fun () ->
            // the string "null" represents None
            // it's what fable sends from browser
            let defaultConfig = getConfig (random.Next(1000, 9999))
            let noneInput = postContent "null" // toJson None => "null"
            let testApp = runWith defaultConfig app
            
            testApp
            |> req HttpMethod.POST "/IProtocol/echoOption" (Some noneInput)
            |> fun result -> equal "0" result

        
        testCase "Sending DateTime as input works" <| fun () -> 
            let defaultConfig = getConfig (random.Next(1000, 9999))
            let someInput = postContent "\"2017-05-12T14:20:00.000Z\""
            let testApp = runWith defaultConfig app
            testApp
            |> req HttpMethod.POST "/IProtocol/echoMonth" (Some someInput)
            |> equal "5"

        testCase "Sending Result<int, string> roundtrip works with Ok" <| fun _ ->
            let defaultConfig = getConfig (random.Next(1000, 9999))
            let input = postContent (toJson (Ok 15))
            runWith defaultConfig app
            |> req HttpMethod.POST "/IProtocol/echoResult" (Some input)
            |> ofJson<Result<int, string>>
            |> function 
                | Ok 15 -> pass()
                | otherwise -> fail()


        testCase "Sending Result<int, string> roundtrip works with Error" <| fun _ ->
            let defaultConfig = getConfig (random.Next(1000, 9999))
            let input = postContent (toJson (Error "hello"))
            runWith defaultConfig app
            |> req POST "/IProtocol/echoResult" (Some input)
            |> ofJson<Result<int, string>>
            |> function 
                | Error "hello" -> pass()
                | otherwise -> fail()
            
        testCase "Sending BigInteger roundtrip works" <| fun _ ->
            let defaultConfig = getConfig (random.Next(1000, 9999))
            let input = postContent (toJson [1I .. 5I])
            runWith defaultConfig app
            |> req POST "/IProtocol/echoBigInteger" (Some input)
            |> ofJson<bigint>
            |> function 
                | sum when sum = 15I -> pass()
                | otherwise -> fail()

        testCase "Sending and recieving strings works" <| fun () -> 
            let defaultConfig = getConfig (random.Next(1000, 9999))
            let someInput = postContent "\"my-string\""
            let testApp = runWith defaultConfig app
            testApp
            |> req POST "/IProtocol/echoString" (Some someInput)
            |> equal "\"my-string\""     
            
        testCase "Recieving int option to None output works" <| fun () -> 
            let defaultConfig = getConfig (random.Next(1000, 9999))
            let someInput = postContent "\"\""
            let testApp = runWith defaultConfig app
            testApp
            |> req POST "/IProtocol/optionOutput" (Some someInput)
            |> equal "null" 
            
        testCase "Recieving int option to Some output works" <| fun () -> 
            let defaultConfig = getConfig (random.Next(1000, 9999))
            let someInput = postContent "\"non-empty\""
            let testApp = runWith defaultConfig app
            testApp
            |> req POST "/IProtocol/optionOutput" (Some someInput)
            |> equal "5"
            
        testCase "Sending generic union case Nothing as input works" <| fun () ->
            let defaultConfig = getConfig (random.Next(1000, 9999)) 
            let someInput = postContent "\"Nothing\""
            let testApp = runWith defaultConfig app
            testApp
            |> req POST "/IProtocol/genericUnionInput" (Some someInput)
            |> equal "0"      
            
        testCase "Sending generic union case Just as input works" <| fun () -> 
            let defaultConfig = getConfig (random.Next(1000, 9999))
            let someInput = postContent "{\"Just\":5}"
            let testApp = runWith defaultConfig app
            testApp
            |> req HttpMethod.POST "/IProtocol/genericUnionInput" (Some someInput)
            |> equal "5" 
            
        
        testCase "Recieving generic union case Just 5 as output works" <| fun () -> 
            let defaultConfig = getConfig (random.Next(1000, 9999))
            let someInput = postContent "true"
            let testApp = runWith defaultConfig app
            testApp
            |> req HttpMethod.POST "/IProtocol/genericUnionOutput" (Some someInput)
            |> equal "{\"Just\":5}"

        
        testCase "Recieving generic union case Nothing as output works" <| fun () ->
            let defaultConfig = getConfig (random.Next(1000, 9999)) 
            let someInput = postContent "false"
            let testApp = runWith defaultConfig app
            testApp
            |> req HttpMethod.POST "/IProtocol/genericUnionOutput" (Some someInput)
            |> equal "\"Nothing\""

        testCase "Recieving and sending simple union works" <| fun () -> 
            let defaultConfig = getConfig (random.Next(1000, 9999))
            let someInput = postContent "\"A\""
            let testApp = runWith defaultConfig app
            testApp
            |> req HttpMethod.POST "/IProtocol/simpleUnionInputOutput" (Some someInput)
            |> equal "\"B\""
            
        testCase "Recieving and sending records works" <| fun () -> 
            let defaultConfig = getConfig (random.Next(1000, 9999))
            // In Fable, toJson { Prop1 = ""; Prop2 = 5; Prop3 = None }
            // becomes
            let recordInput = postContent "{\"Prop1\":\"\",\"Prop2\":5,\"Prop3\":null}"
            let testApp = runWith defaultConfig app
            testApp
            |> req HttpMethod.POST "/IProtocol/recordEcho" (Some recordInput)
            |> equal "{\"Prop1\":\"\",\"Prop2\":15,\"Prop3\":null}" 

        testCase "Sending list of ints works" <| fun () -> 
            let defaultConfig = getConfig (random.Next(1000, 9999))
            let someInput = postContent "[1,2,3,4,5,6,7,8,9,10]"
            let testApp = runWith defaultConfig app
            testApp
            |> req HttpMethod.POST "/IProtocol/listIntegers" (Some someInput)
            |> equal "55" 

        testCase "Inoking function of unit works" <| fun () -> 
            let defaultConfig = getConfig (random.Next(1000, 9999))
            // server will ignore the input
            let someInput = postContent ""
            let testApp = runWith defaultConfig app
            testApp
            |> req HttpMethod.POST "/IProtocol/unitToInts" (Some someInput)
            |> equal "55" 

        testCase "Invoking list of records works" <| fun () ->
            let defaultConfig = getConfig (random.Next(1000, 9999))
            let someInput = postContent "[{\"Prop1\":\"\",\"Prop2\":15,\"Prop3\":null}, {\"Prop1\":\"\",\"Prop2\":10,\"Prop3\":null}]"
            let testApp = runWith defaultConfig app
            testApp
            |> req HttpMethod.POST "/IProtocol/recordListToInt" (Some someInput)
            |> equal "25" 

        testCase "Invoking a list of float works" <| fun () -> 
            let defaultConfig = getConfig (random.Next(1000, 9999))
            let someInput = postContent "[1.20, 1.40, 1.60]"
            let testApp = runWith defaultConfig app
            testApp
            |> req HttpMethod.POST "/IProtocol/floatList" (Some someInput)
            |> equal "4.2"
    ]