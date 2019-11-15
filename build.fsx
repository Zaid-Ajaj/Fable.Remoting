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
let testDll file = (sprintf "Fable.Remoting.%s.Tests" file) </> "bin" </> "Release" </> "netcoreapp3.0" </> (sprintf "Fable.Remoting.%s.Tests.dll" file)

let JsonTestsDll = testDll "Json"
let ServerTestsDll = testDll "Server"
let SuaveTestDll = testDll "Suave"
let GiraffeTestDll = testDll "Giraffe"

let dotnet = "dotnet"


let getPath x = cwd </> (sprintf "Fable.Remoting.%s" x)

let Client = getPath "Client"
let ClientV2 = getPath "ClientV2"
let Json = getPath "Json"
let Server = getPath "Server"
let Suave = getPath "Suave"
let Giraffe = getPath "Giraffe"
let DotnetClient = getPath "DotnetClient"
let AspNetCore = getPath "AspNetCore"
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


Target.create "PublishClientV2" (publish ClientV2)
Target.create "PublishJson" (publish Json)
Target.create "PublishServer" (publish Server)
Target.create "PublishDotnetClient" (publish DotnetClient)
Target.create "PublishSuave" (publish Suave)
Target.create "PublishGiraffe" (publish Giraffe)
Target.create "PublishAspnetCore" (publish AspNetCore)

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
    run "docs" "yarn" "install"

Target.create "BuildDocs" <| fun _ ->
    run "docs" "npm" "run build"

Target.create "ServeDocs" <| fun _ ->
    async {
        run "docs" "npm" "run serve"
    }
    |> Async.StartImmediate


Target.create "PublishDocs" <| fun _ ->
    run "docs" "npm" "run publish"

Target.create "Default" (fun _ -> ())

open Fake.Core.TargetOperators

"CleanGiraffe" ==> "BuildRunGiraffeTests"
"CleanSuave" ==> "BuildRunSuaveTests"

Target.create "BuildRunAllTests" <| fun _ ->
    // Json
    run cwd "dotnet" ("build " + proj "Json.Tests" + " --configuration=Release")
    run cwd "dotnet" JsonTestsDll
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
    clean (getPath "Suave")
    clean (getPath "UITests")
    clean (getPath "Fable.Remoting.ClientV2")
    clean (getPath "ClientV2Tests")
    clean (getPath "IntegrationTests" </> "Server.Suave")
    clean "ClientV2Tests"
    Shell.cleanDirs [ getPath "ClientV2Tests" </> ".fable" ]
    run "ClientV2Tests" "npm" "install"
    run "ClientV2Tests" "npm" "run build"
    run "UITests" "dotnet" "restore --no-cache"
    run "UITests" "dotnet" "run --headless"

Target.create "IntegrationTestsLive" <| fun _ ->
    clean (getPath "Server")
    clean (getPath "Json")
    clean (getPath "Suave")
    clean (getPath "UITests")
    clean (getPath "IntegrationTests" </> "Server.Suave")
    clean "ClientV2Tests"

    run "ClientV2Tests" "npm" "install"
    run "ClientV2Tests" "npm" "build"
    run "UITests" "dotnet" "restore --no-cache"
    run "UITests" "dotnet" "run"

Target.runOrDefault "BuildRunAllTests"