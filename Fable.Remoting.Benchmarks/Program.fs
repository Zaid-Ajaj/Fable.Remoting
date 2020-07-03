open BenchmarkDotNet.Running

[<EntryPoint>]
let main argv =
    let results = [
        BenchmarkRunner.Run<Serialization.Test> ()
    ]
    0
    