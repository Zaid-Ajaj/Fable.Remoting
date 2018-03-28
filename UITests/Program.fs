// Learn more about F# at http://fsharp.org

open System
open Suave 
open Suave.Successful
open System.IO
open Fable.Remoting.Server
open Fable.Remoting.Suave
open OpenQA.Selenium
open OpenQA.Selenium.Firefox
open System.Threading
open Suave.Logging
open Suave.Operators
open Suave.Filters
open SharedTypes
open ServerImpl
open OpenQA.Selenium

let fableWebPart = remoting server {
    with_builder routeBuilder
    use_error_handler (fun ex routeInfo ->
      Propagate ex.Message)
    use_custom_handler_for "overriddenFunction" (fun _ -> ResponseOverride.Default.withBody "42" |> Some)
    use_custom_handler_for "customStatusCode" (fun _ -> ResponseOverride.Default.withStatusCode 204 |> Some)
}

let isVersion v (ctx:HttpContext) =
  if ctx.request.headers |> List.contains ("version",v) then
    None
  else
    Some {ResponseOverride.Default with Abort = true}

let versionTestWebPart =
  remoting versionTestServer {
    with_builder versionTestBuilder
    use_custom_handler_for "v4" (isVersion "4")
    use_custom_handler_for "v3" (isVersion "3")
    use_custom_handler_for "v2" (isVersion "2")
  }

let contextTestWebApp =
    remoting {callWithCtx = fun (ctx:HttpContext) -> async{return ctx.request.path}} {
        with_builder routeBuilder
    }


let (</>) x y = Path.Combine(x, y)

let rec findRoot dir =
    if File.Exists(System.IO.Path.Combine(dir, "paket.dependencies"))
    then dir
    else
        let parent = Directory.GetParent(dir)
        if isNull parent then
            failwith "Couldn't find root directory"
        findRoot parent.FullName

[<EntryPoint>]
let main argv =
    let cwd = Directory.GetCurrentDirectory()
    let root = findRoot cwd 

    let cts = new CancellationTokenSource() 
    let suaveConfig = 
        { defaultConfig with
            homeFolder = Some (root </> "Fable.Remoting.IntegrationTests" </> "client-dist")
            bindings   = [ HttpBinding.createSimple HTTP "127.0.0.1" 8080 ]
            bufferSize = 2048
            cancellationToken = cts.Token }

    let testWebApp = 
        choose [ 
            GET >=> Files.browseHome
            fableWebPart 
            versionTestWebPart
            contextTestWebApp 
            OK "Not Found"
        ]

    printfn "Starting Web server"
    let listening, server = startWebServerAsync suaveConfig testWebApp
    
    Async.Start server
    printfn "Web server started"

    printfn "Getting server ready to listen for reqeusts"
    listening
    |> Async.RunSynchronously
    |> ignore
    
    printfn "Server listening to requests"

    let driversDir = root </> "UITests" </> "drivers"
    let options = FirefoxOptions()
    options.AddArgument("--headless")
    
    printfn "Starting FireFox Driver"
    use driver = new FirefoxDriver(driversDir, options)
    driver.Url <- "http://localhost:8080/index.html"
    
    // give tests time to run
    Threading.Thread.Sleep(30 * 1000)

    //let testsContainer = driver.FindElementById("qunit-tests")
    //let testCases = testsContainer.FindElements(OpenQA.Selenium.By.TagName("li"))
    //
    //for testCase in testCases do
    //    printfn "%s" testCase.Text
    //    let name = testCase.FindElement(By.ClassName("test-name"))
    //    let counts = testCase.FindElement(By.ClassName("counts"))
    //    let assertContainer = testCase.FindElement(By.ClassName("qunit-assert-list"))
    //    let asserts = assertContainer.FindElements(By.TagName("li"))
    //    printfn "Fable.Remoting: %s (%s)" name.Text counts.Text
    //    printfn "  | "
    //    for assertCase in asserts do
    //        let passed = assertCase.GetAttribute("class") = "pass"
    //        if passed then
    //            let message = assertCase.FindElement(By.ClassName("test-message"))
    //            let runtime = assertCase.FindElement(By.ClassName("runtime"))
    //            printfn "  |-- %s (took about %s) -> Passed" message.Text runtime.Text
    //        else
    //            let message = assertCase.FindElement(By.ClassName("test-message"))
    //            let runtime = assertCase.FindElement(By.ClassName("runtime"))
    //            Console.ForegroundColor <- ConsoleColor.Red
    //            printfn "  | %s (took about %s) -> Failed" message.Text runtime.Text
    //            Console.ResetColor()
   
    let failed = driver.FindElementByClassName("failed")
    let success = driver.FindElementByClassName("passed")

    printfn ""
    printfn "Passed: %s" success.Text
    printfn "Failed: %s" failed.Text

    cts.Cancel()
    driver.Quit()

    try 
      let failedCount = int failed.Text
      if failed <> 0 then 1
      else 0
    with
    | e ->
        printfn "Error occured while parsing the number of failed tests"
        printfn "%s\n%s" e.Message e.StackTrace
        1 // return an integer exit code
