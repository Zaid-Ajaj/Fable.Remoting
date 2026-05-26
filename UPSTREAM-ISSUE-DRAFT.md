# Proposal: replace Newtonsoft.Json with System.Text.Json in `Fable.Remoting`

Hi Zaid — opening this as a proposal before any PR lands, so we can agree on the
approach (and the PR shape) up front. Happy to adjust on any of it.

## TL;DR

A working branch on a fork ports the JSON serializer from Newtonsoft.Json
to System.Text.Json, **byte-equally** for every shape Fable.Remoting
produces today. The branch:

- Adds a parallel STJ converter set inside `Fable.Remoting.Json`.
- Plumbs the choice through `Fable.Remoting.Server`, the six sibling
  adapters (Giraffe / Suave / Falco / AspNetCore / AwsLambda × 2 /
  AzureFunctions.Worker), and `Fable.Remoting.DotnetClient`.
- **Flips the default** so `Remoting.createApi()` returns options
  pre-configured with STJ. Existing Fable / DotnetClient clients see no
  change in the bytes they read on the wire (proven by 349 byte-pin
  tests running the same assertions against both serializers, plus 70+
  HTTP integration tests covering Giraffe / Suave / Falco).
- Marks the Newtonsoft path `[<Obsolete>]` with a one-major-version
  deprecation window via a `Remoting.withNewtonsoftJson` opt-back-in
  helper.

The end state: in **v5.0**, `FableJsonConverter` + the Newtonsoft package
reference can be deleted from `Fable.Remoting.Json` entirely. The
deletion is a mechanical follow-up — no design decisions, no risk. All
the hard work (byte-compat verification, dual-backend wiring, latent
bugs surfaced and fixed) lives in **this** PR.

Two pre-existing Newtonsoft bugs were fixed as a side-effect:
- `Map<K, V>` deserialise from `"null"` crashed with `InvalidCastException`.
- `[<Fable.Core.Pojo>]` and `[<Fable.Core.StringEnum>]` were silently
  ignored on DUs with field-bearing cases (case-subtype attribute
  inheritance).

## Motivation

`Newtonsoft.Json` is in maintenance mode; `System.Text.Json` has been the
modern .NET default since .NET 5 and ships in the BCL on `net8.0` — no
new package reference needed. Every consumer of `Fable.Remoting.Server`
(or any sibling adapter, or `Fable.Remoting.DotnetClient`) pulls Newtonsoft
transitively, which means the dep flows into every consumer app whether
they want it or not. For projects going OSS-public, or running
supply-chain audits, that's an awkward dep to inherit through what's
otherwise a tight type-safe RPC layer.

The client side (`Fable.SimpleJson`) is already Newtonsoft-free, so the
entire proposal is server-side.

## Approach

**1. STJ becomes the default; Newtonsoft is opt-out via `[<Obsolete>]`
helper.** `Remoting.createApi()` returns options pre-configured with
`Fable.Remoting.Json.SystemTextJson.FableConverters.create()`. Consumers
who do nothing get the new default. Consumers who need the legacy path
(for verification during migration, or because they touch
`FableJsonConverter` in custom code) pipe through:

```fsharp
let api =
    Remoting.createApi()
    |> Remoting.withNewtonsoftJson    // [<Obsolete>] — for migration only
    |> Remoting.fromValue myImpl
```

Both `withNewtonsoftJson` and `FableJsonConverter` are marked
`[<Obsolete>]` with pointer to `MIGRATION.md`. In **v5.0**, both go away
along with the Newtonsoft package reference.

**2. Parallel converter set in `Fable.Remoting.Json`.** New namespace
`Fable.Remoting.Json.SystemTextJson` with a full STJ converter for every
Kind branch the Newtonsoft `FableJsonConverter` handles:

- `FSharpUnionConverter<'T>` + factory (covers `Kind.Union`; reader
  accepts all five input shapes — string, single-property object,
  `__typename`-keyed union-of-records, `{tag,name,fields}` Fable runtime
  form, string-prefixed array)
