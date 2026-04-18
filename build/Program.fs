open System
open System.Collections.Generic
open Fake
open Fake.Core
open Fake.IO
open System.Threading


let (</>) x y = System.IO.Path.Combine(x, y);


let run workingDir fileName args =
    printfn $"CWD: %s{workingDir}"
    let fileName, args =
        if Environment.isUnix
        then fileName, args else "cmd", ("/C " + fileName + " " + args)

    CreateProcess.fromRawCommandLine fileName args
    |> CreateProcess.withWorkingDirectory workingDir
    |> CreateProcess.withTimeout TimeSpan.MaxValue
    |> CreateProcess.ensureExitCodeWithMessage $"'%s{workingDir}> %s{fileName} %s{args}' task failed"
    |> Proc.run
    |> ignore

let execStdout workingDir fileName args =
    printfn $"CWD: %s{workingDir}"
    let fileName, args =
        if Environment.isUnix
        then fileName, args else "cmd", ("/C " + fileName + " " + args)

    CreateProcess.fromRawCommandLine fileName args
    |> CreateProcess.withWorkingDirectory workingDir
    |> CreateProcess.withTimeout TimeSpan.MaxValue
    |> CreateProcess.redirectOutput
    |> CreateProcess.ensureExitCodeWithMessage $"'%s{workingDir}> %s{fileName} %s{args}' task failed"
    |> Proc.run
    |> fun result -> result.Result.Output

let proj file = $"Fable.Remoting.%s{file}" </> $"Fable.Remoting.%s{file}.fsproj"
let testDll file = $"Fable.Remoting.%s{file}.Tests" </> "bin" </> "Release" </> "net9.0" </> $"Fable.Remoting.%s{file}.Tests.dll"

let JsonTestsDll = testDll "Json"
let MsgPackTestsDll = testDll "MsgPack"
let ServerTestsDll = testDll "Server"
let SuaveTestDll = testDll "Suave"
let GiraffeTestDll = testDll "Giraffe"
let FalcoTestDll = testDll "Falco"

let dotnet = "dotnet"

open System.IO
open System.Linq

/// Recursively tries to find the parent of a file starting from a directory
let rec findParent (directory: string) (fileToFind: string) = 
    let path = if Directory.Exists(directory) then directory else Directory.GetParent(directory).FullName
    let files = Directory.GetFiles(path)
    if files.Any(fun file -> Path.GetFileName(file).ToLower() = fileToFind.ToLower()) 
    then path 
    else findParent (DirectoryInfo(path).Parent.FullName) fileToFind
    
let cwd = findParent __SOURCE_DIRECTORY__ "Fable.Remoting.sln"

let getPath x = cwd </> $"Fable.Remoting.%s{x}"

let Client = getPath "Client"
let Json = getPath "Json"
let Server = getPath "Server"
let Suave = getPath "Suave"
let Giraffe = getPath "Giraffe"
let GiraffeNET5 = getPath "GiraffeNET5"
let Falco = getPath "Falco"
let DotnetClient = getPath "DotnetClient"
let AspNetCore = getPath "AspNetCore"
let MsgPack = getPath "MsgPack"
let AzureFunctionsWorker = getPath "AzureFunctions.Worker"
let AwsLambda = getPath "AwsLambda"
let clientTests = cwd </> "ClientTests"
let clientUITests = cwd </> "UITests"
let docs = cwd </> "documentation"

let clean projectPath =
    Shell.cleanDirs [
      projectPath </> "bin"
      projectPath </> "obj"
    ]

let targets = Dictionary<string, TargetParameter -> unit>()

let createTarget name run = targets.Add(name, run)

let publish projectPath = fun _ ->
    clean projectPath
    "pack -c Release"
    |> run projectPath dotnet
    let nugetKey =
        match Environment.environVarOrNone "NUGET_KEY" with
        | Some nugetKey -> nugetKey
        | None -> failwith "The Nuget API key must be set in a NUGET_KEY environmental variable"
    let nupkg = System.IO.Directory.GetFiles(projectPath </> "bin" </> "Release") |> Seq.head
    let pushCmd = $"nuget push %s{nupkg} -s nuget.org -k %s{nugetKey}"
    run projectPath dotnet pushCmd

