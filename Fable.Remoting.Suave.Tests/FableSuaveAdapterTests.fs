module FableSuaveAdapterTests

open Fable.Remoting.Server
open Fable.Remoting.Suave
open Fable.Remoting.Json

open Newtonsoft.Json

open System.Net.Http
open SuaveTester
open Suave.Http
open System
open Expecto
open Types

// Test helpers

let equal x y = Expect.equal true (x = y) (sprintf "%A = %A" x y)
let pass () = Expect.equal true true ""   
let fail () = Expect.equal false true ""

let errorHandler (ex: exn) (_: RouteInfo) = 
    printfn "Propagating exception message back to client: %s" ex.Message
    Propagate (ex.Message)

let app = remoting implementation { 
    use_error_handler errorHandler
    use_logger (printfn "%s") 
}

let postContent (input: string) =  new StringContent(sprintf "[%s]" input, System.Text.Encoding.UTF8)
let postRaw (input: string) =  new StringContent(input, System.Text.Encoding.UTF8)

let converter : JsonConverter = FableJsonConverter() :> JsonConverter
let toJson (x: obj) = JsonConvert.SerializeObject(x, [| converter |])

let ofJson<'t> (input: string) = JsonConvert.DeserializeObject<'t>(input, [| converter |])

let getConfig =
    let mutable port = 1024
    fun () ->       
        { Suave.Web.defaultConfig 
            with bindings = [ HttpBinding.createSimple HTTP "127.0.0.1" (System.Threading.Interlocked.Increment &port) ] }
            
