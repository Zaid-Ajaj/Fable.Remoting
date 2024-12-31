open BenchmarkDotNet.Running
open BenchmarkDotNet.Configs
open BenchmarkDotNet.Order

type Orderer () =
    interface IOrderer with
        member _.GetExecutionOrder (benchmarksCase, _) = benchmarksCase :> _
        member _.GetHighlightGroupKey _ = null
        member _.GetLogicalGroupKey (_, benchmarkCase) = sprintf "%s_%s" benchmarkCase.Descriptor.Type.Name benchmarkCase.DisplayInfo
        member _.GetLogicalGroupOrder (logicalGroups, _) = logicalGroups
        member _.GetSummaryOrder (benchmarksCases, _) = benchmarksCases :> _
        member _.SeparateLogicalGroups = true

[<EntryPoint>]
let main _ =
    let config = DefaultConfig.Instance.WithOption(ConfigOptions.JoinSummary, true).WithOrderer (Orderer ())
    let _ = BenchmarkSwitcher.FromAssembly(typeof<Serialization.RecursiveRecord>.Assembly).RunAll config
    0
