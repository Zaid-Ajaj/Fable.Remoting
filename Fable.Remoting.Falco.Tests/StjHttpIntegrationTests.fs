module StjHttpIntegrationTests

// HTTP integration tests for the System.Text.Json opt-in path through Falco.
// End-to-end exercise: Falco server (STJ opt-in) + DotnetClient.Proxy
// (STJ opt-in via `WithSerializerOptions`) round-tripping representative
// shapes through a real HTTP loop.
//
// Tests dogfood the Phase 4d DotnetClient STJ plumbing alongside the Falco
// server-side wiring.

open System
open System.IO
open System.Text.Json
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.TestHost
open Falco
open Microsoft.Extensions.DependencyInjection
open Fable.Remoting.Server
open Fable.Remoting.Falco
open Fable.Remoting.Json.SystemTextJson
open Expecto
open Types

let private builder = sprintf "/stjapi/%s/%s"
let private stjOptions = FableConverters.create ()

let private stjWebApp =
    Remoting.createApi()
    |> Remoting.withRouteBuilder builder
    |> Remoting.withSerializerOptions stjOptions
    |> Remoting.fromValue implementation
    |> Remoting.buildHttpEndpoints

let private configureServices (services: IServiceCollection) =
    services.AddRouting() |> ignore

let private configureApp (app: IApplicationBuilder) =
    app.UseRouting().UseFalco stjWebApp |> ignore

let private createHost () =
    WebHostBuilder()
        .UseContentRoot(Directory.GetCurrentDirectory())
        .ConfigureServices(Action<IServiceCollection> configureServices)
        .Configure(Action<IApplicationBuilder> configureApp)

let private stjTestServer = new TestServer(createHost ())
let private stjClient = stjTestServer.CreateClient()

// DotnetClient proxy with STJ opt-in — exercises both ends of the wire
// against the same FableConverters.create() options bundle.
// Scope the DotnetClient open inside a module to avoid shadowing the
// Server-side `Remoting` module above.
module private ClientSide =
    open Fable.Remoting.DotnetClient

    let protocolProxy =
        (Proxy.custom<IProtocol> builder stjClient false)
            .WithSerializerOptions(stjOptions)

let private protocolProxy = ClientSide.protocolProxy

let stjFalcoIntegrationTests =
    testList "Phase 4d — STJ HTTP integration (Falco)" [

        testCaseAsync "Int round-trip via STJ" <| async {
            let! result = protocolProxy.call (fun s -> s.echoInteger 42)
            Expect.equal result 42 "echoInteger round-trips through STJ"
        }

        testCaseAsync "String round-trip via STJ" <| async {
            let! result = protocolProxy.call (fun s -> s.echoString "hello world")
            Expect.equal result "hello world" "echoString round-trips through STJ"
        }

        testCaseAsync "Bool round-trip via STJ" <| async {
            let! result = protocolProxy.call (fun s -> s.echoBool true)
            Expect.equal result true "echoBool round-trips through STJ"
        }

        testCaseAsync "Option<int> Some round-trip via STJ" <| async {
            let! result = protocolProxy.call (fun s -> s.echoIntOption (Some 7))
            Expect.equal result (Some 7) "Some 7 round-trips through STJ"
        }

        testCaseAsync "Option<int> None round-trip via STJ" <| async {
            let! result = protocolProxy.call (fun s -> s.echoIntOption None)
            Expect.equal result None "None round-trips through STJ"
        }

        testCaseAsync "DU Maybe<int> Just round-trip via STJ" <| async {
            let! result = protocolProxy.call (fun s -> s.echoGenericUnionInt (Just 42))
            Expect.equal result (Just 42) "Just 42 round-trips through STJ"
        }

        testCaseAsync "DU Maybe<int> Nothing round-trip via STJ" <| async {
            let! result = protocolProxy.call (fun s -> s.echoGenericUnionInt Nothing)
            Expect.equal result Nothing "Nothing round-trips through STJ"
        }

        testCaseAsync "Simple DU AB round-trip via STJ" <| async {
            let! result = protocolProxy.call (fun s -> s.echoSimpleUnion A)
            Expect.equal result A "A round-trips through STJ"
        }

        testCaseAsync "Record round-trip via STJ" <| async {
            let input : Record = { Prop1 = "hello"; Prop2 = 42; Prop3 = Some 7 }
            let! result = protocolProxy.call (fun s -> s.echoRecord input)
            Expect.equal result input "record round-trips through STJ"
        }

        testCaseAsync "Record with None field round-trip via STJ" <| async {
            let input : Record = { Prop1 = "x"; Prop2 = 0; Prop3 = None }
            let! result = protocolProxy.call (fun s -> s.echoRecord input)
            Expect.equal result input "record with None round-trips through STJ"
        }

        testCaseAsync "int list round-trip via STJ" <| async {
            let input = [1; 2; 3; 4; 5]
            let! result = protocolProxy.call (fun s -> s.echoIntList input)
            Expect.equal result input "int list round-trips through STJ"
        }

        testCaseAsync "Record list round-trip via STJ" <| async {
            let input : Record list = [
                { Prop1 = "a"; Prop2 = 1; Prop3 = Some 1 }
                { Prop1 = "b"; Prop2 = 2; Prop3 = None }
            ]
            let! result = protocolProxy.call (fun s -> s.echoRecordList input)
            Expect.equal result input "Record list round-trips through STJ"
        }

        testCaseAsync "Map<string,int> round-trip via STJ" <| async {
            let input = Map.ofList ["a", 1; "b", 2; "c", 3]
            let! result = protocolProxy.call (fun s -> s.echoMap input)
            Expect.equal result input "Map<string,int> round-trips through STJ"
        }

        testCaseAsync "Map<int*int,int> round-trip via STJ" <| async {
            let input = Map.ofList [(1, 1), 10; (2, 2), 20]
            let! result = protocolProxy.call (fun s -> s.echoTupleMap input)
            Expect.equal result input "Map<tuple,int> round-trips through STJ"
        }

        testCaseAsync "bigint round-trip via STJ" <| async {
            let inputs = [1I; 100I; -50I; System.Numerics.BigInteger.Parse "99999999999999999999"]
            for input in inputs do
                let! result = protocolProxy.call (fun s -> s.echoBigInteger input)
                Expect.equal result input "bigint round-trips through STJ"
        }

        testCaseAsync "Result<int,string> Ok round-trip via STJ" <| async {
            let! result = protocolProxy.call (fun s -> s.echoResult (Ok 42))
            Expect.equal result (Ok 42) "Ok 42 round-trips through STJ"
        }

        testCaseAsync "Result<int,string> Error round-trip via STJ" <| async {
            let! result = protocolProxy.call (fun s -> s.echoResult (Error "fail"))
            Expect.equal result (Error "fail") "Error \"fail\" round-trips through STJ"
        }

        testCaseAsync "binaryInputOutput round-trip via STJ" <| async {
            let input : byte[] = [| 1uy; 2uy; 3uy; 4uy; 5uy |]
            let! result = protocolProxy.call (fun s -> s.binaryInputOutput input)
            Expect.equal result input "byte[] round-trips through STJ"
        }
    ]