- `FSharpOptionConverter<'T>` + factory (`Kind.Option`)
- `FSharpTupleConverter<'T>` + factory (`Kind.Tuple`)
- `FSharpRecordConverter<'T>` + factory (`Kind.Other` for plain F# records)
- `FSharpCliMutableRecordConverter<'T>` + factory (`Kind.MutableRecord`)
- `FSharpSetConverter<'T>` + factory, `FSharpListConverter<'T>` + factory
- `FSharpMapStringKeyConverter<'V>` + factory (`Kind.MapWithStringKey`)
- `FSharpMapNonStringKeyConverter<'K,'V>` + factory
  (`Kind.MapOrDictWithNonStringKey` — including the escaped-quote-property-
  name pattern your converter emits)
- `FSharpPojoDUConverter<'T>` + factory (`Kind.PojoDU`)
- `FSharpStringEnumConverter<'T>` + factory (`Kind.StringEnum`)
- `Int64Converter` / `UInt64Converter` / `BigIntConverter` (`Kind.Long`,
  `Kind.BigInt`)
- `DateTimeConverter` (`Kind.DateTime` — three-way Kind branching;
  `DateTimeKind.Unspecified` passes through unchanged, no UTC promotion,
  matching the existing behaviour at `FableConverter.fs:410`)
- `TimeSpanConverter` (`Kind.TimeSpan`)
- `DateOnlyConverter` / `TimeOnlyConverter` (.NET 6+)
- `DataTableConverter` / `DataSetConverter`
- `DoubleConverter` (Newtonsoft preserves `0.0` for whole-valued floats,
  STJ drops the trailing zero by default — converter restores parity)
- `StringConverter` (uses `Utf8JsonWriter.WriteRawValue` to bypass STJ's
  encoder; Newtonsoft emits supplementary-plane codepoints as raw UTF-8
  bytes, but `JavaScriptEncoder.UnsafeRelaxedJsonEscaping` escapes them
  in practice — surfaced empirically in testing, this fix matches
  Newtonsoft byte-for-byte)

Plus a setup module:

```fsharp
module FableConverters =
    val addTo : JsonSerializerOptions -> unit
    val create : unit -> JsonSerializerOptions
```

`addTo` configures an existing options instance; `create` is the
convenience for "give me a fresh, fully-configured `JsonSerializerOptions`."

**3. Backend choice plumbed everywhere on the server side.**

- `Fable.Remoting.Server`: new `JsonSerializerBackend` DU on
  `RemotingOptions` + the `withNewtonsoftJson` / `withSerializerOptions`
  fluent helpers. `Server.Proxy.fs` branches outer-array parsing,
  per-argument deserialisation, and response serialisation on the
  backend.
- All six sibling adapters: each adapter's `setBody`-shape helper (used
  for docs schema responses and error responses) takes the
  `JsonSerializerBackend` and routes through the
  backend-aware serializer. Without this, error / docs paths would silently
  fall back to Newtonsoft even when consumers opted in to STJ.
- `Fable.Remoting.DotnetClient`: parallel
  `Remoting.withSerializerOptions` fluent helper on the
  `Remoting.createApi → buildProxy` path, plus a
  `Proxy<'t>.WithSerializerOptions(opts)` builder member on the
  constructor-style API. Threads `stjOptions` through the 14 internal
  `ServiceCallerFuncN` types.

**4. Byte-compatible wire output.** This is the load-bearing constraint:
every Fable client ever deployed against `Fable.Remoting.Json` decodes
the server's bytes via `Fable.SimpleJson`. Any deviation breaks deployed
apps silently.

To hold ourselves to this, the branch carries 103 byte-equality tests +
26 dedicated null-handling tests + 12 Pojo/StringEnum byte-pin tests +
23 STJ-specific reader tests. The same byte-pin gallery runs through
both serializers via an `ISerializer` abstraction — same assertions,
byte-for-byte. Plus 70+ HTTP integration tests across Giraffe / Suave /
Falco that round-trip representative shapes through real `TestServer`s
or live Suave listeners, both via the STJ default AND via the
explicit `withNewtonsoftJson` opt-back-in (so the legacy path stays
under automated coverage through the deprecation window).

