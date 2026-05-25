# Proposal: opt-in System.Text.Json serializer for `Fable.Remoting.Json`

Hi Zaid — opening this as a proposal before any PR lands, so we can agree on the
approach (and the PR shape) up front. Happy to adjust on any of it.

## Motivation

`Fable.Remoting.Json` is built on `Newtonsoft.Json`, which is in maintenance
mode. Every package downstream (`Fable.Remoting.Server`, the Suave / Giraffe /
Falco / AspNetCore / AwsLambda / AzureFunctions adapters, `Fable.Remoting.DotnetClient`)
pulls Newtonsoft transitively, which means the dep flows into every consumer
app even when they'd rather not ship Newtonsoft. For projects going OSS-public
or running supply-chain audits, that's an awkward dep to inherit through what's
otherwise a tight type-safe RPC layer.

System.Text.Json has been the modern .NET default since .NET 5 and ships in the
BCL on `net8.0` — no new package reference needed. The clean fix is to add a
parallel STJ converter set inside `Fable.Remoting.Json` and let consumers
choose. Newtonsoft stays the default; STJ is opt-in.

The client side (`Fable.SimpleJson`) is already Newtonsoft-free and doesn't
need to change — the entire proposal is server-side.

## Approach

A working branch is sitting on a fork: a parallel STJ converter set has been
written, tested, and integrated. The shape:

**1. Parallel converter set, same package.** `Fable.Remoting.Json` gains a
`Fable.Remoting.Json.SystemTextJson` namespace alongside the existing
`Fable.Remoting.Json.FableJsonConverter`. Every Kind branch the Newtonsoft
converter handles has a matching STJ converter:

- `FSharpUnionConverter<'T>` + factory (covers `Kind.Union`; reader accepts
  all five input shapes — string, single-property object, `__typename`-keyed
  union-of-records, `{tag,name,fields}` Fable runtime form, string-prefixed
  array)
