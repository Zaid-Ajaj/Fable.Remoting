#r "paket: groupref build //"
#load "./.fake/build.fsx/intellisense.fsx"

#if !FAKE
#r "netstandard"
#r "Facades/netstandard" // https://github.com/ionide/ionide-vscode-fsharp/issues/839#issuecomment-396296095
#endif

open System
open Fake
open Fake.Core
open Fake.IO
open Fake.SystemHelper
open System.Threading

let (</>) x y = System.IO.Path.Combine(x, y);
let cwd = __SOURCE_DIRECTORY__

let run workingDir fileName args =
    printfn "CWD: %s" workingDir
    let fileName, args =
        if Environment.isUnix
        then fileName, args else "cmd", ("/C " + fileName + " " + args)

    CreateProcess.fromRawCommandLine fileName args
    |> CreateProcess.withWorkingDirectory workingDir
    |> CreateProcess.withTimeout TimeSpan.MaxValue
    |> CreateProcess.ensureExitCodeWithMessage (sprintf "'%s> %s %s' task failed" workingDir fileName args)
    |> Proc.run
    |> ignore


let proj file = (sprintf "Fable.Remoting.%s" file) </> (sprintf "Fable.Remoting.%s.fsproj" file)
let testDll file = (sprintf "Fable.Remoting.%s.Tests" file) </> "bin" </> "Release" </> "net6.0" </> (sprintf "Fable.Remoting.%s.Tests.dll" file)

let JsonTestsDll = testDll "Json"
let MsgPackTestsDll = testDll "MsgPack"
let ServerTestsDll = testDll "Server"
let SuaveTestDll = testDll "Suave"
let GiraffeTestDll = testDll "Giraffe"

let dotnet = "dotnet"


let getPath x = cwd </> (sprintf "Fable.Remoting.%s" x)

let ClientV2 = getPath "ClientV2"
let Json = getPath "Json"
let Server = getPath "Server"
let Suave = getPath "Suave"
let Giraffe = getPath "Giraffe"
let GiraffeNET5 = getPath "GiraffeNET5"
let DotnetClient = getPath "DotnetClient"
let AspNetCore = getPath "AspNetCore"
let MsgPack = getPath "MsgPack"
let AzureFunctionsWorker = getPath "AzureFunctions.Worker"

let clean projectPath =
    Shell.cleanDirs [
      projectPath </> "bin"
      projectPath </> "obj"
    ]

let publish projectPath = fun _ ->
    clean projectPath
    "pack -c Release"
    |> run projectPath dotnet
    let nugetKey =
        match Environment.environVarOrNone "NUGET_KEY" with
        | Some nugetKey -> nugetKey
        | None -> failwith "The Nuget API key must be set in a NUGET_KEY environmental variable"
    let nupkg = System.IO.Directory.GetFiles(projectPath </> "bin" </> "Release") |> Seq.head
    let pushCmd = sprintf "nuget push %s -s nuget.org -k %s" nupkg nugetKey
    run projectPath dotnet pushCmd

Target.create "PublishClient" (publish ClientV2)
Target.create "PublishJson" (publish Json)
Target.create "PublishServer" (publish Server)
Target.create "PublishDotnetClient" (publish DotnetClient)
Target.create "PublishSuave" (publish Suave)
Target.create "PublishGiraffe" (publish Giraffe)
Target.create "PublishGiraffeNET5" (publish GiraffeNET5)
Target.create "PublishAspnetCore" (publish AspNetCore)
Target.create "PublishMsgPack" (publish MsgPack)


Target.create "PublishMsgPackDownstream" (fun ctx ->
    publish MsgPack ctx
    publish ClientV2 ctx
    publish Server ctx
    publish Suave ctx
    publish GiraffeNET5 ctx
    publish AspNetCore ctx
    publish DotnetClient ctx
    publish AzureFunctionsWorker ctx
)

