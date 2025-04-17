open System
open System.IO
open Fable.Remoting.Server
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open ServerImpl
open SharedTypes
open Fable.Remoting.Giraffe
open PuppeteerSharp
open System.Threading
open Giraffe
open Microsoft.AspNetCore.Http

let fableWebPart =
    Remoting.createApi()
    |> Remoting.fromValue server
    |> Remoting.withRouteBuilder routeBuilder
    |> Remoting.withErrorHandler (fun ex routeInfo -> Propagate (sprintf "Message: %s, request body: %A" ex.Message routeInfo.requestBodyText))
    |> Remoting.buildHttpHandler

let fableWebPartBinary = 
    Remoting.createApi()
    |> Remoting.fromValue serverBinary
    |> Remoting.withRouteBuilder routeBuilder
    |> Remoting.withErrorHandler (fun ex routeInfo -> Propagate (sprintf "Message: %s, request body: %A" ex.Message routeInfo.requestBodyText))
    |> Remoting.withBinarySerialization
    |> Remoting.buildHttpHandler

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
    let token = routef "/IAuthServer/token/%i" (string >> text)

    // WebPart to ensure that there is a non-empty authorization header
    let requireAuthorized next (ctx: HttpContext) =
        if ctx.Request.Headers.Authorization.Count > 0 then
            next ctx
        else
            Tasks.Task.FromResult None

    // the actual secure api, cannot be reached unless an authorization header is present
    let authorizedServerApi =
        Remoting.createApi()
        |> Remoting.fromContext (fun (ctx: HttpContext) ->
            {
                // return the authorization header
                getSecureValue = fun () ->
                    async {
                        return ctx.Request.Headers.Authorization.Item 0 |> int
                    }
            })
        |> Remoting.withRouteBuilder routeBuilder
        |> Remoting.buildHttpHandler

    let api =
        choose [
            // web part to acquire the token
            token
            // protect authorized server api
            requireAuthorized >=> authorizedServerApi
        ]

module CookieTest =
    let cookieName = "httpOnly-test-cookie"

    let cookieWebPart =
        let cookieServer (ctx: HttpContext): ICookieServer =
            cookieServer <| fun _ ->
                ctx.Response.Cookies.Append (cookieName, "test value", CookieOptions (Secure = false, HttpOnly = true))
                ctx.Request.Cookies.ContainsKey cookieName

        Remoting.createApi()
        |> Remoting.fromContext cookieServer
        |> Remoting.withRouteBuilder routeBuilder
        |> Remoting.withErrorHandler (fun ex routeInfo -> Propagate (sprintf "Message: %s, request body: %A" ex.Message routeInfo.requestBodyText))
        |> Remoting.buildHttpHandler

[<EntryPoint>]
let main argv =
    let cwd = Directory.GetCurrentDirectory()
    let root = findRoot cwd
    let rnd = new Random()
    let port = rnd.Next(5000, 9000)
    let cts = new CancellationTokenSource()
    let homeFolder = root </> "Fable.Remoting.IntegrationTests" </> "client-dist"

    let configureApp (app: IApplicationBuilder) =
        app
            .UseDefaultFiles()
            .UseStaticFiles()
            .UseGiraffe(
                choose [
                    fableWebPart
                    fableWebPartBinary
                    CookieTest.cookieWebPart
                    AuthServer.api
                ]
            )

    let webhost port =
        WebHostBuilder()
            .UseWebRoot(homeFolder)
            .UseContentRoot(homeFolder)
            .Configure(configureApp)
            .UseKestrel()
            .UseUrls($"http://localhost:{port}")
            .Build()

    if not (Array.contains "--headless" argv) then
        printfn "Starting web server..."
        webhost(5000).Run ()
        0
    else
        printfn "Starting Integration Tests"
        printfn ""
        printfn "========== SETUP =========="
        printfn ""
        printfn "Downloading chromium browser..."
        use browserFetcher = new BrowserFetcher()
        browserFetcher.DownloadAsync(BrowserFetcher.DefaultChromiumRevision)
        |> Async.AwaitTask
        |> Async.RunSynchronously
        |> ignore
        printfn "Chromium browser downloaded"

        let _shutdownTask = webhost(port).RunAsync cts.Token

        printfn "Server listening to requests"
        let launchOptions = LaunchOptions()
        launchOptions.ExecutablePath <- browserFetcher.GetExecutablePath(BrowserFetcher.DefaultChromiumRevision)
        launchOptions.Headless <- true

        async {
            use! browser = Async.AwaitTask(Puppeteer.LaunchAsync(launchOptions))
            use! page = Async.AwaitTask(browser.NewPageAsync())
            printfn ""
            printfn "Navigating to http://localhost:%d" port
            let! _ = Async.AwaitTask (page.GoToAsync (sprintf "http://localhost:%d" port))
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