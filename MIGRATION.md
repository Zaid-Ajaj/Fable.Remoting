# Migrating to the System.Text.Json default — and toward retiring Newtonsoft

`Fable.Remoting`'s default JSON serializer is now **System.Text.Json**. The
previous default — Newtonsoft.Json via the `FableJsonConverter` —
remains available as an opt-in path for one more major version, then will
be removed.

The wire format is **byte-equal** between the two serializers. 349
byte-equality tests + 70+ HTTP integration tests in this repo prove every
representative F# shape (records, DUs including Pojo / StringEnum, options,
lists, tuples, Maps with arbitrary keys, Sets, BigInt, DateTime in every
`Kind`, DateTimeOffset, Result, byte[], and null cases) round-trips the
same bytes through both serializers. Existing Fable clients (via
`Fable.SimpleJson`) and .NET clients (via `Fable.Remoting.DotnetClient`)
see no change in the bytes they read on the wire.

---

## TL;DR — Most consumers do nothing

If you're a typical consumer who does:

```fsharp
open Fable.Remoting.Server

let webApp =
    Remoting.createApi()
    |> Remoting.fromValue myImpl
    |> Remoting.buildHttpHandler
```

…then **you don't need to change anything**. After the upgrade, `createApi()`
defaults to System.Text.Json with the byte-compatible converter set
pre-registered. Your clients receive the same bytes; your tests pass; your
app boots cleanly.

You'll see deprecation warnings if you reference `FableJsonConverter` (the
legacy Newtonsoft converter) directly — replace those references with
`Fable.Remoting.Json.SystemTextJson.FableConverters.create()` to clear the
warning. The legacy converter still works for the duration of this major
version.

---

## Three migration paths, by consumer profile

### 1. "I want the new default. Nothing in my codebase touches the converter directly."

**Action: none.** Upgrade to the new version. Your wire format is byte-equal
and your `createApi()` already returns options pre-configured with the
STJ converter set.

If you also want to **drop Newtonsoft from your deployed binaries**
(important for OSS releases, supply-chain audits, etc.): in this major
version, Newtonsoft.Json is still a transitive dep of `Fable.Remoting.Json`.
You can't fully drop it from your binaries yet — it sits in your `bin/`
unused. **In the next major version**, `Fable.Remoting.Json` will drop the
Newtonsoft package reference entirely and Newtonsoft will vanish from your
deployment tree.

### 2. "I'm cautious — I want to verify the byte-equal claim on my own protocol before flipping."

**Action: opt out, run tests, opt back in.**

```fsharp
open Fable.Remoting.Server

let webApp =
    Remoting.createApi()
    |> Remoting.withNewtonsoftJson   // <-- explicit opt-back-in
    |> Remoting.fromValue myImpl
    |> Remoting.buildHttpHandler
```

`Remoting.withNewtonsoftJson` is `[<Obsolete>]` — it will be removed in the
next major version. Use it only during your migration window. Compare wire
output against your STJ deployment (with the helper removed) to verify
byte-equality on your domain types, then delete the line.

### 3. "I touch `FableJsonConverter` directly in my own code."

This happens in three places we know about:

- Custom JSON middleware that registers `FableJsonConverter` into a
  hand-rolled `JsonSerializerSettings`.
- Server-Sent Events (SSE) endpoints that use `FableJsonConverter` to
  serialize event bodies. (The ToolUp SDK's CLAUDE.md explicitly calls
  this out as the required converter — that guidance now updates to
  `FableConverters.create()`.)
- Tests that pin Newtonsoft-specific wire output for regression.

**Migration**:

```fsharp
// Before
open Fable.Remoting.Json
let converter = FableJsonConverter()
let settings = JsonSerializerSettings()
settings.Converters.Add converter
JsonConvert.SerializeObject(value, settings)

// After
open Fable.Remoting.Json.SystemTextJson
let options = FableConverters.create()
System.Text.Json.JsonSerializer.Serialize(value, options)
```

Or if you have settings you can't or don't want to recreate yet, use the
opt-back-in helper from Section 2.

---

## What's actually under the hood

The new default is functionally equivalent to:

```fsharp
let webApp =
    Remoting.createApi()
    |> Remoting.withSerializerOptions (FableConverters.create())
    |> Remoting.fromValue myImpl
```

`FableConverters.create()` returns a `JsonSerializerOptions` pre-configured
with:

- `Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping`
- A converter set that mirrors every wire shape `FableJsonConverter`
  produces:
  - F# discriminated unions (`{"<Case>": ...}` shapes — regular, plus
    `[<Pojo>]` and `[<StringEnum>]` variants).
  - F# records (declaration-order field emission).
  - F# `CLIMutable` records (omits null-valued properties).
  - F# options (`Some x` → JSON of `x`, `None` → `null`).
  - F# tuples (JSON array of typed elements).
  - F# lists / sets / maps (with the escaped-quote-property-name quirk
    on non-string-keyed maps preserved byte-for-byte).
  - Int64 (`"+N"` string with leading sign), UInt64 (`"N"` string),
    BigInt, DateTime (three-way Kind handling), TimeSpan, DateOnly,
    TimeOnly, DataTable, DataSet.
  - String (raw UTF-8 passthrough for supplementary-plane codepoints via
    `Utf8JsonWriter.WriteRawValue` — a byte-compat workaround for STJ's
    default encoder behaviour with surrogate pairs).
  - Double (forces trailing `.0` for whole-valued doubles — STJ's
    default emits `"0"` for `0.0`; Newtonsoft emits `"0.0"`).

