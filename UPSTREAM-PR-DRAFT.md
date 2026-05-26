# Replace Newtonsoft.Json with System.Text.Json (byte-equal wire format)

Refs the proposal in issue #NNN.

## Summary

Ports `Fable.Remoting`'s JSON serializer from Newtonsoft.Json to
System.Text.Json. STJ becomes the **default**; Newtonsoft is kept as an
explicit `[<Obsolete>]` opt-in for one major version, then deletable in
v5.0.

The wire format is **byte-equal** between the two serializers across the
entire test matrix — 349 byte-pin tests in `Fable.Remoting.Json.Tests`
running the same assertions against both serializers, plus 70+ HTTP
integration tests across Giraffe / Suave / Falco round-tripping
representative shapes through `TestServer`s wired to STJ (and a parallel
set wired to the legacy `withNewtonsoftJson` opt-in). Existing Fable /
DotnetClient clients see no change in the bytes they read on the wire.

```fsharp
// Existing consumers — no change required (wire format byte-equal).
Remoting.createApi()
|> Remoting.fromValue myImpl
|> Remoting.buildHttpHandler

// Pin to the legacy Newtonsoft path during migration (deprecation warning).
Remoting.createApi()
|> Remoting.withNewtonsoftJson    // [<Obsolete>]
|> Remoting.fromValue myImpl
|> Remoting.buildHttpHandler
```

## Motivation

`Newtonsoft.Json` is in maintenance mode; `System.Text.Json` ships in the
BCL on `net8.0` (no new package reference). Every `Fable.Remoting.*`
consumer pulls Newtonsoft transitively today — projects going OSS-public
or running supply-chain audits inherit it without a way to opt out. This
PR delivers the opt-out, and lays the foundation for v5.0 to drop the
Newtonsoft package reference from `Fable.Remoting.Json` entirely.

## What landed

### `Fable.Remoting.Json` — parallel STJ converter set

New file `Fable.Remoting.Json/FableSystemTextJsonConverter.fs`
(~1000 lines). Every `Kind` branch the Newtonsoft `FableJsonConverter`
handles has a matching `System.Text.Json` converter:

| Newtonsoft `Kind` | STJ converter |
|---|---|
| `Kind.Union` | `FSharpUnionConverter<'T>` + factory |
| `Kind.PojoDU` | `FSharpPojoDUConverter<'T>` + factory |
| `Kind.StringEnum` | `FSharpStringEnumConverter<'T>` + factory |
| `Kind.Option` | `FSharpOptionConverter<'T>` + factory |
| `Kind.Tuple` | `FSharpTupleConverter<'T>` + factory |
| `Kind.Other` (plain records) | `FSharpRecordConverter<'T>` + factory |
| `Kind.MutableRecord` | `FSharpCliMutableRecordConverter<'T>` + factory |
| `Kind.MapWithStringKey` | `FSharpMapStringKeyConverter<'V>` + factory |
| `Kind.MapOrDictWithNonStringKey` | `FSharpMapNonStringKeyConverter<'K,'V>` + factory |
| `Kind.Long` (`int64`) | `Int64Converter` |
| `Kind.Long` (`uint64`) | `UInt64Converter` |
| `Kind.BigInt` | `BigIntConverter` |
| `Kind.DateTime` | `DateTimeConverter` (three-way Kind branching preserved) |
| `Kind.TimeSpan` | `TimeSpanConverter` |
| `Kind.DateOnly` | `DateOnlyConverter` |
| `Kind.TimeOnly` | `TimeOnlyConverter` |
| `Kind.DataTable` / `Kind.DataSet` | `DataTableConverter` / `DataSetConverter` |
| `Kind.Other` (sets) | `FSharpSetConverter<'T>` + factory |
| `Kind.Other` (lists) | `FSharpListConverter<'T>` + factory |

Plus two converters that don't have a direct `Kind` analogue — both are
byte-compat workarounds for places STJ defaults diverge from Newtonsoft:

