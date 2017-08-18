#r "packages/FAKE/tools/FakeLib.dll"

open System
open Fake
open Fake.DotNetCli

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
let testDll file = (sprintf "Fable.Remoting.%s.Tests" file) </> "bin" </> "MCD" </> "Release" </> "netcoreapp2.0" </> (sprintf "Fable.Remoting.%s.Tests.dll" file)


let JsonTestsDll = testDll "Json"
let ServerTestsDll = testDll "Server"
let SuaveTestDll = testDll "Suave"

Target "RestoreBuildRunJsonTests" <| fun _ ->
    run "." "dotnet"  ("restore " + proj "Json.Tests")
    run "." "dotnet" ("build " + proj "Json.Tests" + " --configuration=Release")
    run "." "dotnet" JsonTestsDll

Target "BuildRunJsonTests" <| fun _ ->
    run "." "dotnet" ("build " + proj "Json.Tests" + " --configuration=Release")
    run "." "dotnet" JsonTestsDll

Target "RestoreBuildRunServerTests" <| fun _ ->
    run "." "dotnet"  ("restore " + proj "Server.Tests")
    run "." "dotnet" ("build " + proj "Server.Tests" + " --configuration=Release")
    run "." "dotnet" ServerTestsDll

Target "BuildRunServerTests" <| fun _ ->
    run "." "dotnet" ("build " + proj "Server.Tests" + " --configuration=Release")
    run "." "dotnet" ServerTestsDll

Target "RestoreBuildRunSuaveTests" <| fun _ ->
    run "." "dotnet"  ("restore " + proj "Suave.Tests")
    run "." "dotnet" ("build " + proj "Suave.Tests" + " --configuration=Release")
    run "." "dotnet" SuaveTestDll

Target "BuildRunSuaveTests" <| fun _ ->
    run "." "dotnet" ("build " + proj "Suave.Tests" + " --configuration=Release")
    run "." "dotnet" SuaveTestDll

Target "AllTests" <| DoNothing
Target "Default" <| DoNothing

RunTargetOrDefault "Default"