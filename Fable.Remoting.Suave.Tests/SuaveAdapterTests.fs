namespace Fable.Remoting.Suave.Tests

open Fable.Remoting.Suave
open System.Net.Http
open Suave.Testing
open Suave.Testing.Utilities
open Suave.Web
open Suave.Http
open Suave.Successful
open System
open NUnit.Framework


[<TestFixture>]
module SuaveAdapterTests = 

    let implementation = TestImplementation.implementation

    let app = FableSuaveAdapter.webPartFor implementation

    let postContent (input: string) =  new StringContent(input, System.Text.Encoding.UTF8)

    [<Test>]
    let ``Sending string as input works``() = 
        let input = "\"my-test-string\"";
        let content = postContent input
        runWith Suave.Web.defaultConfig app
        |> req HttpMethod.POST "/IProtocol/getLength" (Some content)
        |> fun result -> Assert.AreEqual(result, "14")

    [<Test>]
    let ``Sending int as input works``() =
        let input = postContent "5" 
        runWith Suave.Web.defaultConfig app
        |> req HttpMethod.POST "/IProtocol/echoInteger" (Some input)
        |> fun result -> Assert.AreEqual("10", result)

    
    [<Test>]
    let ``Sending some option as input works``() = 
        let someInput = postContent "5" // toJson (Some 5) => "5"
        let testApp = runWith Suave.Web.defaultConfig app
        testApp
        |> req HttpMethod.POST "/IProtocol/echoOption" (Some someInput)
        |> fun result -> Assert.AreEqual("10", result)

    [<Test>]
    let ``Sending none option as input works``() = 
        // the string "null" represents None
        // it's what fable sends from browser
        let noneInput = postContent "null" // toJson None => "null"
        let testApp = runWith Suave.Web.defaultConfig app
        
        testApp
        |> req HttpMethod.POST "/IProtocol/echoOption" (Some noneInput)
        |> fun result -> Assert.AreEqual("0", result)

    [<Test>]
    let ``Sending DateTime as input works``() = 
        let someInput = postContent "\"2017-05-12T14:20:00.000Z\""
        let testApp = runWith Suave.Web.defaultConfig app
        testApp
        |> req HttpMethod.POST "/IProtocol/echoMonth" (Some someInput)
        |> fun result -> Assert.AreEqual("5", result)

    [<Test>]
    let ``Sending and recieving strings works``() = 
        let someInput = postContent "\"my-string\""
        let testApp = runWith Suave.Web.defaultConfig app
        testApp
        |> req HttpMethod.POST "/IProtocol/echoString" (Some someInput)
        |> fun result -> Assert.AreEqual("\"my-string\"", result)      
        
     
    [<Test>]
    let ``Recieving int option to None output works``() = 
        let someInput = postContent "\"\""
        let testApp = runWith Suave.Web.defaultConfig app
        testApp
        |> req HttpMethod.POST "/IProtocol/optionOutput" (Some someInput)
        |> fun result -> Assert.AreEqual("null", result)  
        
        
    [<Test>]
    let ``Recieving int option to Some output works``() = 
        let someInput = postContent "\"non-empty\""
        let testApp = runWith Suave.Web.defaultConfig app
        testApp
        |> req HttpMethod.POST "/IProtocol/optionOutput" (Some someInput)
        |> fun result -> Assert.AreEqual("5", result)     
        
        
    [<Test>]
    let ``Sending generic union case Nothing as input works``() = 
        let someInput = postContent "\"Nothing\""
        let testApp = runWith Suave.Web.defaultConfig app
        testApp
        |> req HttpMethod.POST "/IProtocol/genericUnionInput" (Some someInput)
        |> fun result -> Assert.AreEqual("0", result)      
        
    [<Test>]
    let ``Sending generic union case Just as input works``() = 
        let someInput = postContent "{\"Just\":5}"
        let testApp = runWith Suave.Web.defaultConfig app
        testApp
        |> req HttpMethod.POST "/IProtocol/genericUnionInput" (Some someInput)
        |> fun result -> Assert.AreEqual("5", result)  
        
    [<Test>]
    let ``Recieving generic union case Just 5 as output works``() = 
        let someInput = postContent "true"
        let testApp = runWith Suave.Web.defaultConfig app
        testApp
        |> req HttpMethod.POST "/IProtocol/genericUnionOutput" (Some someInput)
        |> fun result -> Assert.AreEqual("{\"Just\":5}", result)    

    [<Test>]
    let ``Recieving generic union case Nothing as output works``() = 
        let someInput = postContent "false"
        let testApp = runWith Suave.Web.defaultConfig app
        testApp
        |> req HttpMethod.POST "/IProtocol/genericUnionOutput" (Some someInput)
        |> fun result -> Assert.AreEqual("\"Nothing\"", result)    


    [<Test>]
    let ``Recieving and sending simple union works``() = 
        let someInput = postContent "\"A\""
        let testApp = runWith Suave.Web.defaultConfig app
        testApp
        |> req HttpMethod.POST "/IProtocol/simpleUnionInputOutput" (Some someInput)
        |> fun result -> Assert.AreEqual("\"B\"", result)  
        
    [<Test>]
    let ``Recieving and sending records works``() = 
        // In Fable, toJson { Prop1 = ""; Prop2 = 5; Prop3 = None }
        // becomes
        let recordInput = postContent "{\"Prop1\":\"\",\"Prop2\":5,\"Prop3\":null}"
        let testApp = runWith Suave.Web.defaultConfig app
        testApp
        |> req HttpMethod.POST "/IProtocol/recordEcho" (Some recordInput)
        |> fun result -> Assert.AreEqual("{\"Prop1\":\"\",\"Prop2\":15,\"Prop3\":null}", result) 

    [<Test>]
    let ``Sending list of ints works``() = 
        let someInput = postContent "[1,2,3,4,5,6,7,8,9,10]"
        let testApp = runWith Suave.Web.defaultConfig app
        testApp
        |> req HttpMethod.POST "/IProtocol/listIntegers" (Some someInput)
        |> fun result -> Assert.AreEqual("55", result)  

    [<Test>]
    let ``Inoking function of unit works``() = 
        // server will ignore the input
        let someInput = postContent ""
        let testApp = runWith Suave.Web.defaultConfig app
        testApp
        |> req HttpMethod.POST "/IProtocol/unitToInts" (Some someInput)
        |> fun result -> Assert.AreEqual("55", result)  