createTarget "PublishClient" (publish Client)
createTarget "PublishJson" (publish Json)
createTarget "PublishServer" (publish Server)
createTarget "PublishDotnetClient" (publish DotnetClient)
createTarget "PublishSuave" (publish Suave)
createTarget "PublishGiraffeNET5" (publish GiraffeNET5)
createTarget "PublishFalco" (publish Falco)
createTarget "PublishAspnetCore" (publish AspNetCore)
createTarget "PublishMsgPack" (publish MsgPack)
createTarget "PublishAwsLambda" (publish AwsLambda)


createTarget "PublishMsgPackDownstream" (fun ctx ->
    publish MsgPack ctx
    publish Client ctx
    publish Server ctx
    publish Suave ctx
    publish GiraffeNET5 ctx
    publish Falco ctx
    publish AspNetCore ctx
    publish DotnetClient ctx
    publish AzureFunctionsWorker ctx
    publish AwsLambda ctx
)

createTarget "PublishJsonDownstream" (fun ctx ->
    publish Json ctx
    publish Server ctx
    publish Suave ctx
    publish GiraffeNET5 ctx
    publish Falco ctx
    publish AspNetCore ctx
    publish DotnetClient ctx
    publish AzureFunctionsWorker ctx
    publish AwsLambda ctx
)

createTarget "PublishServerDownstream" (fun ctx ->
    publish Server ctx
    publish Suave ctx
    publish GiraffeNET5 ctx
    publish Falco ctx
    publish AspNetCore ctx
    publish AzureFunctionsWorker ctx
)

createTarget "RestoreBuildRunJsonTests" <| fun _ ->
    run cwd "dotnet"  ("restore " + proj "Json.Tests")
    run cwd "dotnet" ("build " + proj "Json.Tests" + " --configuration=Release")
    run cwd "dotnet" JsonTestsDll

createTarget "BuildRunJsonTests" <| fun _ ->
    run cwd "dotnet" ("build " + proj "Json.Tests" + " --configuration=Release")
    run cwd "dotnet" JsonTestsDll

createTarget "RunJsonTests" <| fun _ ->
    run cwd "dotnet" JsonTestsDll

createTarget "RestoreBuildRunServerTests" <| fun _ ->
    run cwd "dotnet"  ("restore " + proj "Server.Tests")
    run cwd "dotnet" ("build " + proj "Server.Tests" + " --configuration=Release")
    run cwd "dotnet" ServerTestsDll

createTarget "BuildGiraffeTests" <| fun _ ->
    clean (getPath "Giraffe")
    clean (getPath "Giraffe.Tests")
    let path = getPath "Giraffe.Tests"
    run path "dotnet" "restore --no-cache"
    run path "dotnet" "build -c Debug"

createTarget "BuildFalcoTests" <| fun _ ->
    clean (getPath "Falco")
    clean (getPath "Falco.Tests")
    let path = getPath "Falco.Tests"
    run path "dotnet" "restore --no-cache"
    run path "dotnet" "build -c Debug"

createTarget "BuildDotnetClientTests" <| fun _ ->
    clean (getPath "IntegrationTests" </> "DotnetClient")
    run (getPath "IntegrationTests" </> "DotnetClient") "dotnet" "build"

createTarget "RunDotnetClientTests" <| fun _ ->
    let path = getPath "IntegrationTests" </> "DotnetClient"
    clean path
    run path "dotnet" "restore --no-cache"
    run path "dotnet" "run"

createTarget "BuildRunServerTests" <| fun _ ->
    run cwd "dotnet" ("build " + proj "Server.Tests" + " --configuration=Release")
    run cwd "dotnet" ServerTestsDll

createTarget "RunServerTests" <| fun _ ->
    run cwd "dotnet" ServerTestsDll

