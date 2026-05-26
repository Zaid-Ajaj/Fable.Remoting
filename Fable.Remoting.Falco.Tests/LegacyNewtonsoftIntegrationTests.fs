module LegacyNewtonsoftIntegrationTests

// Phase 8 (gap #1, #6): explicit coverage for the legacy Newtonsoft path
// after Phase 5's default-flip.
//
// Pairs a Falco server pinned to the legacy backend (via
// `withNewtonsoftJson`) against a DotnetClient.Proxy that also opts back
// into Newtonsoft (default `Proxy.custom` constructor — no STJ helper).
// Catches regressions in either end of the legacy wire.

#nowarn "44"

open System
open System.IO
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.TestHost
open Falco
open Microsoft.Extensions.DependencyInjection
open Fable.Remoting.Server
open Fable.Remoting.Falco
open Expecto
open Types

let private builder = sprintf "/legacyapi/%s/%s"

let private legacyWebApp =
    Remoting.createApi()
    |> Remoting.withRouteBuilder builder
    |> Remoting.withNewtonsoftJson
    |> Remoting.fromValue implementation
    |> Remoting.buildHttpEndpoints

let private configureServices (services: IServiceCollection) =
    services.AddRouting() |> ignore

let private configureApp (app: IApplicationBuilder) =
    app.UseRouting().UseFalco legacyWebApp |> ignore

let private createHost () =
    WebHostBuilder()
        .UseContentRoot(Directory.GetCurrentDirectory())
        .ConfigureServices(Action<IServiceCollection> configureServices)
        .Configure(Action<IApplicationBuilder> configureApp)

let private legacyTestServer = new TestServer(createHost ())
let private legacyClient = legacyTestServer.CreateClient()

// Default DotnetClient.Proxy uses Newtonsoft — no .WithSerializerOptions
// call, so the client side is also legacy.
module private ClientSide =
    open Fable.Remoting.DotnetClient
    let protocolProxy = Proxy.custom<IProtocol> builder legacyClient false

let private protocolProxy = ClientSide.protocolProxy

let legacyNewtonsoftFalcoTests =
    testList "Phase 8 — Legacy Newtonsoft HTTP integration (Falco + DotnetClient)" [

        testCaseAsync "Int round-trip via legacy Newtonsoft (both ends)" <| async {
            let! result = protocolProxy.call (fun s -> s.echoInteger 42)
            Expect.equal result 42 "echoInteger round-trips via legacy Newtonsoft on both ends"
        }

        testCaseAsync "String round-trip via legacy Newtonsoft" <| async {
            let! result = protocolProxy.call (fun s -> s.echoString "hello")
            Expect.equal result "hello" "string round-trips via legacy Newtonsoft"
        }

        testCaseAsync "Option<int> None round-trip via legacy Newtonsoft" <| async {
            let! result = protocolProxy.call (fun s -> s.echoIntOption None)
            Expect.equal result None "None round-trips via legacy Newtonsoft"
        }

        testCaseAsync "DU Maybe<int> Just round-trip via legacy Newtonsoft" <| async {
            let! result = protocolProxy.call (fun s -> s.echoGenericUnionInt (Just 42))
            Expect.equal result (Just 42) "Just 42 round-trips via legacy Newtonsoft"
        }

        testCaseAsync "Record round-trip via legacy Newtonsoft" <| async {
            let input : Record = { Prop1 = "hello"; Prop2 = 42; Prop3 = Some 7 }
            let! result = protocolProxy.call (fun s -> s.echoRecord input)
            Expect.equal result input "record round-trips via legacy Newtonsoft"
        }

        testCaseAsync "Map<int*int,int> round-trip via legacy Newtonsoft" <| async {
            let input = Map.ofList [(1, 1), 10; (2, 2), 20]
            let! result = protocolProxy.call (fun s -> s.echoTupleMap input)
            Expect.equal result input "Map<tuple,int> round-trips via legacy Newtonsoft"
        }

        testCaseAsync "bigint round-trip via legacy Newtonsoft" <| async {
            let input = System.Numerics.BigInteger.Parse "99999999999999999999"
            let! result = protocolProxy.call (fun s -> s.echoBigInteger input)
            Expect.equal result input "bigint round-trips via legacy Newtonsoft"
        }
    ]