Target.create "PublishJsonDownstream" (fun ctx ->
    publish Json ctx
    publish Server ctx
    publish Suave ctx
    publish GiraffeNET5 ctx
    publish AspNetCore ctx
    publish DotnetClient ctx
    publish AzureFunctionsWorker ctx
)

Target.create "CleanGiraffe" <| fun _ ->
    clean (getPath "Giraffe")
    clean (getPath "Giraffe.Tests")

Target.create "CleanSuave" <| fun _ ->
    clean (getPath "Suave")
    clean (getPath "Suave.Tests")

Target.create "RestoreBuildRunJsonTests" <| fun _ ->
    run cwd "dotnet"  ("restore " + proj "Json.Tests")
    run cwd "dotnet" ("build " + proj "Json.Tests" + " --configuration=Release")
    run cwd "dotnet" JsonTestsDll

Target.create "BuildRunJsonTests" <| fun _ ->
    run cwd "dotnet" ("build " + proj "Json.Tests" + " --configuration=Release")
    run cwd "dotnet" JsonTestsDll

Target.create "RunJsonTests" <| fun _ ->
    run cwd "dotnet" JsonTestsDll

Target.create "RestoreBuildRunServerTests" <| fun _ ->
    run cwd "dotnet"  ("restore " + proj "Server.Tests")
    run cwd "dotnet" ("build " + proj "Server.Tests" + " --configuration=Release")
    run cwd "dotnet" ServerTestsDll

Target.create "BuildGiraffeTests" <| fun _ ->
    let path = getPath "Giraffe.Tests"
    run path "dotnet" "restore --no-cache"
    run path "dotnet" "build -c Debug"

Target.create "BuildDotnetClientTests" <| fun _ ->
    clean (getPath "IntegrationTests" </> "DotnetClient")
    run (getPath "IntegrationTests" </> "DotnetClient") "dotnet" "build"

Target.create "RunDotnetClientTests" <| fun _ ->
    let path = getPath "IntegrationTests" </> "DotnetClient"
    clean path
    run path "dotnet" "restore --no-cache"
    run path "dotnet" "run"

Target.create "BuildRunServerTests" <| fun _ ->
    run cwd "dotnet" ("build " + proj "Server.Tests" + " --configuration=Release")
    run cwd "dotnet" ServerTestsDll

Target.create "RunServerTests" <| fun _ ->
    run cwd "dotnet" ServerTestsDll

Target.create "RestoreBuildRunSuaveTests" <| fun _ ->
    run cwd "dotnet"  ("restore " + proj "Suave.Tests")
    run cwd "dotnet" ("build " + proj "Suave.Tests" + " --configuration=Release")
    run cwd "dotnet" SuaveTestDll

Target.create "BuildRunSuaveTests" <| fun _ ->
    run cwd "dotnet" ("build " + proj "Suave.Tests" + " --configuration=Release")
    run cwd "dotnet" SuaveTestDll

Target.create "RunSuaveTests" <| fun _ ->
    run cwd "dotnet" SuaveTestDll

Target.create "RestoreBuildRunGiraffeTests" <| fun _ ->
    run cwd "dotnet"  ("restore " + proj "Giraffe.Tests")
    run cwd "dotnet" ("build " + proj "Giraffe.Tests" + " --configuration=Release")
    run cwd "dotnet" GiraffeTestDll

Target.create "BuildRunGiraffeTests" <| fun _ ->
    run cwd "dotnet" ("build " + proj "Giraffe.Tests" + " --configuration=Release")
    run cwd "dotnet" GiraffeTestDll

Target.create "RunGiraffeTests" <| fun _ ->
    run cwd "dotnet" GiraffeTestDll

Target.create "InstallDocs" <| fun _ ->
    run "documentation" "npm" "install"

Target.create "BuildDocs" <| fun _ ->
    run "documentation" "npm" "run build"

Target.create "ServeDocs" <| fun _ ->
    run "documentation" "npm" "run serve"

Target.create "PublishDocs" <| fun _ ->
    run "documentation" "npm" "run publish"

