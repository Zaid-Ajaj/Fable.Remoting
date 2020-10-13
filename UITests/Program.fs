open System
open Suave
open Suave.Successful
open System.IO
open Fable.Remoting.Server
open Fable.Remoting.Suave
open System.Threading
open Suave.Operators
open Suave.Filters
open SharedTypes
open ServerImpl
open PuppeteerSharp

let fableWebPart =
    Remoting.createApi()
    |> Remoting.fromContext (fun ctx -> server)
    |> Remoting.withRouteBuilder routeBuilder
    |> Remoting.withErrorHandler (fun ex _ -> Propagate ex.Message)
    |> Remoting.buildWebPart

let fableWebPartBinary = 
    Remoting.createApi()
    |> Remoting.fromContext (fun ctx -> serverBinary)
    |> Remoting.withRouteBuilder routeBuilder
    |> Remoting.withErrorHandler (fun ex routeInfo -> Propagate ex.Message)
    |> Remoting.withBinarySerialization
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
            fableWebPartBinary
            CookieTest.cookieWebPart
            AuthServer.api
            OK "Not Found"
        ]

    if not (Array.contains "--headless" argv)
    then
      printfn "Starting web server..."
      startWebServer { suaveConfig with bindings = [ HttpBinding.createSimple HTTP "127.0.0.1" 5000 ] } testWebApp
      0
    else
    printfn "Starting Integration Tests"
    printfn ""
    printfn "========== SETUP =========="
    printfn ""
    printfn "Downloading chromium browser..."
    let browserFetcher = BrowserFetcher()
    browserFetcher.DownloadAsync(BrowserFetcher.DefaultRevision)
    |> Async.AwaitTask
    |> Async.RunSynchronously
    |> ignore
    printfn "Chromium browser downloaded"
    let listening, server = startWebServerAsync suaveConfig testWebApp

    Async.Start server
    printfn "Web server started"

    printfn "Getting server ready to listen for reqeusts"
    listening
    |> Async.RunSynchronously
    |> ignore

    printfn "Server listening to requests"
    let launchOptions = LaunchOptions()
    launchOptions.ExecutablePath <- browserFetcher.GetExecutablePath(BrowserFetcher.DefaultRevision)
    launchOptions.Headless <- true

    async {
        use! browser = Async.AwaitTask(Puppeteer.LaunchAsync(launchOptions))
        use! page = Async.AwaitTask(browser.NewPageAsync())
        printfn ""
        printfn "Navigating to http://localhost:%d/index.html" port
        let! _ = Async.AwaitTask (page.GoToAsync (sprintf "http://localhost:%d/index.html" port))
        let stopwatch = System.Diagnostics.Stopwatch.StartNew()
        let toArrayFunction = """
        window.domArr = function(elements) {
            var arr = [ ];
            for(var i = 0; i < elements.length;i++) arr.push(elements.item(i));
            return arr;
        };
        """

        let getResultsFunctions = """
        window.getTests = function() {
            var tests = document.querySelectorAll("div.passed, div.executing, div.failed, div.pending");
            return domArr(tests).map(function(test) {
                var name = test.getAttribute('data-test')
                var type = test.classList[0]
                var module =
                    type === 'failed'
                    ? test.parentNode.parentNode.parentNode.getAttribute('data-module')
                    : test.parentNode.parentNode.getAttribute('data-module')
                return [name, type, module];
            });
        }
        """
        let! _ = Async.AwaitTask (page.EvaluateExpressionAsync(toArrayFunction))
        let! _ = Async.AwaitTask (page.EvaluateExpressionAsync(getResultsFunctions))
        let! _ = Async.AwaitTask (page.WaitForExpressionAsync("document.getElementsByClassName('executing').length === 0"))
        stopwatch.Stop()
        printfn "Finished running tests, took %d ms" stopwatch.ElapsedMilliseconds
        let passingTests = "document.getElementsByClassName('passed').length"
        let! passedTestsCount = Async.AwaitTask (page.EvaluateExpressionAsync<int>(passingTests))
        let failingTests = "document.getElementsByClassName('failed').length"
        let! failedTestsCount = Async.AwaitTask (page.EvaluateExpressionAsync<int>(failingTests))
        let pendingTests = "document.getElementsByClassName('pending').length"
        let! pendingTestsCount = Async.AwaitTask(page.EvaluateExpressionAsync<int>(pendingTests))
        let! testResults = Async.AwaitTask (page.EvaluateExpressionAsync<string [] []>("window.getTests()"))
        printfn ""
        printfn "========== SUMMARY =========="
        printfn ""
        printfn "Total test count %d" (passedTestsCount + failedTestsCount + pendingTestsCount)
        printfn "Passed tests %d" passedTestsCount
        printfn "Failed tests %d" failedTestsCount
        printfn "Skipped tests %d" pendingTestsCount
        printfn ""
        printfn "========== TESTS =========="
        printfn ""
        let moduleGroups = testResults |> Array.groupBy (fun arr -> arr.[2])

        for (moduleName, tests) in moduleGroups do
            for test in tests do
                let name = test.[0]
                let testType = test.[1]

                match testType with
                | "passed" ->
                    Console.ForegroundColor <- ConsoleColor.Green
                    printfn "√ %s / %s" moduleName name
                | "failed" ->
                    Console.ForegroundColor <- ConsoleColor.Red
                    printfn "X %s / %s" moduleName name
                | "pending" ->
                    Console.ForegroundColor <- ConsoleColor.Blue
                    printfn "~ %s / %s" moduleName name
                | other ->
                    printfn "~ %s / %s" moduleName name

        Console.ResetColor()
        printfn ""
        printfn "Stopping web server..."
        cts.Cancel()
        printfn "Exit code: %d" failedTestsCount
        return failedTestsCount
    }

    |> Async.RunSynchronously