**Test totals on the branch:**

| Project | Count | Status |
|---|---|---|
| `Fable.Remoting.Json.Tests` | 349 | ✅ |
| `Fable.Remoting.Server.Tests` | 30 | ✅ |
| `Fable.Remoting.MsgPack.Tests` | 55 | ✅ |
| `Fable.Remoting.Suave.Tests` | 48 (28 legacy + 13 STJ + 7 legacy-canary) | ✅ |
| `Fable.Remoting.Giraffe.Tests` | 120 (96 legacy + 18 STJ + 6 legacy-canary) | ✅ |
| `Fable.Remoting.Falco.Tests` | 102 (77 legacy + 18 STJ + 7 legacy-canary) | ✅ |
| **Total** | **704/704** | ✅ |

The `legacy-canary` test files (`LegacyNewtonsoftIntegrationTests.fs` in
each adapter project) pin a `Remoting.withNewtonsoftJson` server +
representative roundtrips — including the DateTimeOffset
offset-preservation case (the canary that surfaced a per-argument
DateParseHandling regression during the port). They prove the legacy
path stays operational through the deprecation window.

## Unintentional improvements: pre-existing Newtonsoft bugs surfaced

Two real bugs in the existing `FableJsonConverter` were caught and fixed
as a side effect of the byte-equality testing:

