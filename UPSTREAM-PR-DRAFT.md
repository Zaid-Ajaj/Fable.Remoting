# Replace Newtonsoft.Json with System.Text.Json (byte-equal wire format)

Hi Zaid,

I had a couple of errors that arose from mixed use of Newtonsoft and
System.Text.Json so I asked Claude Code to write an update for
Fable.Remoting that would allow you to remove Newtonsoft from
Fable.Remoting altogether. Details are below — hope this is useful.

Cheers,
Andrew

---

## TL;DR

This PR ports the JSON serializer from Newtonsoft.Json to
System.Text.Json, **byte-equally** for every shape Fable.Remoting
produces today. STJ becomes the default; Newtonsoft is kept as an
explicit `[<Obsolete>]` opt-in for one major version, then deletable in
v5.0.

- Parallel STJ converter set in `Fable.Remoting.Json`.
- Backend choice plumbed through `Fable.Remoting.Server`, all six
  sibling adapters (Giraffe / Suave / Falco / AspNetCore / AwsLambda × 2
  / AzureFunctions.Worker), and `Fable.Remoting.DotnetClient`.
- `Remoting.createApi()` now defaults to STJ. Wire format is byte-equal
  to the previous Newtonsoft default — verified by 349 byte-pin tests
  running the same assertions against both serializers, plus 70+ HTTP
  integration tests covering Giraffe / Suave / Falco round-tripping
  representative shapes through real `TestServer`s wired to both
  backends.
- `FableJsonConverter` and a new `Remoting.withNewtonsoftJson`
  opt-back-in helper are `[<Obsolete>]` with migration pointers.
- v5.0 follow-up is pure deletion — no design decisions left. See
  [`MIGRATION.md`](MIGRATION.md) for the timeline.

Two pre-existing Newtonsoft bugs were caught and fixed as a side-effect:
`Map<K, V>` deserialise from `"null"` crashed; `[<Fable.Core.Pojo>]` and
`[<Fable.Core.StringEnum>]` were silently ignored on DUs with
field-bearing cases. Details below — happy to split those into a
precursor PR for v4.x consumers if you'd prefer.

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
BCL on `net8.0` (no new package reference needed). Every `Fable.Remoting.*`
consumer pulls Newtonsoft transitively today — projects going OSS-public,
running supply-chain audits, or just trying to minimise their dependency
graph inherit it without a way to opt out. This PR delivers the opt-out,
and lays the foundation for v5.0 to drop the Newtonsoft package reference
from `Fable.Remoting.Json` entirely.

The client side (`Fable.SimpleJson`) is already Newtonsoft-free, so the
entire PR is server-side.

## What landed

### `Fable.Remoting.Json` — parallel STJ converter set

New file `Fable.Remoting.Json/FableSystemTextJsonConverter.fs`
(~1000 lines). Every `Kind` branch the existing Newtonsoft
`FableJsonConverter` handles has a matching System.Text.Json converter:

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
  — the raw JSON text of each argument is backend-agnostic, so the STJ
  path doesn't touch `JToken` at runtime.

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
  `fableArgSerializer` with `DateParseHandling.None` and
  `DateTimeZoneHandling.RoundtripKind` to preserve as much of the
  original JToken-roundtrip semantics as possible. One known
  limitation: DateTimeOffset offset preservation through the Newtonsoft
  path is now fragile (see "Known follow-ups" below); the STJ default
  path preserves offsets correctly.

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

## Pre-existing Newtonsoft bugs caught + fixed

Surfaced by the byte-equality testing, since both bugs broke byte-compat
in ways the existing test suite never exercised:

**1. `Map<K, V>` deserialise from `"null"` crashes** with
`InvalidCastException` at `FableConverter.fs:669` — the
`Kind.MapWithStringKey` else-branch (array-of-pairs fallback) read
`serializer.Deserialize<JToken>(reader) :?> JArray` without a
`JsonToken.Null` guard. Bites any Fable client that ever sends `null`
for an `Option<Map<...>>` field. The STJ path doesn't share the bug
(STJ's default `HandleNull = false` returns null directly without
invoking the converter). Documented with an STJ-only test list in
`StjWireFormatTests.fs`.

**2. `[<Fable.Core.Pojo>]` / `[<Fable.Core.StringEnum>]` silently ignored on
DUs with field-bearing cases.** `getUnionKind` at
`FableConverter.fs:156-163` read attributes from the runtime
case-subtype (e.g. `PojoDU+PojoOne`), which doesn't inherit the
attribute from the declaring DU. Fixed by normalising to the declaring
type first. The STJ path was correct by construction — factories
dispatch on the declared static type.