- `FSharpOptionConverter<'T>` + factory (`Kind.Option`)
- `FSharpTupleConverter<'T>` + factory (`Kind.Tuple`)
- `FSharpRecordConverter<'T>` + factory (`Kind.Other` for plain F# records)
- `FSharpCliMutableRecordConverter<'T>` + factory (`Kind.MutableRecord`)
- `FSharpSetConverter<'T>` + factory, `FSharpListConverter<'T>` + factory
- `FSharpMapStringKeyConverter<'V>` + factory (`Kind.MapWithStringKey`)
- `FSharpMapNonStringKeyConverter<'K,'V>` + factory (`Kind.MapOrDictWithNonStringKey` —
  including the escaped-quote-property-name pattern your converter emits)
- `Int64Converter` / `UInt64Converter` / `BigIntConverter` (`Kind.Long`, `Kind.BigInt`)
- `DateTimeConverter` (`Kind.DateTime` — three-way Kind branching;
  `DateTimeKind.Unspecified` passes through unchanged, no UTC promotion,
  matching the existing behaviour at `FableConverter.fs:410`)
- `TimeSpanConverter` (`Kind.TimeSpan`)
- `DateOnlyConverter` / `TimeOnlyConverter` (.NET 6+)
- `DataTableConverter` / `DataSetConverter`
- `DoubleConverter` (Newtonsoft preserves `0.0` for whole-valued floats, STJ
  drops the trailing zero by default — converter restores parity)
- `StringConverter` (uses `Utf8JsonWriter.WriteRawValue` to bypass STJ's
  encoder; Newtonsoft emits supplementary-plane codepoints as raw UTF-8 bytes,
  but `JavaScriptEncoder.UnsafeRelaxedJsonEscaping` escapes them in practice —
  surfaced empirically in testing, this fix matches Newtonsoft byte-for-byte)

Plus a setup module:

```fsharp
module FableConverters =
    val addTo : JsonSerializerOptions -> unit
    val create : unit -> JsonSerializerOptions
```

`addTo` is the configuration helper; `create` is the convenience for "give me
a fresh, fully configured `JsonSerializerOptions`."

**2. Opt-in via a new field on `RemotingOptions`.** In
`Fable.Remoting.Server/Types.fs`, a new DU is added:

```fsharp
type JsonSerializerBackend =
    | NewtonsoftJson
    | SystemTextJson of System.Text.Json.JsonSerializerOptions
```

`RemotingOptions<'context, 'serverImpl>` gains a `JsonSerializer` field.
`Remoting.createApi()` defaults to `NewtonsoftJson`. Consumers who do nothing
see no change. Consumers who want STJ add one line:

```fsharp
open Fable.Remoting.Server
open Fable.Remoting.Json.SystemTextJson

let app =
    Remoting.createApi()
    |> Remoting.fromValue myImpl
    |> Remoting.withSerializerOptions (FableConverters.create())
    |> Remoting.buildHttpHandler
```

All six sibling adapters (Giraffe, Suave, Falco, AspNetCore, AwsLambda ×
2, AzureFunctions.Worker) are plumbed: each adapter's `setBody`-shape
helper takes a `JsonSerializerBackend` parameter, and each `fail` entry
pulls `options.JsonSerializer` once and threads it down. So both the
main wire payload AND error / docs-schema responses respect the opt-in.

`Fable.Remoting.DotnetClient` has its own opt-in surface — a parallel
`Remoting.withSerializerOptions` fluent helper on the
`Remoting.createApi → buildProxy` path, plus a
`Proxy<'t>.WithSerializerOptions(opts)` builder member on the lower-level
constructor API. Same pattern as Server-side.

**3. Byte-compatible wire output.** This is the load-bearing constraint:
every Fable client ever deployed against `Fable.Remoting.Json` decodes the
server's bytes via `Fable.SimpleJson`. The PR is "different serializer, same
bytes" — any deviation breaks deployed apps silently.

To hold ourselves to this, the branch carries 103 byte-equality tests pinning
the exact Newtonsoft wire output for representative F# values across every
Kind branch. The same 103 tests then run a second time through the STJ
serializer — same assertions, byte-for-byte. Plus 52 dedicated null-handling
tests (serialise + deserialise) and 23 STJ-specific reader tests. The
byte-compat suite is the contract — if it says bytes must equal `X`, bytes
must equal `X`.

**Test totals on the branch:**

| Project | Count | Status |
|---|---|---|
| `Fable.Remoting.Json.Tests` | 337 | ✅ |
| `Fable.Remoting.Server.Tests` | 30 | ✅ |
| `Fable.Remoting.MsgPack.Tests` | 55 | ✅ |
| `Fable.Remoting.Suave.Tests` | 41 (28 pre-existing + 13 new STJ HTTP integration) | ✅ |
| `Fable.Remoting.Giraffe.Tests` | 114 (96 pre-existing + 18 new STJ HTTP integration) | ✅ |
| `Fable.Remoting.Falco.Tests` | 95 (77 pre-existing + 18 new STJ HTTP integration) | ✅ |
| **Total** | **672/672** | ✅ |

The 49 new HTTP integration tests are end-to-end through a real
`TestServer` (Giraffe / Suave / Falco) — serialise via STJ → real HTTP →
deserialise via STJ → assert. Covers every major shape (primitives,
options, records with None fields, DUs including `Maybe<int>` and simple
`AB`, lists, Maps with string + tuple keys, bigint, Result, byte[]).

The Falco STJ tests are particularly load-bearing — they exercise **both
ends of the wire** in one test: Falco server wired with
`Remoting.withSerializerOptions stjOptions` (server-side STJ) and a
`Fable.Remoting.DotnetClient.Proxy.custom` with
`.WithSerializerOptions(stjOptions)` (client-side STJ). Dogfoods the
entire opt-in surface in one round-trip.

## Unintentional improvement: pre-existing Newtonsoft bug surfaced

`JsonConvert.DeserializeObject<Map<string,int>>("null", FableJsonConverter())`
crashes with `InvalidCastException`. The crash site is
[`FableConverter.fs:669`](https://github.com/Zaid-Ajaj/Fable.Remoting/blob/master/Fable.Remoting.Json/FableConverter.fs#L669) — the `Kind.MapWithStringKey` else-branch
(array-of-pairs fallback) reads
`serializer.Deserialize<JToken>(reader) :?> JArray` without a `JsonToken.Null`
guard. `JValue(null)` fails to cast to `JArray`.

This bites Fable clients that ever send `null` for an `Option<Map<...>>`
field. The STJ port doesn't share the bug — STJ's default `HandleNull = false`
for ref-typed `JsonConverter<T>` returns null directly without invoking the
converter — but if you want, I can also fix the Newtonsoft branch in the same
PR (one-line null guard).

## Suggested PR shape

This can be **one PR** or **a stack of three**, whichever you prefer:

**One PR** — `Fable.Remoting.Json` STJ converter set + `Fable.Remoting.Server`
opt-in plumbing + all six sibling adapters + `Fable.Remoting.DotnetClient`
opt-in + HTTP integration tests. ~2100 lines of converter code + ~900 lines
of tests; default unchanged. The branch as it stands today.

**Three-PR stack** if you'd rather review in chunks:

1. **PR #1 — `Fable.Remoting.Json` converter set + byte-pin tests.** Lands
   the STJ converters in a new sub-namespace with the byte-equality tests
   running against both serializers (337 Json tests, including 52 explicit
   null-handling cases). No downstream changes, no opt-in surface yet — the
   converters are addressable directly via `FableConverters.create()`.
   Dependency-free in terms of breaking changes.
2. **PR #2 — `Fable.Remoting.Server` + sibling adapters opt-in plumbing.**
   Adds `JsonSerializerBackend` DU, threads it through `Server.Proxy.fs` +
   the six sibling adapters (`setBody`-shape helpers + `fail` entries take
   the backend). Plus the Giraffe / Suave / Falco HTTP integration tests as
   end-to-end validation (49 tests). Default stays Newtonsoft.
3. **PR #3 — `Fable.Remoting.DotnetClient` opt-in.** Adds
   `Remoting.withSerializerOptions` fluent helper +
   `Proxy<'t>.WithSerializerOptions` builder member. Threads `stjOptions`
   through the 14 `ServiceCallerFuncN` types. This PR is what makes the
   Falco STJ tests' client side work (the tests live in PR #2 in this split,
   but those specific Falco tests would need a small skip-or-defer for the
   client-side STJ assertions until PR #3 lands).

I have a preference for the three-PR stack — easier review, easier rollback
if anything surprises us — but it's your call.

## Known follow-ups (not part of the initial PR(s))

- **`[<Fable.Core.Pojo>]` and `[<Fable.Core.StringEnum>]` DU dispatch** —
  the Newtonsoft `FableJsonConverter` has explicit branches for these
  (`Kind.PojoDU`, `Kind.StringEnum`). The STJ port doesn't yet — there are
  no test fixtures using these attributes in the existing suite, and no
  client-emitted output to byte-match against. Would land as a follow-up
  once we decide on test shapes.
- **Outer-array argument parsing.** The proxy parses `[arg1, arg2, ...]`
  into `Choice<byte[], JToken> list` using Newtonsoft regardless of
  backend. The per-argument deserialisation IS backend-routed (the byte-compat
  hot path), but the array slicing itself still uses Newtonsoft. Generalising
  this would mean abstracting `InvocationPropsInt.Arguments` over the JSON
  DOM — bigger refactor than the rest of the PR, and not necessary for
  byte-compat. Worth doing one day but not today.
- **Belt-and-braces HTTP integration tests for AspNetCore / AwsLambda /
  AzureFunctions.Worker.** The adapter code in all three is plumbed; the
  shape is identical to Giraffe / Suave / Falco (all use the same
  `setBody`-with-backend pattern). Existing Phase 4b/4d tests cover the
  shape by proxy. A maintainer who wants per-adapter coverage could add
  equivalent test files in a small follow-up — same TestServer-or-
  equivalent pattern as the existing three.

## Sign-off request

Before I open the actual PR(s), wanted to check:

1. **Is the approach acceptable?** Parallel converter set, opt-in via a new
   field on `RemotingOptions`, default unchanged.
2. **One PR or the three-PR stack?**
3. **Do you want the `FableConverter.fs:669` Newtonsoft null bug fixed in
   the same PR**, or as a separate small PR?
4. **Any naming preferences?** I went with `FableConverters.create()` /
   `Remoting.withSerializerOptions` — happy to rename.
5. **`BYTE-COMPAT-MAP.md`** on the branch is a 1300-line working artefact
   documenting every Kind branch's wire shape + surprises caught during
   the port. It's useful as a reference for whoever maintains the converter
   set going forward, but it's verbose. Want it in the PR? Trimmed? Or left
   off?

Branch is on a fork; happy to share the link when you're ready to look. I
won't open the PR until we've agreed on the shape.

Thanks for `Fable.Remoting` — it's been one of the most enjoyable libraries
to use in F#-land for years.
