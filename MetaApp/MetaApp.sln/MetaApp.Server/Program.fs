module Server

open Suave
open Suave.Filters
open Suave.Operators
open System
open SharedModelsOne


let implementation : IServer = {
    echoMaybe = fun input ->
        match input with
        | Just x -> int x
        | Nothing -> 0
        |> fun result -> async { return Just result }

    personsName = fun p -> async { return p.Name }
    getPerson = fun () -> async { return { Name = "Mike"; Age = 35; Birthday = DateTime.Now } }
    addTwenty = fun x -> async { return x + 20 }

    sumSeq = fun xs -> async { return Seq.sum xs }

    minimumAge = fun people -> 
        async { 
            return people
                   |> Seq.map (fun person -> person.Age)
                   |> Seq.min
        }

    oldestPerson = fun people -> async { 
            return Seq.minBy (fun person -> person.Birthday) people
        }
}


open Fable.Remoting.Suave


[<EntryPoint>]
let main argv = 
    FableSuaveAdapter.logger <- Some (fun text -> Console.WriteLine(text))
    let webApp = FableSuaveAdapter.webPartFor implementation
    startWebServer defaultConfig webApp
    System.Console.ReadKey() |> ignore
    0 // return an integer exit code