- `DoubleConverter` — STJ's `WriteNumberValue(0.0)` emits `"0"`;
  Newtonsoft emits `"0.0"`. Converter restores the trailing `.0` for
  whole-valued doubles using `ToString("R")` + appended ".0" when no
  decimal/exponent is present.
- `StringConverter` — Newtonsoft emits high-codepoint codepoints (emoji
  etc.) as raw UTF-8 bytes. `JavaScriptEncoder.UnsafeRelaxedJsonEscaping`
  in STJ still escapes them to `\uXXXX\uXXXX` surrogate-pair escapes in
  practice (verified empirically). The converter uses
  `Utf8JsonWriter.WriteRawValue` to bypass STJ's encoder entirely and
  emits only the RFC-8259-required escapes (`"`, `\`, control chars).

A `FableConverters` module exposes the conventional registration helpers:

```fsharp
module FableConverters =
    val addTo : JsonSerializerOptions -> unit
    val create : unit -> JsonSerializerOptions
```

`addTo` validates that the options instance isn't already in use (STJ
freezes options after first serialize call). `create` returns a fresh,
fully-configured instance.

The `FSharpUnionConverter` reader handles all five input shapes the
Newtonsoft path supports (per `FableConverter.fs:594-659`):

1. `JsonTokenType.Null` → default-of-T.
2. `String` → no-field case by name.
3. `StartObject` with `__typename` (union-of-records, case-insensitive).
4. `StartObject` with `{tag, name, fields}` (Fable runtime form).
5. `StartObject` single-property (writer round-trip — `{"<Case>": value-or-array}`).
6. `StartArray` (`["<Case>", <f>, ...]`).

### `Fable.Remoting.Server` — opt-in plumbing + default flip

- **`Types.fs`** — new `JsonSerializerBackend` DU:
  ```fsharp
  type JsonSerializerBackend =
      | NewtonsoftJson
      | SystemTextJson of System.Text.Json.JsonSerializerOptions
  ```
  Plus a new `JsonSerializer` field on `RemotingOptions<'context, 'serverImpl>`
  and on the internal `MakeEndpointProps` record. `InvocationPropsInt.Arguments`
  changed from `Choice<byte[], JToken> list` to `Choice<byte[], string> list`
  — the raw JSON text of each argument is backend-agnostic.

- **`Remoting.fs`** — `createApi()` defaults to `SystemTextJson` with a
  cached module-level `defaultStjOptions` (one `FableConverters.create()`
  instance reused across all `createApi()` calls); new
  `Remoting.withNewtonsoftJson` `[<Obsolete>]` fluent helper for the
  legacy opt-in; existing `Remoting.withSerializerOptions` accepts a
  custom `JsonSerializerOptions`.

- **`Proxy.fs`** — `jsonSerializeWithBackend` (public, so adapters can
  consume), `parseArgumentArray` (outer-array slicing branched on
  backend), `deserialiseArgWithBackend` (per-argument deserialise
  branched on backend). The Newtonsoft per-arg path uses a dedicated
  `newtonsoftArgSettings` with `DateParseHandling.None` to preserve
  DateTimeOffset offsets — caught and fixed during testing.

### Sibling adapters — `setBody` helpers backend-aware

Six adapters had a parallel `setBody`-shape helper (`setJsonBody` /
`setResponseBody` / etc.) that called the Newtonsoft `jsonSerialize`
directly, bypassing the backend choice for **error responses** and the
**docs schema `OPTIONS /$schema` endpoint**. Each now takes a
`JsonSerializerBackend` parameter routed from `options.JsonSerializer`:

- `Fable.Remoting.Giraffe/FableGiraffeAdapter.fs`
- `Fable.Remoting.Suave/FableSuaveAdapter.fs`
- `Fable.Remoting.Falco/FableFalcoAdapter.fs`
- `Fable.Remoting.AspNetCore/Middleware.fs`
- `Fable.Remoting.AwsLambda/FableLambdaAdapter.fs`
- `Fable.Remoting.AwsLambda/FableLambdaApiGatewayAdapter.fs`
- `Fable.Remoting.AzureFunctions.Worker/FableAzureFunctionsAdapter.fs`

### `Fable.Remoting.DotnetClient` — parallel opt-in surface

- `Proxy<'t>.WithSerializerOptions(opts: JsonSerializerOptions) : Proxy<'t>`
  — builder member on the constructor-style API.
- `Remoting.withSerializerOptions opts options` — fluent helper on the
  `Remoting.createApi → buildProxy` path.
- `RemoteBuilderOptions` gains a `StjOptions: JsonSerializerOptions option`
  field. 14 internal `ServiceCallerFuncN` types thread `stjOptions`
  through their constructors and into `Proxy.proxyPost`/`proxyPostTask`
  calls.

### Deprecation surface

- `[<Obsolete>]` on `Fable.Remoting.Json.FableJsonConverter` (the legacy
  converter class).
- `[<Obsolete>]` on `Fable.Remoting.Server.Remoting.withNewtonsoftJson`.
- Internal usages of `FableJsonConverter` (the implementations of the
  legacy path that remain supported through the deprecation window) are
  guarded with `#nowarn "44"` in `Server.Proxy`, `Server.Documentation`,
  `DotnetClient.Proxy`. Test files that intentionally exercise the
  legacy path are similarly annotated with a header comment explaining
  why.

### Pre-existing Newtonsoft bugs caught + fixed

Surfaced by the byte-equality testing, since both bugs broke byte-compat
in ways the existing test suite never exercised:

1. **`Map<K, V>` deserialise from `"null"` crashes** with
   `InvalidCastException` at `FableConverter.fs:669` — the
   `Kind.MapWithStringKey` else-branch (array-of-pairs fallback) read
   `serializer.Deserialize<JToken>(reader) :?> JArray` without a
   `JsonToken.Null` guard. The STJ path doesn't share the bug; documented
   with an STJ-only test list in `StjWireFormatTests.fs`.
2. **`[<Fable.Core.Pojo>]` and `[<Fable.Core.StringEnum>]` silently ignored
   on DUs with field-bearing cases.** `getUnionKind` at
   `FableConverter.fs:156-163` read attributes from the runtime
   case-subtype (e.g. `PojoDU+PojoOne`), which doesn't inherit the
   attribute. Fixed by normalising to the declaring type first. The
   STJ path was correct by construction.

If you want these bug fixes in a separate small precursor PR (so v4.x
consumers benefit immediately without taking the full STJ port), happy
to split them out.

### Tests

`Fable.Remoting.Json.Tests` (349 total):

- `WireFormatTests.fs` — 103 byte-equality tests + 26 dedicated
  null-handling tests + 12 Pojo/StringEnum byte-pin tests. The gallery
  is parameterised by an `ISerializer` abstraction; the same tests run
  against both serializers.
- `StjWireFormatTests.fs` — STJ instantiation of the gallery.
- `StjUnionPrototypeTests.fs` — 23 STJ-specific reader tests covering
  the five input shapes.

`Fable.Remoting.Giraffe.Tests` / `Suave.Tests` / `Falco.Tests` each gain
TWO new files:

- `StjHttpIntegrationTests.fs` — STJ HTTP integration tests through real
  `TestServer` (Giraffe / Falco) or a live Suave listener.
- `LegacyNewtonsoftIntegrationTests.fs` — same shape, pinned to
  `withNewtonsoftJson`. Includes a DateTimeOffset round-trip "canary"
  test that surfaced a real per-argument `DateParseHandling.None`
  regression during the port. **Proves the legacy path stays operational
  through the deprecation window — when v5.0 deletes the Newtonsoft
  branch, this is the file that should retire alongside.**

**Test totals on this branch:**

| Project | Count |
|---|---|
| `Fable.Remoting.Json.Tests` | 349 (50 pre-existing + 299 new) |
| `Fable.Remoting.Server.Tests` | 30 (unchanged) |
| `Fable.Remoting.MsgPack.Tests` | 55 (unchanged) |
| `Fable.Remoting.Suave.Tests` | 48 (28 legacy + 13 STJ + 7 legacy-canary) |
| `Fable.Remoting.Giraffe.Tests` | 120 (96 legacy + 18 STJ + 6 legacy-canary) |
| `Fable.Remoting.Falco.Tests` | 102 (77 legacy + 18 STJ + 7 legacy-canary) |
| **Total** | **704 ✅** |

## Migration story for consumers

See [`MIGRATION.md`](MIGRATION.md) at the repo root for the full guide.
The TL;DR:

1. **Most consumers do nothing.** Wire format is byte-equal; existing
   Fable / DotnetClient clients receive the same bytes.
2. **Consumers cautious about the flip** can pin to the legacy path
   during their migration window via `|> Remoting.withNewtonsoftJson`
   (deprecation warning fires).
3. **Consumers who reference `FableJsonConverter` directly** (e.g. for
   custom SSE serialisation) swap to `FableConverters.create()` from the
   `Fable.Remoting.Json.SystemTextJson` namespace.

## Out of scope (deferred follow-ups)

- **Per-adapter HTTP integration tests for AspNetCore / AwsLambda /
  AzureFunctions.Worker.** Adapter code is plumbed; the shape is
  identical to Giraffe / Suave / Falco. The existing tests cover the
  shape by proxy. Per-adapter coverage could land as a small follow-up;
  AzureFunctions in particular needs a CI-friendly replacement for the
  current manual-FunctionApp-on-localhost rig.
- **v5.0 deletion sweep.** When you flip the major version, this PR's
  contents enable deleting `Fable.Remoting.Json/FableConverter.fs`,
  dropping `Newtonsoft.Json` from `Fable.Remoting.Json/paket.references`,
  removing the `NewtonsoftJson` case from `JsonSerializerBackend`,
  removing `Remoting.withNewtonsoftJson`, and retiring every test file
  that pins legacy behaviour. ~hundreds of lines of pure deletion with
  no design decisions.

## Documentation

- [`MIGRATION.md`](MIGRATION.md) — consumer-facing migration guide
  (~350 lines). TL;DR for typical consumers, three migration paths by
  profile, security note on `UnsafeRelaxedJsonEscaping`'s
  HTML-sensitive-character behaviour, timeline for v4 → v5 retirement.
- [`BYTE-COMPAT-MAP.md`](BYTE-COMPAT-MAP.md) — internal working artefact
  documenting every Kind branch's wire shape, surprises caught during
  the port, design rationale. ~1500 lines, written during the work as
  both a navigation map and an empirical-findings log. Useful as a
  reference for whoever maintains the converter set going forward, but
  verbose. Can be trimmed or dropped from the PR if you'd prefer a
  leaner change.

## Testing locally

```bash
# Byte-compat matrix (349 tests, both serializers in parallel)
dotnet run --project Fable.Remoting.Json.Tests/Fable.Remoting.Json.Tests.fsproj

# Full HTTP integration tests for Giraffe / Suave / Falco
dotnet run --project Fable.Remoting.Giraffe.Tests/Fable.Remoting.Giraffe.Tests.fsproj
dotnet run --project Fable.Remoting.Suave.Tests/Fable.Remoting.Suave.Tests.fsproj
dotnet run --project Fable.Remoting.Falco.Tests/Fable.Remoting.Falco.Tests.fsproj

# Or run any other Tests project the same way.
```

The suites are Expecto console runners (`<OutputType>Exe</OutputType>`);
use `dotnet run`, not `dotnet test` (which silently exits zero — known
upstream pattern).

### Diff stats

```
~35 files changed, ~4500 insertions(+), ~180 deletions(-)
```

Most of the insertions are documentation (`BYTE-COMPAT-MAP.md` +
`MIGRATION.md`) and the test gallery. The actual converter code is
~1000 lines; sibling-adapter plumbing is single-line changes per
helper. The `FableConverter.fs` (Newtonsoft path) is untouched except
for two bug fixes (`getUnionKind` normalisation) and the `[<Obsolete>]`
annotation.

---

Signed-off-by: [operator's git signing line]