createTarget "RestoreBuildRunSuaveTests" <| fun _ ->
    run cwd "dotnet"  ("restore " + proj "Suave.Tests")
    run cwd "dotnet" ("build " + proj "Suave.Tests" + " --configuration=Release")
    run cwd "dotnet" SuaveTestDll

createTarget "BuildRunSuaveTests" <| fun _ ->
    clean (getPath "Suave")
    clean (getPath "Suave.Tests")
    run cwd "dotnet" ("build " + proj "Suave.Tests" + " --configuration=Release")
    run cwd "dotnet" SuaveTestDll

createTarget "RunSuaveTests" <| fun _ ->
    run cwd "dotnet" SuaveTestDll

createTarget "RestoreBuildRunGiraffeTests" <| fun _ ->
    run cwd "dotnet"  ("restore " + proj "Giraffe.Tests")
    run cwd "dotnet" ("build " + proj "Giraffe.Tests" + " --configuration=Release")
    run cwd "dotnet" GiraffeTestDll

createTarget "BuildRunGiraffeTests" <| fun _ ->
    run cwd "dotnet" ("build " + proj "Giraffe.Tests" + " --configuration=Release")
    run cwd "dotnet" GiraffeTestDll

createTarget "RunGiraffeTests" <| fun _ ->
    run cwd "dotnet" GiraffeTestDll

createTarget "RestoreBuildRunFalcoTests" <| fun _ ->
    run cwd "dotnet"  ("restore " + proj "Falco.Tests")
    run cwd "dotnet" ("build " + proj "Falco.Tests" + " --configuration=Release")
    run cwd "dotnet" FalcoTestDll

createTarget "BuildRunFalcoTests" <| fun _ ->
    run cwd "dotnet" ("build " + proj "Falco.Tests" + " --configuration=Release")
    run cwd "dotnet" FalcoTestDll

createTarget "RunFalcoTests" <| fun _ ->
    run cwd "dotnet" FalcoTestDll

createTarget "BuildDocs" <| fun _ ->
    run docs "npm" "install"
    run docs "npm" "run build"

createTarget "ServeDocs" <| fun _ ->
    run docs "npm" "install"
    run docs "npm" "run serve"

createTarget "PublishDocs" <| fun _ ->
    run docs "npm" "install"
    run docs "npm" "run publish"


let buildRunAzureFunctionsTests onTestsFinished =
    let funcsPath = cwd </> "Fable.Remoting.AzureFunctions.Worker.Tests" </> "FunctionApp"
    let clientPath = cwd </> "Fable.Remoting.AzureFunctions.Worker.Tests" </> "Client"
    
    let mutable started = false
    // Azure Functions Server
    let server = Tasks.Task.Run (fun () ->
        run funcsPath "dotnet" "restore --no-cache"
        started <- true
        run funcsPath "func start" "."
    )

    // Azure Functions Client
    let client = Tasks.Task.Run (fun () ->
        while started = false do
            printfn "Waiting for Azure Functions server to start"
            Thread.Sleep 2000
        
        Thread.Sleep 5000 // give it time to start
        run clientPath "dotnet" "restore --no-cache"
        run clientPath "dotnet" "build --configuration=Release"
        run cwd "dotnet" (clientPath </> "bin" </> "Release" </> "net9.0" </> "Fable.Remoting.AzureFunctions.Worker.Tests.Client.dll")
        onTestsFinished()
        Tasks.Task.CompletedTask
    )

    Tasks.Task.WaitAll (server, client)

createTarget "BuildRunAzureFunctionsTests" <| fun _ -> buildRunAzureFunctionsTests (fun _ -> Environment.Exit(0)) // necessary hack to finish func process
createTarget "PublishAzureFunctionsWorker" <| fun _ -> buildRunAzureFunctionsTests (publish AzureFunctionsWorker)
createTarget "PublishAzureFunctionsWorkerWithoutTests" (publish AzureFunctionsWorker)

