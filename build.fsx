#r "packages/build/FAKE/tools/FakeLib.dll"

open System
open Fake

let run workingDir fileName args =
    printfn "CWD: %s" workingDir
    let fileName, args =
        if EnvironmentHelper.isUnix
        then fileName, args else "cmd", ("/C " + fileName + " " + args)
    let ok =
        execProcess (fun info ->
            info.FileName <- fileName
            info.WorkingDirectory <- workingDir
            info.Arguments <- args) TimeSpan.MaxValue
    if not ok then failwith (sprintf "'%s> %s %s' task failed" workingDir fileName args)

let proj file = (sprintf "Fable.Remoting.%s" file) </> (sprintf "Fable.Remoting.%s.fsproj" file)
let testDll file = (sprintf "Fable.Remoting.%s.Tests" file) </> "bin" </> "Release" </> "netcoreapp2.0" </> (sprintf "Fable.Remoting.%s.Tests.dll" file)

let JsonTestsDll = testDll "Json"
let ServerTestsDll = testDll "Server"
let SuaveTestDll = testDll "Suave"
let GiraffeTestDll = testDll "Giraffe"

let cwd = __SOURCE_DIRECTORY__
let dotnet = "dotnet"


let getPath x = cwd </> (sprintf "Fable.Remoting.%s" x)

let Client = getPath "Client"
let Json = getPath "Json"
let Server = getPath "Server"
let Reflection = getPath "Reflection"
let Suave = getPath "Suave"
let Giraffe = getPath "Giraffe"
let clean projectPath =
    [ projectPath </> "bin"
      projectPath </> "obj" ] |> CleanDirs

let publish projectPath = fun () ->
    clean projectPath
    "pack -c Release"
    |> run projectPath dotnet
    let nugetKey =
        match environVarOrNone "NUGET_KEY" with
        | Some nugetKey -> nugetKey
        | None -> failwith "The Nuget API key must be set in a NUGET_KEY environmental variable"
    let nupkg = System.IO.Directory.GetFiles(projectPath </> "bin" </> "Release") |> Seq.head
    let pushCmd = sprintf "nuget push %s -s nuget.org -k %s" nupkg nugetKey
    run projectPath dotnet pushCmd


Target "PublishClient" (publish Client)

Target "PublishJson" (publish Json)
Target "PublishServer" (publish Server)
Target "PublishReflection" (publish Reflection)

Target "PublishSuave" (publish Suave)
Target "PublishGiraffe" (publish Giraffe)

Target "CleanGiraffe" <| fun _ ->
    clean (getPath "Giraffe")
    clean (getPath "Giraffe.Tests")

Target "CleanSuave" <| fun _ ->
    clean (getPath "Suave")
    clean (getPath "Suave.Tests")

Target "RestoreBuildRunJsonTests" <| fun _ ->
    run "." "dotnet"  ("restore " + proj "Json.Tests")
    run "." "dotnet" ("build " + proj "Json.Tests" + " --configuration=Release")
    run "." "dotnet" JsonTestsDll

Target "BuildRunJsonTests" <| fun _ ->
    run "." "dotnet" ("build " + proj "Json.Tests" + " --configuration=Release")
    run "." "dotnet" JsonTestsDll

Target "RunJsonTests" <| fun _ ->
    run "." "dotnet" JsonTestsDll

Target "RestoreBuildRunServerTests" <| fun _ ->
    run "." "dotnet"  ("restore " + proj "Server.Tests")
    run "." "dotnet" ("build " + proj "Server.Tests" + " --configuration=Release")
    run "." "dotnet" ServerTestsDll

Target "BuildRunServerTests" <| fun _ ->
    run "." "dotnet" ("build " + proj "Server.Tests" + " --configuration=Release")
    run "." "dotnet" ServerTestsDll

Target "RunServerTests" <| fun _ ->
    run "." "dotnet" ServerTestsDll

Target "RestoreBuildRunSuaveTests" <| fun _ ->
    run "." "dotnet"  ("restore " + proj "Suave.Tests")
    run "." "dotnet" ("build " + proj "Suave.Tests" + " --configuration=Release")
    run "." "dotnet" SuaveTestDll

Target "BuildRunSuaveTests" <| fun _ ->
    run "." "dotnet" ("build " + proj "Suave.Tests" + " --configuration=Release")
    run "." "dotnet" SuaveTestDll

Target "RunSuaveTests" <| fun _ ->
    run "." "dotnet" SuaveTestDll

Target "RestoreBuildRunGiraffeTests" <| fun _ ->
    run "." "dotnet"  ("restore " + proj "Giraffe.Tests")
    run "." "dotnet" ("build " + proj "Giraffe.Tests" + " --configuration=Release")
    run "." "dotnet" GiraffeTestDll

Target "BuildRunGiraffeTests" <| fun _ ->
    run "." "dotnet" ("build " + proj "Giraffe.Tests" + " --configuration=Release")
    run "." "dotnet" GiraffeTestDll

Target "RunGiraffeTests" <| fun _ ->
    run "." "dotnet" GiraffeTestDll

Target "Default" <| DoNothing

"CleanGiraffe"
    ==> "BuildRunGiraffeTests"

"CleanSuave"
  ==> "BuildRunSuaveTests"

Target "BuildRunAllTests" <| fun _ ->
    // Json
    run "." "dotnet" ("build " + proj "Json.Tests" + " --configuration=Release")
    run "." "dotnet" JsonTestsDll
    // Server
    run "." "dotnet" ("build " + proj "Server.Tests" + " --configuration=Release")
    run "." "dotnet" ServerTestsDll
    // Suave
    run "." "dotnet" ("build " + proj "Suave.Tests" + " --configuration=Release")
    run "." "dotnet" SuaveTestDll
    // Giraffe
    run "." "dotnet" ("build " + proj "Giraffe.Tests" + " --configuration=Release")
    run "." "dotnet" GiraffeTestDll

RunTargetOrDefault "BuildRunAllTests"