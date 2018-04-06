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
    use_route_builder routeBuilder
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
    use_route_builder versionTestBuilder
    use_custom_handler_for "v4" (isVersion "4")
    use_custom_handler_for "v3" (isVersion "3")
    use_custom_handler_for "v2" (isVersion "2")
  }

let contextTestWebApp =
    remoting {callWithCtx = fun (ctx:HttpContext) -> async{return ctx.request.path}} {
        use_route_builder routeBuilder
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
    
    let mutable testsFinishedRunning = false

    while not testsFinishedRunning do
      // give tests time to run
      printfn "Tests have not finished running yet"
      printfn "Waiting for another 10 seconds"
      Threading.Thread.Sleep(10 * 1000)
      try 
        driver.FindElementByClassName("failed") |> ignore
        testsFinishedRunning <- true
      with 
        | _ -> ()

    let passedTests = unbox<string> (driver.ExecuteScript("return JSON.stringify(passedTests, null, 4);"))
    let failedTests = unbox<string> (driver.ExecuteScript("return JSON.stringify(failedTests, null, 4);"))
    Console.ForegroundColor <- ConsoleColor.Green
    printfn "Tests Passed: \n%s" passedTests
    Console.ForegroundColor <- ConsoleColor.Red
    printfn "Tests Failed: \n%s" failedTests
    Console.ResetColor()
    let failed = driver.FindElementByClassName("failed")
    let success = driver.FindElementByClassName("passed")

    let failedText = failed.Text

    printfn ""
    printfn "Passed: %s" success.Text
    printfn "Failed: %s" failed.Text

    cts.Cancel()
    driver.Quit()

    try 
      let failedCount = int failedText
      if failedCount <> 0 then 1
      else 0
    with
    | e ->
        printfn "Error occured while parsing the number of failed tests"
        printfn "%s\n%s" e.Message e.StackTrace
        1 // return an integer exit code