createTarget "BuildRunAllTests" <| fun _ ->
    // Json
    run cwd "dotnet" ("build " + proj "Json.Tests" + " --configuration=Release")
    run cwd "dotnet" JsonTestsDll
    // MsgPack
    run cwd "dotnet" ("build " + proj "MsgPack.Tests" + " --configuration=Release")
    run cwd "dotnet" MsgPackTestsDll
    // Server
    run cwd "dotnet" ("build " + proj "Server.Tests" + " --configuration=Release")
    run cwd "dotnet" ServerTestsDll
    // Suave
    run cwd "dotnet" ("build " + proj "Suave.Tests" + " --configuration=Release")
    run cwd "dotnet" SuaveTestDll
    // Giraffe
    run cwd "dotnet" ("build " + proj "Giraffe.Tests" + " --configuration=Release")
    run cwd "dotnet" GiraffeTestDll
    // Falco
    run cwd "dotnet" ("build " + proj "Falco.Tests" + " --configuration=Release")
    run cwd "dotnet" FalcoTestDll

let runHeadlessBrowserTests() =
    run clientUITests "dotnet" "restore --no-cache"
    run clientUITests "dotnet" "run --headless"

let runFableIntegrationTests() = 
    clean (getPath "Server")
    clean (getPath "Json")
    clean (getPath "MsgPack")
    clean (getPath "Suave")
    clean (getPath "UITests")
    clean (getPath "IntegrationTests" </> "Server.Suave")
    Shell.rm (getPath "IntegrationTests" </> "client-dist" </> "bundle.js")
    clean clientTests
    run (clientTests </> "src") "dotnet" "restore"
    run clientTests "npm" "install"
    run clientTests "npm" "run build"
    runHeadlessBrowserTests()

let withDotnetTool (tool: string) (version: string) (f: unit -> unit) =
    let existingTools =
        execStdout cwd "dotnet" "tool list"
        |> String.split '\n'
        |> List.skip 2 // skip table header
        |> List.where String.isNotNullOrEmpty
        |> List.map (fun line ->
            let parts = line.Split(" ", StringSplitOptions.RemoveEmptyEntries)
            let tool = parts[0]
            let version = parts[1]
            tool, version)
        |> Map.ofList

    if not (existingTools.ContainsKey tool) then
        // tool doesn't exist
        run cwd "dotnet" $"tool install {tool} --version {version}"
        try
            f()
        finally
            // uninstall it after having finished working with it
            run cwd "dotnet" $"tool uninstall {tool}"
    else
        // tool exists, keep track of the version
        let originalVersion = existingTools.[tool]
        run cwd "dotnet" $"tool uninstall {tool}"
        run cwd "dotnet" $"tool install {tool} --version {version}"
        try
            f()
        finally
            // revert back to original version
            run cwd "dotnet" $"tool uninstall {tool}"
            run cwd "dotnet" $"tool install {tool} --version {originalVersion}"

createTarget "IntegrationTests" <| fun _ ->
    runFableIntegrationTests()

createTarget "IntegrationTestsLive" <| fun _ ->
    clean (getPath "Server")
    clean (getPath "Json")
    clean (getPath "MsgPack")
    clean (getPath "Suave")
    clean (getPath "UITests")
    clean (getPath "IntegrationTests" </> "Server.Suave")
    Shell.rm (getPath "IntegrationTests" </> "client-dist" </> "bundle.js")
    clean clientTests
    run (clientTests </> "src") "dotnet" "restore"
    run clientTests "npm" "install"
    run clientTests "npm" "run build"
    run clientUITests "dotnet" "restore --no-cache"
    run clientUITests "dotnet" "run"

let runTarget targetName =
    match targets.TryGetValue targetName with
    | true, target ->
        let input = Unchecked.defaultof<TargetParameter>
        target input
    | false, _ -> 
        printfn $"Could not find build target {targetName}"

[<EntryPoint>]
let main(args: string[]) =
    match args with
    | [||] -> runTarget "BuildRunAllTests"
    | [| targetName |] -> runTarget targetName
    | otherwise -> printfn $"Unknown args %A{otherwise}"
    0