**1. `Map<K, V>` deserialise from `"null"` crashes.**
`JsonConvert.DeserializeObject<Map<string,int>>("null", FableJsonConverter())`
threw `InvalidCastException` at
[`FableConverter.fs:669`](https://github.com/Zaid-Ajaj/Fable.Remoting/blob/master/Fable.Remoting.Json/FableConverter.fs#L669) —
the `Kind.MapWithStringKey` else-branch (array-of-pairs fallback) tried
to cast `JValue(null)` to `JArray` without a null guard. Bites any Fable
client that ever sends `null` for an `Option<Map<...>>` field. The STJ
path doesn't share the bug — STJ's default `HandleNull = false`
returns null directly without invoking the converter. The branch
documents this with an STJ-only test list
`stjFixesNewtonsoftNullBug` in `StjWireFormatTests.fs`.

**2. `[<Fable.Core.Pojo>]` / `[<Fable.Core.StringEnum>]` silently ignored on
DUs with field-bearing cases.** `getUnionKind` at
[`FableConverter.fs:156-163`](https://github.com/Zaid-Ajaj/Fable.Remoting/blob/master/Fable.Remoting.Json/FableConverter.fs#L156-L163)
read attributes from the runtime type via `value.GetType()`. For a DU
with at least one field-bearing case, F# emits each case as a nested
subtype (e.g. `PojoDU+PojoOne`), and these subtypes do NOT inherit the
attribute from the declaring DU. So the attribute lookup silently
returned None → fallback to `Kind.Union` → Pojo DUs were mis-serialised
as regular Unions. Fixed by normalising `t` to the declaring type via
`FSharpType.GetUnionCases(t).[0].DeclaringType` before reading
attributes. The STJ path was correct by construction — factories
dispatch on the declared static type, not the runtime case-subtype.

If you'd prefer these bug fixes land in a separate small PR before the
STJ work (so v4.x consumers benefit immediately), I can split them.

## Suggested PR shape

This can be **one PR** or **a stack of three**, whichever you prefer:

**One PR** — everything in the branch: STJ converter set, byte-pin
tests, sibling-adapter plumbing, default flip + Newtonsoft `[<Obsolete>]`,
DotnetClient opt-in, MIGRATION.md. Default unchanged from consumer
perspective (wire format byte-equal). ~30 files changed.

**Three-PR stack** if you'd rather review in chunks:

1. **PR #1 — `Fable.Remoting.Json` converter set + byte-pin tests + the
   two pre-existing-bug fixes.** Lands the STJ converters in a new
   sub-namespace with the byte-equality tests running against both
   serializers (349 Json tests, including 52 explicit null-handling
   cases). No downstream changes; the converters are addressable via
   `FableConverters.create()`. Default unchanged. Dependency-free in
   terms of breaking changes.
2. **PR #2 — `Fable.Remoting.Server` + sibling adapters opt-in plumbing.**
   Adds `JsonSerializerBackend` DU, threads it through `Server.Proxy.fs`
   + the six sibling adapters' response helpers. Plus the
   Giraffe / Suave / Falco STJ HTTP integration tests (49 tests). Default
   stays Newtonsoft.
3. **PR #3 — `Fable.Remoting.DotnetClient` opt-in + default flip + Newtonsoft
   `[<Obsolete>]` + MIGRATION.md.** Adds `withSerializerOptions` to
   DotnetClient. Flips `createApi()` default to STJ. Marks `FableJsonConverter`
   and `withNewtonsoftJson` `[<Obsolete>]`. The legacy-canary integration
   tests land here (they require the `withNewtonsoftJson` helper from
   PR #2 to compile).

I have a slight preference for the three-PR stack — easier review,
easier rollback if anything surprises us, and PR #1 plus the two bug
fixes deliver real value even if you decide not to merge the rest. But
it's your call.

## Known follow-ups (not part of any of the PRs above)

- **`Fable.Remoting.AspNetCore`, `Fable.Remoting.AwsLambda`,
  `Fable.Remoting.AzureFunctions.Worker` HTTP integration tests.** The
  adapter code in all three is plumbed for STJ; identical
  `setBody`-with-backend pattern as Giraffe / Suave / Falco. Existing
  Phase 4b/4d tests cover the pattern by proxy. Per-adapter
  belt-and-braces tests would be welcome but each needs its own test
  project (or, for AzureFunctions, a CI-friendly replacement for the
  manual-FunctionApp-on-localhost rig). Happy to do as a follow-up if
  it'd help.
- **Outer-array argument parsing.** Per-argument deserialisation is now
  fully backend-routed (the byte-compat hot path). The outer array
  slicing was also moved off `JToken` in Phase 4f — both backends now
  parse `[arg1, arg2, ...]` via their own JSON DOM
  (`JsonConvert.DeserializeObject<JToken>` for Newtonsoft;
  `JsonDocument.Parse` for STJ). So STJ consumers exercise zero
  Newtonsoft API at runtime today.
- **`MapNonStringKey` write-side perf.** The converter allocates a
  shared `MemoryStream` + `Utf8JsonWriter` per Map (amortised) and
  resets between keys. Could go further with `ArrayBufferWriter<byte>`
  pooling if it ever shows up in a benchmark; not on the critical path
  today.

## Sign-off request

Before I open the actual PR(s), wanted to check:

1. **Is the approach acceptable?** STJ becomes the default; Newtonsoft
   opt-in via `[<Obsolete>]`-marked helper for one major version; deletion
   in v5.0. Byte-equal wire format. Existing consumers see no behaviour
   change unless they touched `FableJsonConverter` directly.
2. **One PR or the three-PR stack?**
3. **Two pre-existing-bug fixes** — same PR with the rest, or split them
   into a small precursor PR so v4.x consumers benefit immediately?
4. **Any naming preferences?** I went with `FableConverters.create()`,
   `FableConverters.addTo`, `Remoting.withSerializerOptions`, and
   `Remoting.withNewtonsoftJson`. Happy to rename.
5. **`BYTE-COMPAT-MAP.md`** on the branch is a 1500-line working
   artefact documenting every Kind branch's wire shape, surprises caught
   during the port, and design rationale. It's useful as a reference for
   whoever maintains the converter set going forward, but it's verbose.
   `MIGRATION.md` is a leaner consumer-facing migration guide (~300
   lines). Want them both in the PR? Either trimmed? Or left off?

Branch is on a fork; happy to share the link when you're ready to look. I
won't open the PR until we've agreed on the shape.

Thanks for `Fable.Remoting` — it's been one of the most enjoyable
libraries to use in F#-land for years.
