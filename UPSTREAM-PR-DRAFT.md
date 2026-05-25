# Add opt-in System.Text.Json serializer to `Fable.Remoting.Json`

Refs the proposal in issue #NNN.

## Summary

Adds a parallel System.Text.Json converter set to `Fable.Remoting.Json` plus
opt-in plumbing through `Fable.Remoting.Server`. Newtonsoft remains the
default for every existing consumer — zero behaviour change unless a consumer
explicitly opts in. STJ output is byte-equal to the existing Newtonsoft wire
format across 103 representative shapes, verified by a parameterised test
gallery that runs the same assertions against both serializers.

```fsharp
// Existing consumers — no change required.
Remoting.createApi()
|> Remoting.fromValue myImpl
|> Remoting.buildHttpHandler

// Opting in to STJ — one new line.
open Fable.Remoting.Json.SystemTextJson

Remoting.createApi()
|> Remoting.fromValue myImpl
|> Remoting.withSerializerOptions (FableConverters.create())
|> Remoting.buildHttpHandler
```

## Motivation

`Newtonsoft.Json` is in maintenance mode; `System.Text.Json` is the modern
.NET default and ships in the BCL on `net8.0` (no new package reference).
Every `Fable.Remoting.*` consumer pulls Newtonsoft transitively today —
projects going OSS-public or running supply-chain audits inherit it without
a way to opt out. This PR adds the way out.

## What landed

### `Fable.Remoting.Json` — parallel STJ converter set

New file [`FableSystemTextJsonConverter.fs`](Fable.Remoting.Json/FableSystemTextJsonConverter.fs)
(~1000 lines). Every `Kind` branch the Newtonsoft `FableJsonConverter` handles
has a matching `System.Text.Json` converter:

| Newtonsoft `Kind` | STJ converter |
|---|---|
| `Kind.Union` | `FSharpUnionConverter<'T>` + factory |
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

- `DoubleConverter` — STJ's `WriteNumberValue(0.0)` emits `"0"`; Newtonsoft
  emits `"0.0"`. Converter restores the trailing `.0` for whole-valued
  doubles using `ToString("R", InvariantCulture)`.