The same converter set is also exposed by:

- `Fable.Remoting.DotnetClient.Remoting.withSerializerOptions` (the
  client-side fluent helper).
- `Fable.Remoting.DotnetClient.Proxy<'t>.WithSerializerOptions(opts)`
  (the constructor-style API).

---

## Surprises this work surfaced (worth knowing about)

Two pre-existing **bugs** in the legacy Newtonsoft path were fixed as part of
this work, because they prevented byte-equality testing:

1. **`JsonConvert.DeserializeObject<Map<string, int>>("null", FableJsonConverter())`
   crashed with `InvalidCastException`** — the `Kind.MapWithStringKey`
   else-branch tried to cast `JValue(null)` to `JArray` without a null
   guard. Now returns null cleanly. (STJ default behavior is correct here
   without any work; the test in `StjWireFormatTests.fs`'s
   `stjFixesNewtonsoftNullBug` block documents the fix.)

2. **`[<Fable.Core.Pojo>]` / `[<Fable.Core.StringEnum>]` were silently
   ignored on DUs with field-bearing cases** — `getUnionKind` read
   attributes from the case-subtype's `value.GetType()` instead of the
   declaring DU type. The case subtype doesn't inherit the attribute, so
   the lookup always missed. Now normalises to the declaring type before
   reading. (STJ path was correct by construction — factories dispatch on
   declared static type.)

If your Newtonsoft tests passed under the buggy behavior, the fix is
**byte-format-aligning** — your STJ deployment will get the correct shape,
and re-running the same tests against your new build will surface the
shape difference where it should have always been correct.

---

## Timeline for full Newtonsoft retirement

- **This major version (v4.x)** — STJ is the default. Newtonsoft is
  available as an explicit `[<Obsolete>]` opt-in via
  `Remoting.withNewtonsoftJson`. `FableJsonConverter` is `[<Obsolete>]`.
  Both paths build and pass tests. Newtonsoft is still a transitive
  package reference for `Fable.Remoting.Json` and a runtime fallback for
  consumers who explicitly opt back into it.

- **Next major version (v5.0)** — `FableJsonConverter`, the legacy
  `Newtonsoft.Json` package reference on `Fable.Remoting.Json`, and the
  `NewtonsoftJson` case of `JsonSerializerBackend` are removed. The
  `withNewtonsoftJson` helper is removed. Consumers who haven't migrated
  pin to a previous version or finish the migration.

- **Maintainer-side cleanup that the v5.0 retirement enables**:
  - Drop `Newtonsoft.Json` from `Fable.Remoting.Json/paket.references`.
  - Delete `Fable.Remoting.Json/FableConverter.fs` entirely.
  - Drop `open Newtonsoft.Json` / `open Newtonsoft.Json.Linq` from
    `Fable.Remoting.Server/Proxy.fs`, `Fable.Remoting.DotnetClient/Proxy.fs`,
    and every sibling adapter.
  - Collapse `JsonSerializerBackend` to a single case (or remove the DU
    entirely — every code path uses `JsonSerializerOptions` directly).

---

## Why the byte-equality is real

If you're skeptical that two different serializer libraries can produce
identical bytes — fair scepticism. The test suite proves it empirically.
`Fable.Remoting.Json.Tests/WireFormatTests.fs` is parameterised over an
`ISerializer` abstraction; the same 130+ wire-format-pinning tests run
against both serializers. Every Phase 2 pin test (originally written
against Newtonsoft) passes byte-equally when re-run via STJ.

The byte-compat workaround details are documented in
[`BYTE-COMPAT-MAP.md`](BYTE-COMPAT-MAP.md) — most notably:

- STJ's `WriteNumberValue(0.0)` writes `"0"`; Newtonsoft writes `"0.0"`.
  Fixed by a custom `DoubleConverter` that uses `ToString("R")` +
  appended `.0` for whole-valued doubles.
- STJ's `UnsafeRelaxedJsonEscaping` still escapes supplementary-plane
  codepoints (emoji, etc.) to surrogate-pair `\uXXXX\uXXXX` sequences.
  Newtonsoft passes them through as raw UTF-8. Fixed by a custom
  `StringConverter` that uses `Utf8JsonWriter.WriteRawValue` to bypass
  STJ's encoder entirely.
- `DateTimeKind.Unspecified` is NOT silently promoted to UTC (a
  long-standing subtlety in `FableJsonConverter` that the STJ port had to
  preserve to maintain byte-compat).
- Map-with-non-string-keys writes property names containing escaped
  quotes — e.g. `{"\"Red\"": 10}` for `Map<Color, int>`. Both serializers
  produce this odd-looking-but-valid shape; STJ's converter replicates it
  via a temp `Utf8JsonWriter` that serialises the key, then uses the
  resulting bytes as the property name.

Each finding has a dedicated byte-pin test that runs against both
serializers, so any future regression in either is caught immediately.