let fableSuaveAdapterTests = 
    testList "FableSuaveAdapter tests" [
        testCase "Sending string as input works" <| fun () ->
            let defaultConfig = getConfig ()
            let input = "\"my-test-string\"";
            let content = postContent input
            runWith defaultConfig app
            |> req POST "/IProtocol/getLength" (Some content)
            |> fun result -> equal result "14"

        testCase "Sending int as input works" <| fun () ->
            let defaultConfig = getConfig ()
            let input = postContent "5" 
            runWith defaultConfig app
            |> req POST "/IProtocol/echoInteger" (Some input)
            |> fun result -> equal "10" result

    
        testCase "Sending some option as input works" <| fun () ->
            let defaultConfig = getConfig ()
            let someInput = postContent "5" // toJson (Some 5) => "5"
            let testApp = runWith defaultConfig app
            testApp
            |> req POST "/IProtocol/echoOption" (Some someInput)
            |> fun result -> equal "10" result

        testCase "Sending none option as input works" <| fun () ->
            // the string "null" represents None
            // it's what fable sends from browser
            let defaultConfig = getConfig ()
            let noneInput = postContent "null" // toJson None => "null"
            let testApp = runWith defaultConfig app
            
            testApp
            |> req POST "/IProtocol/echoOption" (Some noneInput)
            |> fun result -> equal "0" result

        
        testCase "Sending DateTime as input works" <| fun () -> 
            let defaultConfig = getConfig ()
            let someInput = postContent "\"2017-05-12T14:20:00.000Z\""
            let testApp = runWith defaultConfig app
            testApp
            |> req POST "/IProtocol/echoMonth" (Some someInput)
            |> equal "5"

        testCase "Sending Result<int, string> roundtrip works with Ok" <| fun _ ->
            let defaultConfig = getConfig ()
            let input = postContent (toJson (Ok 15))
            runWith defaultConfig app
            |> req POST "/IProtocol/echoResult" (Some input)
            |> ofJson<Result<int, string>>
            |> function 
                | Ok 15 -> pass()
                | otherwise -> fail()
        
        testCase "Thrown error is catched and returned" <| fun _ -> 
            let defaultConfig = getConfig ()
            let input = postContent ""
            runWith defaultConfig app
            |> req POST "/IProtocol/throwError" (Some input)
            |> ofJson<CustomErrorResult<string>>
            |> equal { error = "I am thrown from adapter function";
                       handled = true;
                       ignored = false }

        testCase "Sending Result<int, string> roundtrip works with Error" <| fun _ ->
            let defaultConfig = getConfig ()
            let input = postContent (toJson (Error "hello"))
            runWith defaultConfig app
            |> req POST "/IProtocol/echoResult" (Some input)
            |> ofJson<Result<int, string>>
            |> function 
                | Error "hello" -> pass()
                | otherwise -> fail()
            
        testCase "Sending BigInteger roundtrip works" <| fun _ ->
            let defaultConfig = getConfig ()
            let input = postContent (toJson [1I .. 5I])
            runWith defaultConfig app
            |> req POST "/IProtocol/echoBigInteger" (Some input)
            |> ofJson<bigint>
            |> function 
                | sum when sum = 15I -> pass()
                | otherwise -> fail()

        testCase "Sending Map<string, int> roundtrips works" <| fun _ ->
            let defaultConfig = getConfig ()
            let inputMap = ["one",1; "two",2] |> Map.ofList
            let input = postContent (toJson inputMap)
            runWith defaultConfig app
            |> req POST "/IProtocol/echoMap" (Some input)
            |> ofJson<Map<string, int>>
            |> Map.toList
            |> function 
                | ["one",1; "two",2] -> pass()
                | otherwise -> fail() 

        testCase "Sending and recieving strings works" <| fun () -> 
            let defaultConfig = getConfig ()
            let someInput = postContent "\"my-string\""
            let testApp = runWith defaultConfig app
            testApp
            |> req POST "/IProtocol/echoString" (Some someInput)
            |> equal "\"my-string\""     
            
        testCase "Recieving int option to None output works" <| fun () -> 
            let defaultConfig = getConfig ()
            let someInput = postContent "\"\""
            let testApp = runWith defaultConfig app
            testApp
            |> req POST "/IProtocol/optionOutput" (Some someInput)
            |> equal "null" 
            
        testCase "Recieving int option to Some output works" <| fun () -> 
            let defaultConfig = getConfig ()
            let someInput = postContent "\"non-empty\""
            let testApp = runWith defaultConfig app
            testApp
            |> req POST "/IProtocol/optionOutput" (Some someInput)
            |> equal "5"
            
        testCase "Sending generic union case Nothing as input works" <| fun () ->
            let defaultConfig = getConfig () 
            let someInput = postContent "\"Nothing\""
            let testApp = runWith defaultConfig app
            testApp
            |> req POST "/IProtocol/genericUnionInput" (Some someInput)
            |> equal "0"      
            
        testCase "Sending generic union case Just as input works" <| fun () -> 
            let defaultConfig = getConfig ()
            let someInput = postContent "{\"Just\":5}"
            let testApp = runWith defaultConfig app
            testApp
            |> req POST "/IProtocol/genericUnionInput" (Some someInput)
            |> equal "5" 
            
        
        testCase "Recieving generic union case Just 5 as output works" <| fun () -> 
            let defaultConfig = getConfig ()
            let someInput = postContent "true"
            let testApp = runWith defaultConfig app
            testApp
            |> req POST "/IProtocol/genericUnionOutput" (Some someInput)
            |> equal "{\"Just\":5}"

        
        testCase "Recieving generic union case Nothing as output works" <| fun () ->
            let defaultConfig = getConfig () 
            let someInput = postContent "false"
            let testApp = runWith defaultConfig app
            testApp
            |> req POST "/IProtocol/genericUnionOutput" (Some someInput)
            |> equal "\"Nothing\""

        testCase "Recieving and sending simple union works" <| fun () -> 
            let defaultConfig = getConfig ()
            let someInput = postContent "\"A\""
            let testApp = runWith defaultConfig app
            testApp
            |> req POST "/IProtocol/simpleUnionInputOutput" (Some someInput)
            |> equal "\"B\""
            
        testCase "Recieving and sending records works" <| fun () -> 
            let defaultConfig = getConfig ()
            // In Fable, toJson { Prop1 = ""; Prop2 = 5; Prop3 = None }
            // becomes
            let recordInput = postContent "{\"Prop1\":\"\",\"Prop2\":5,\"Prop3\":null}"
            let testApp = runWith defaultConfig app
            testApp
            |> req POST "/IProtocol/recordEcho" (Some recordInput)
            |> equal "{\"Prop1\":\"\",\"Prop2\":15,\"Prop3\":null}" 

        testCase "Sending list of ints works" <| fun () -> 
            let defaultConfig = getConfig ()
            let someInput = postContent "[1,2,3,4,5,6,7,8,9,10]"
            let testApp = runWith defaultConfig app
            testApp
            |> req POST "/IProtocol/listIntegers" (Some someInput)
            |> equal "55" 

        testCase "Inoking function of unit works" <| fun () -> 
            let defaultConfig = getConfig ()
            // server will ignore the input
            let someInput = postContent ""
            let testApp = runWith defaultConfig app
            testApp
            |> req POST "/IProtocol/unitToInts" (Some someInput)
            |> equal "55" 

        testCase "Invoking list of records works" <| fun () ->
            let defaultConfig = getConfig ()
            let someInput = postContent "[{\"Prop1\":\"\",\"Prop2\":15,\"Prop3\":null}, {\"Prop1\":\"\",\"Prop2\":10,\"Prop3\":null}]"
            let testApp = runWith defaultConfig app
            testApp
            |> req POST "/IProtocol/recordListToInt" (Some someInput)
            |> equal "25" 

        testCase "Invoking a list of float works" <| fun () -> 
            let defaultConfig = getConfig ()
            let someInput = postContent "[1.20, 1.40, 1.60]"
            let testApp = runWith defaultConfig app
            testApp
            |> req POST "/IProtocol/floatList" (Some someInput)
            |> equal "4.2"

        testCase "Invoking with two arguments works" <| fun () -> 
            let defaultConfig = getConfig ()
            let someInput = postRaw "[13, 17]"
            let testApp = runWith defaultConfig app
            testApp
            |> req POST "/IProtocol/multipleSum" (Some someInput)
            |> equal "30"

        testCase "Invoking with lots of arguments works" <| fun () -> 
            let defaultConfig = getConfig ()
            let someInput = postRaw "[\"Test\", 17, 5.0]"
            let testApp = runWith defaultConfig app
            testApp
            |> req POST "/IProtocol/lotsOfArgs" (Some someInput)
            |> equal "\"string: Test; int: 17; float: 5.000000\""

            
    ]