- `StringConverter` — Newtonsoft emits high-codepoint codepoints (emoji etc.)
  as raw UTF-8 bytes. `JavaScriptEncoder.UnsafeRelaxedJsonEscaping` in STJ
  still escapes them to `\uXXXX\uXXXX` surrogate-pair escapes in practice
  (verified empirically; `JavaScriptEncoder.Create(UnicodeRanges.All)` is
  strictly worse — escapes `+` and inline `"`). The converter uses
  `Utf8JsonWriter.WriteRawValue` to bypass STJ's encoder entirely and emits
  only the RFC-8259-required escapes (`"`, `\`, control chars).

The factories register against `JsonSerializerOptions.Converters` in
specificity order (most-specific factories first). A `FableConverters` module
exposes the conventional registration helpers:

```fsharp
module FableConverters =
    val addTo : JsonSerializerOptions -> unit
    val create : unit -> JsonSerializerOptions
```

The `FSharpUnionConverter` reader handles all five input shapes the
Newtonsoft path accepts (per `FableConverter.fs:594-659`):

1. `JsonTokenType.Null` → default-of-T.
2. `String` → no-field case by name.
3. `StartObject` with `__typename` (union-of-records, case-insensitive match
   — preserves Newtonsoft's behaviour at `FableConverter.fs:624-632`).
4. `StartObject` with `{tag, name, fields}` (Fable runtime form).
5. `StartObject` single-property (writer round-trip — `{"<Case>": value-or-array}`).
6. `StartArray` (`["<Case>", <f>, ...]`).

### `Fable.Remoting.Server` — opt-in plumbing

Minimal additions to keep existing consumers untouched:

- **[`Types.fs`](Fable.Remoting.Server/Types.fs)** — new `JsonSerializerBackend` DU:
  ```fsharp
  type JsonSerializerBackend =
      | NewtonsoftJson
      | SystemTextJson of System.Text.Json.JsonSerializerOptions
  ```
  Plus a new `JsonSerializer` field on `RemotingOptions<'context, 'serverImpl>`
  and on the internal `MakeEndpointProps` record.
- **[`Remoting.fs`](Fable.Remoting.Server/Remoting.fs)** — `createApi()`
  defaults `JsonSerializer = NewtonsoftJson`; new `Remoting.withSerializerOptions`
  fluent helper.
- **[`Proxy.fs`](Fable.Remoting.Server/Proxy.fs)** — new
  `jsonSerializeWithBackend` helper (now `public` so sibling adapters can
  consume it) that branches between the existing `jsonSerialize`
  (Newtonsoft) and `JsonSerializer.Serialize<'a>(stream, value, stjOptions)`.
  Threaded through `makeApiProxy → makeEndpointProxy`. Per-argument
  deserialisation also branches on backend.

The outer JSON-array parsing still routes through Newtonsoft (`JTokenarray`
slicing) — generalising it would require abstracting
`InvocationPropsInt.Arguments` over the JSON DOM, which is a bigger refactor
and isn't on the byte-compat hot path. Per-argument and response shapes are
both backend-routed when opted in.

### Sibling adapters — `setBody` helpers backend-aware

Six adapters had a parallel `setBody`-shape helper (`setJsonBody` /
`setResponseBody` / etc.) that called the Newtonsoft `jsonSerialize`
directly, bypassing the backend choice for **error responses** and the
**docs schema `OPTIONS /$schema` endpoint**. Each now takes a
`JsonSerializerBackend` parameter routed from `options.JsonSerializer` at
the `fail` entry point:

- [`Fable.Remoting.Giraffe/FableGiraffeAdapter.fs`](Fable.Remoting.Giraffe/FableGiraffeAdapter.fs)
- [`Fable.Remoting.Suave/FableSuaveAdapter.fs`](Fable.Remoting.Suave/FableSuaveAdapter.fs)
- [`Fable.Remoting.Falco/FableFalcoAdapter.fs`](Fable.Remoting.Falco/FableFalcoAdapter.fs)
- [`Fable.Remoting.AspNetCore/Middleware.fs`](Fable.Remoting.AspNetCore/Middleware.fs)
- [`Fable.Remoting.AwsLambda/FableLambdaAdapter.fs`](Fable.Remoting.AwsLambda/FableLambdaAdapter.fs)
- [`Fable.Remoting.AwsLambda/FableLambdaApiGatewayAdapter.fs`](Fable.Remoting.AwsLambda/FableLambdaApiGatewayAdapter.fs)
- [`Fable.Remoting.AzureFunctions.Worker/FableAzureFunctionsAdapter.fs`](Fable.Remoting.AzureFunctions.Worker/FableAzureFunctionsAdapter.fs)

### `Fable.Remoting.DotnetClient` — parallel opt-in surface

The .NET-side client package gets its own `withSerializerOptions` opt-in
on two surfaces:

- **`Proxy<'t>.WithSerializerOptions(opts: JsonSerializerOptions) : Proxy<'t>`** —
  builder member on the constructor-style API.
- **`Remoting.withSerializerOptions opts options`** — fluent helper on the
  `Remoting.createApi → buildProxy` path.

Internals: `RemoteBuilderOptions` gains a `StjOptions: JsonSerializerOptions
option` field. The 14 internal `ServiceCallerFuncN` types (covering
`Func2..Func9` plus `FuncTask2..9` plus `ParameterlessServiceCall`) each
thread `stjOptions` through their constructor and into the
`Proxy.proxyPost`/`proxyPostTask` calls. `buildProxy`'s reflective
`Activator.CreateInstance` and static-method `Invoke` call sites pass the
new arg through.

### Tests

The byte-compat test gallery is the contract. `Fable.Remoting.Json.Tests`
gains three new files:

- [`WireFormatTests.fs`](Fable.Remoting.Json.Tests/WireFormatTests.fs) — 103
  byte-equality tests pinning the exact Newtonsoft wire format for primitives,
  options, lists, tuples, records (including non-alphabetical field order),
  DUs (including recursive + generic), maps (string-key + non-string-key +
  Guid-key + tuple-key + DU-key), sets, DateTime (UTC + Unspecified + Local
  Kinds), TimeSpan, DateTimeOffset, and combinations. Plus 26 dedicated
  null-handling tests covering both serialise and deserialise directions.
- [`StjWireFormatTests.fs`](Fable.Remoting.Json.Tests/StjWireFormatTests.fs) —
  runs the same gallery through the STJ serializer for byte-equal output.
- [`StjUnionPrototypeTests.fs`](Fable.Remoting.Json.Tests/StjUnionPrototypeTests.fs) —
  23 STJ-specific reader tests covering the five input shapes.

The gallery is parameterised by an `ISerializer` interface so both serializers
share one source of truth.

HTTP integration tests across three adapters — 49 end-to-end round-trips
through real `TestServer` / Suave-listener instances wired with
`Remoting.withSerializerOptions (FableConverters.create())`:

- [`Fable.Remoting.Giraffe.Tests/StjHttpIntegrationTests.fs`](Fable.Remoting.Giraffe.Tests/StjHttpIntegrationTests.fs) — 18 tests via Giraffe + ASP.NET `TestServer`.
- [`Fable.Remoting.Suave.Tests/StjHttpIntegrationTests.fs`](Fable.Remoting.Suave.Tests/StjHttpIntegrationTests.fs) — 13 tests via real Suave listener on a localhost port.
- [`Fable.Remoting.Falco.Tests/StjHttpIntegrationTests.fs`](Fable.Remoting.Falco.Tests/StjHttpIntegrationTests.fs) — 18 tests via Falco + ASP.NET `TestServer` + DotnetClient.Proxy with STJ opt-in (exercises both ends of the wire in one round-trip).

**Test totals on this branch:**

| Project | Count |
|---|---|
| `Fable.Remoting.Json.Tests` | 337 (50 pre-existing + 287 new) |
| `Fable.Remoting.Server.Tests` | 30 (unchanged) |
| `Fable.Remoting.MsgPack.Tests` | 55 (unchanged) |
| `Fable.Remoting.Suave.Tests` | 41 (28 pre-existing + 13 new STJ HTTP) |
| `Fable.Remoting.Giraffe.Tests` | 114 (96 pre-existing + 18 new STJ HTTP) |
| `Fable.Remoting.Falco.Tests` | 95 (77 pre-existing + 18 new STJ HTTP) |
| **Total** | **672 ✅** |

### Diff stats

```
31 files changed, 4030 insertions(+), 163 deletions(-)
```

(Of those insertions, ~1300 lines are `BYTE-COMPAT-MAP.md` — a working
artefact documenting every Kind branch's wire shape, surprises caught
during the port, and design rationale. See note in "Documentation" below.)

The deletions are entirely from the `ISerializer` refactor in
`WireFormatTests.fs` (extracting the gallery into a function so it runs
against both serializers) and the `setBody` signature changes in the six
sibling adapters (each helper grew a `JsonSerializerBackend` parameter and
the `fail` entry threads it down). No behaviour change to any pre-existing
file's wire format. The `FableConverter.fs` (Newtonsoft path) is untouched.

## Migration story for consumers

Consumers who do nothing continue to use Newtonsoft. No code change required.

Consumers who want to opt in:

1. Add `open Fable.Remoting.Json.SystemTextJson` to the file that builds the API.
2. Add `|> Remoting.withSerializerOptions (FableConverters.create())` to the
   `Remoting.createApi()` pipeline.
3. Done. Wire format is byte-equal; Fable clients see no difference.

To pass a custom-configured `JsonSerializerOptions`:

```fsharp
let myOptions = JsonSerializerOptions()
FableConverters.addTo myOptions  // register the converter set
myOptions.WriteIndented <- true  // or whatever else
...
Remoting.withSerializerOptions myOptions
```

## Unintentional improvement: pre-existing Newtonsoft null bug

`JsonConvert.DeserializeObject<Map<string,int>>("null", FableJsonConverter())`
crashes with `InvalidCastException` against the existing Newtonsoft converter.
Crash site is [`FableConverter.fs:669`](Fable.Remoting.Json/FableConverter.fs#L669) —
the `Kind.MapWithStringKey` else-branch (array-of-pairs fallback) reads
`serializer.Deserialize<JToken>(reader) :?> JArray` without a `JsonToken.Null`
guard. `JValue(null)` can't cast to `JArray` → crash.

The STJ path doesn't share the bug — STJ's default `HandleNull = false`
returns null directly for ref-typed converters without invoking the converter.
A test list `stjFixesNewtonsoftNullBug` in
[`StjWireFormatTests.fs`](Fable.Remoting.Json.Tests/StjWireFormatTests.fs)
documents the fix. Happy to also patch the Newtonsoft branch in this PR if
you'd prefer — one-line null guard. Otherwise it can be a separate small PR.

## Out of scope (deferred follow-ups)

- **`[<Fable.Core.Pojo>]` and `[<Fable.Core.StringEnum>]` DU dispatch.** The
  Newtonsoft `FableJsonConverter` has explicit branches for these. No test
  fixtures or client-emitted output to byte-match against in the existing
  suite — would land as a follow-up once we agree on fixture shapes.
- **Outer-array argument parsing.** `InvocationPropsInt.Arguments` is still
  `Choice<byte[], JToken> list`; generalising it over the JSON DOM is a
  separate refactor (per-argument deserialisation IS backend-routed; only
  the outer slicing routes through Newtonsoft regardless of backend).
- **Belt-and-braces HTTP integration tests for AspNetCore / AwsLambda /
  AzureFunctions.Worker.** The adapter code is plumbed; the shape is
  identical to Giraffe / Suave / Falco. The existing tests cover the shape
  by proxy. A maintainer who wants per-adapter coverage can add equivalent
  test files in a small follow-up.

## Documentation

[`BYTE-COMPAT-MAP.md`](BYTE-COMPAT-MAP.md) on the branch root is a working
artefact documenting every `Kind` branch's wire shape, the surprises caught
during the port, and design rationale. ~1300 lines, written during the
work as both a navigation map and an empirical-findings log. Useful as a
reference for whoever maintains the converter set going forward, but can be
trimmed or dropped from the PR if you'd prefer a leaner change.

## Testing locally

```bash
# Run the byte-compat matrix (337 tests)
dotnet run --project Fable.Remoting.Json.Tests/Fable.Remoting.Json.Tests.fsproj

# Run the HTTP integration tests (114 tests; 18 of those are STJ end-to-end)
dotnet run --project Fable.Remoting.Giraffe.Tests/Fable.Remoting.Giraffe.Tests.fsproj

# Or run the full suite per project (Server / MsgPack / Suave / Falco)
dotnet run --project Fable.Remoting.<Project>.Tests/Fable.Remoting.<Project>.Tests.fsproj
```

The suites are Expecto console runners (`<OutputType>Exe</OutputType>`); use
`dotnet run`, not `dotnet test` (which silently exits zero).

---

Signed-off-by: [operator's git signing line]