Target.create "Default" (fun _ -> ())

open Fake.Core.TargetOperators

"CleanGiraffe" ==> "BuildRunGiraffeTests"
"CleanSuave" ==> "BuildRunSuaveTests"

let buildRunAzureFunctionsTests onTestsFinished =
    let funcsPath = "Fable.Remoting.AzureFunctions.Worker.Tests" </> "FunctionApp"
    let clientPath = "Fable.Remoting.AzureFunctions.Worker.Tests" </> "Client"
    
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
        run cwd "dotnet" (clientPath </> "bin" </> "Release" </> "net6.0" </> "Fable.Remoting.AzureFunctions.Worker.Tests.Client.dll")
        onTestsFinished()
        Tasks.Task.CompletedTask
    )

    Tasks.Task.WaitAll (server, client)

Target.create "BuildRunAzureFunctionsTests" <| fun _ -> buildRunAzureFunctionsTests (fun _ -> Environment.Exit(0)) // necessary hack to finish func process
Target.create "PublishAzureFunctionsWorker" <| fun _ -> buildRunAzureFunctionsTests (publish AzureFunctionsWorker)


Target.create "BuildRunAllTests" <| fun _ ->
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

Target.create "IntegrationTests" <| fun _ ->
    clean (getPath "Server")
    clean (getPath "Json")
    clean (getPath "MsgPack")
    clean (getPath "Suave")
    clean (getPath "UITests")
    clean (getPath "IntegrationTests" </> "Server.Suave")
    clean "ClientV2Tests"
    Shell.rm (getPath "IntegrationTests" </> "client-dist" </> "bundle.js")
    run ("ClientV2Tests" </> "src") "dotnet" "restore"
    run "ClientV2Tests" "npm" "install"
    run "ClientV2Tests" "npm" "run build"
    run "UITests" "dotnet" "restore --no-cache"
    run "UITests" "dotnet" "run --headless"

Target.create "IntegrationTestsNagareyama" <| fun _ ->
    clean (getPath "Server")
    clean (getPath "Json")
    clean (getPath "MsgPack")
    clean (getPath "Suave")
    clean (getPath "UITests")
    clean (getPath "IntegrationTests" </> "Server.Suave")
    Shell.rm (getPath "IntegrationTests" </> "client-dist" </> "bundle.js")
    clean "ClientV2Tests"
    run ("ClientV2Tests" </> "src") "dotnet" "restore"
    run "ClientV2Tests" "npm" "install"
    run "ClientV2Tests" "npm" "run build-nagareyama"
    run "UITests" "dotnet" "restore --no-cache"
    run "UITests" "dotnet" "run --headless"

Target.create "IntegrationTestsNagareyamaLive" <| fun _ ->
    clean (getPath "Server")
    clean (getPath "Json")
    clean (getPath "MsgPack")
    clean (getPath "Suave")
    clean (getPath "UITests")
    clean (getPath "IntegrationTests" </> "Server.Suave")
    Shell.rm (getPath "IntegrationTests" </> "client-dist" </> "bundle.js")
    clean "ClientV2Tests"
    run ("ClientV2Tests" </> "src") "dotnet" "restore"
    run "ClientV2Tests" "npm" "install"
    run "ClientV2Tests" "npm" "run build-nagareyama"
    run "UITests" "dotnet" "restore --no-cache"
    run "UITests" "dotnet" "run"

Target.create "IntegrationTestsLive" <| fun _ ->
    clean (getPath "Server")
    clean (getPath "Json")
    clean (getPath "MsgPack")
    clean (getPath "Suave")
    clean (getPath "UITests")
    clean (getPath "IntegrationTests" </> "Server.Suave")
    clean "ClientV2Tests"

    run "ClientV2Tests" "npm" "install"
    run "UITests" "dotnet" "restore --no-cache"

    run "ClientV2Tests" "npm" "run build"
    run "UITests" "dotnet" "run"

Target.runOrDefault "BuildRunAllTests"
