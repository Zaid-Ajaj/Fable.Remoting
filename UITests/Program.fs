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
open OpenQA.Selenium.Chrome

let fableWebPart = 
    Remoting.createApi()
    |> Remoting.fromContext (fun ctx -> server)
    |> Remoting.withRouteBuilder routeBuilder
    |> Remoting.withErrorHandler (fun ex routeInfo -> Propagate ex.Message) 
    |> Remoting.buildWebPart
    
let (</>) x y = Path.Combine(x, y)

let rec findRoot dir =
    if File.Exists(System.IO.Path.Combine(dir, "paket.dependencies"))
    then dir
    else
        let parent = Directory.GetParent(dir)
        if isNull parent then
            failwith "Couldn't find root directory"
        findRoot parent.FullName

module AuthServer = 

    // acquire the access token from here, returns an integer
    let token = pathScan "/IAuthServer/token/%d" (sprintf "%d" >> OK)

    // WebPart to ensure that there is a non-empty authorization header
    let requireAuthorized : WebPart = 
        fun (ctx: HttpContext) -> 
            async {
                return ctx.request.headers 
                       |> List.tryFind (fun (key, value) -> key = "authorization" && value <> "")
                       |> function 
                        | Some header -> Some ctx 
                        | None -> None       
            }  

    // the actual secure api, cannot be reached unless an authorization header is present
    let authorizedServerApi = 
        Remoting.createApi()
        |> Remoting.fromContext (fun ctx -> 
            {
                // return the authorization header
                getSecureValue = fun () ->
                    async {
                        return ctx.request.headers
                               |> List.tryFind (fun (key, value) -> key = "authorization" && value <> "")
                               |> Option.map (snd >> int) 
                               |> function 
                                  | Some value -> value
                                  | None -> -1
                    } 
            }) 
        |> Remoting.withRouteBuilder routeBuilder
        |> Remoting.buildWebPart


    let api = 
        choose [ 
            // web part to acquire the token
            token
            // protect authorized server api
            requireAuthorized >=> authorizedServerApi 
        ]

    
module CookieTest =
    open Suave.Cookie
    let cookieName = "httpOnly-test-cookie"

    let setCookie ctx =
        let cookie = {
            name = cookieName
            value = "test value"
            expires = None
            path = Some "/"
            domain = None
            secure = false
            httpOnly = true
            sameSite = None
        }
        Cookie.setCookie cookie ctx

    let cookieWebPart =
        let cookieServer (ctx:HttpContext) : ICookieServer =
            cookieServer <| fun _ -> ctx.request.cookies |> Map.containsKey cookieName

        Remoting.createApi()
        |> Remoting.fromContext cookieServer
        |> Remoting.withRouteBuilder routeBuilder
        |> Remoting.withErrorHandler (fun ex routeInfo -> Propagate ex.Message)
        |> Remoting.buildWebPart
        >=> setCookie

[<EntryPoint>]
let main argv =
    let cwd = Directory.GetCurrentDirectory()
    let root = findRoot cwd 
    let rnd = new Random()
    let port = rnd.Next(5000, 9000) 
    let cts = new CancellationTokenSource() 
    let suaveConfig = 
        { defaultConfig with
            homeFolder = Some (root </> "Fable.Remoting.IntegrationTests" </> "client-dist")
            bindings   = [ HttpBinding.createSimple HTTP "127.0.0.1" port ]
            bufferSize = 2048
            cancellationToken = cts.Token }

    let testWebApp = 
        choose [ 
            GET >=> Files.browseHome >=> Writers.setHeader "Set-Cookie" "dummy=value;"
            fableWebPart 
            CookieTest.cookieWebPart
            AuthServer.api
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

    let mutable autoClose = false
    let driversDir = root </> "UITests" </> "drivers"
    let options = FirefoxOptions()
    match argv with 
    | [| "--headless" |] -> 
        autoClose <- true 
        options.AddArgument("--headless")
    | _ -> () 


    printfn "Starting FireFox Driver"
    use driver = new FirefoxDriver(driversDir, options)
    

    driver.Url <- sprintf "http://localhost:%d/index.html" port
    
    let mutable testsFinishedRunning = false

    while not testsFinishedRunning do
      // give tests time to run
      printfn "Tests have not finished running yet"
      printfn "Waiting for another 5 seconds"
      Threading.Thread.Sleep(5 * 1000)
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

    if autoClose 
    then 
      cts.Cancel()
      driver.Quit()
    else 
      printfn "Finished testing, press any key to continue..."
      Console.ReadKey() |> ignore
    

    try 
      let failedCount = int failedText
      if failedCount <> 0 then 1
      else 0
    with
    | e ->
        printfn "Error occured while parsing the number of failed tests"
        printfn "%s\n%s" e.Message e.StackTrace
        1 // return an integer exit code