Happy to split these into a small precursor PR if you want v4.x
consumers to benefit immediately without taking the whole STJ port.

## Tests

`Fable.Remoting.Json.Tests` (349 total):

- `WireFormatTests.fs` — 103 byte-equality tests + 26 dedicated
  null-handling tests + 12 Pojo/StringEnum byte-pin tests. The gallery
  is parameterised by an `ISerializer` abstraction; the same tests run
  against both serializers byte-for-byte.
- `StjWireFormatTests.fs` — STJ instantiation of the gallery.
- `StjUnionPrototypeTests.fs` — 23 STJ-specific reader tests covering
  the five input shapes.

`Fable.Remoting.Giraffe.Tests` / `Suave.Tests` / `Falco.Tests` each gain
TWO new files:

- `StjHttpIntegrationTests.fs` — STJ HTTP integration tests through real
  `TestServer` (Giraffe / Falco) or a live Suave listener.
- `LegacyNewtonsoftIntegrationTests.fs` — same shape, pinned to
  `withNewtonsoftJson`. Keeps the legacy path under automated coverage
  through the deprecation window. **When v5.0 deletes the Newtonsoft
  branch, this is the file in each adapter that retires alongside.**

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

`MIGRATION.md` includes a security note on
`UnsafeRelaxedJsonEscaping`'s non-escaping of HTML-sensitive characters
— same behaviour as the previous Newtonsoft default, but worth flagging
to anyone re-auditing their serialiser setup.

## Known follow-ups (deliberately out of scope)

- **DateTimeOffset offset preservation on the legacy Newtonsoft path.**
  Writing the legacy-canary tests surfaced that
  `Maybe<DateTimeOffset>` round-trips through `withNewtonsoftJson` lose
  the original offset (rewritten to the server's local TZ). The STJ
  path doesn't share the bug. Root cause appears to be in
  `FableJsonConverter`'s `Kind.Union` single-field-case branch — the
  inner `JTokenReader.CreateReader()` doesn't fully inherit
  `DateParseHandling.None` from the outer serializer. Documented in
  MIGRATION.md as a "migrate to STJ if you depend on this" item. v5.0's
  deletion of the Newtonsoft path makes the limitation moot.
- **Per-adapter HTTP integration tests for AspNetCore / AwsLambda /
  AzureFunctions.Worker.** Adapter code is plumbed; shape is identical
  to Giraffe / Suave / Falco. Existing tests cover the shape by proxy.
  AzureFunctions specifically needs a CI-friendly replacement for the
  manual-FunctionApp-on-localhost rig.
- **v5.0 deletion sweep.** When you flip the major version, this PR's
  contents enable deleting `Fable.Remoting.Json/FableConverter.fs`,
  dropping `Newtonsoft.Json` from `Fable.Remoting.Json/paket.references`,
  removing the `NewtonsoftJson` case from `JsonSerializerBackend`,
  removing `Remoting.withNewtonsoftJson`, and retiring every
  `LegacyNewtonsoftIntegrationTests.fs` plus the pre-existing
  Newtonsoft-only adapter test files. Pure deletion, no design
  decisions.

## Documentation in the PR

- [`MIGRATION.md`](MIGRATION.md) — consumer-facing migration guide
  (~350 lines). TL;DR for typical consumers, three migration paths by
  profile, security note on the encoder's HTML-sensitive-character
  behaviour, timeline for v4 → v5 retirement.
- [`BYTE-COMPAT-MAP.md`](BYTE-COMPAT-MAP.md) — internal working artefact
  documenting every Kind branch's wire shape, surprises caught during
  the port, design rationale. ~1700 lines, written during the work as
  both a navigation map and an empirical-findings log. Useful as a
  reference for whoever maintains the converter set going forward, but
  verbose. Happy to trim or drop from the PR if you'd prefer a leaner
  change.

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

## Diff stats

```
~38 files changed, ~5000 insertions(+), ~200 deletions(-)
```

Most of the insertions are documentation (`BYTE-COMPAT-MAP.md` +
`MIGRATION.md`) and the test gallery (parallel STJ runs of the byte-pin
gallery, plus the legacy-canary HTTP integration tests). The actual
converter code is ~1100 lines; sibling-adapter plumbing is single-line
changes per helper. The `FableConverter.fs` (Newtonsoft path) is
untouched except for two bug fixes (`getUnionKind` normalisation +
documenting the `[<Obsolete>]` rationale) and the deprecation
annotation.

---

Happy to split this into a stack of three smaller PRs if you'd rather
review in chunks (suggested split: Json package + bug fixes / Server +
sibling adapters / DotnetClient + default flip), or to drop any
specific piece. Just let me know what shape you'd prefer for review.

Signed-off-by: [your DCO line]
