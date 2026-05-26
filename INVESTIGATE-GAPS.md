# Investigate Gaps — `stj-json-converter-port` branch

Read-only audit of the 13-commit branch ahead of opening the upstream
issue/PR. The lens: things a thorough reviewer (or Zaid himself) would
catch — silent regressions, dead code, doc drift, perf cliffs, latent
correctness corners that the existing test suite doesn't reach.

10 gaps below, grouped by severity. Each cites a `file:line`, the
fingerprint (pattern), what would break, a proposed fix shape, and how it
should land relative to the PR.

---

## HIGH

### 1. Newtonsoft adapter test coverage regressed silently after the default flip

**Fingerprint.** Phase 5 changed `Remoting.createApi()` to default to
`SystemTextJson`. The pre-existing adapter tests
(`FableSuaveAdapterTests.fs`, `FableGiraffeAdapterTests.fs`,
`FableFalcoAdapterTests.fs`, `ServerDynamicInvokeTests.fs`,
`Fable.Remoting.AzureFunctions.Worker.Tests/Client/AdapterTests.fs`) still
call `Remoting.createApi()` without piping through `withNewtonsoftJson`.
They serialise assertion fixtures via `FableJsonConverter` (the legacy
Newtonsoft converter) on the **client side** of each test.

These tests still **pass** — because byte-compat holds, an STJ server
responds with bytes a Newtonsoft client can parse. But they no longer
exercise the legacy *server-side* Newtonsoft path end-to-end.

**What breaks.** When v5.0's retirement PR deletes the
`JsonSerializerBackend.NewtonsoftJson` branch from
`Fable.Remoting.Server.Proxy.fs`, there is **no automated regression
gate** that the legacy path still works through the adapter integration
layer. Subtle Newtonsoft-only behaviours (e.g. DateParseHandling
interaction with DateTimeOffset offsets — fixed in Phase 4f at
`Proxy.fs:newtonsoftArgSettings`) could be silently broken by a refactor
during the deletion sweep, and not surface until a consumer pins the
legacy version and tries to upgrade.

**Evidence.**
- [`Fable.Remoting.Suave.Tests/FableSuaveAdapterTests.fs:32-38`](Fable.Remoting.Suave.Tests/FableSuaveAdapterTests.fs#L32-L38) — `app = Remoting.createApi() |> ... |> buildWebPart`. No `withNewtonsoftJson`.
- Same pattern in Giraffe, Falco, AzureFunctions test setups.
- Per Phase 5 commit `26ee7af`: 28 Suave / 96 Giraffe / 77 Falco / 30 Server / 28 AzureFunctions tests all pass — but the *server-side* serializer they're hitting is STJ, not Newtonsoft.

**Proposed fix.** Build TWO apps in each existing adapter test file — one
default (STJ), one with `|> Remoting.withNewtonsoftJson`. Run the existing
fixtures against both via shared test factories. Each adapter test file
grows by ~10 lines (the second app + a parameter on the runner). Same
337 assertions, 2 backends.

**PR fit.** This PR. The coverage gap was introduced by the default flip
in this PR; closing it should land in the same PR.

**Severity.** HIGH.

### 2. `JsonSerializerOptions` rebuilt per `createApi()` call

**Fingerprint.** `Remoting.createApi()` calls
`Fable.Remoting.Json.SystemTextJson.FableConverters.create()` on every
invocation. `create()` allocates a fresh `JsonSerializerOptions` and
registers ~24 converters via reflection. Once any serializer has been
used with that options instance, STJ freezes it — but the cost of the
freeze ceremony, plus the reflection-driven factory registrations, is
incurred each time.

**What breaks.** Not a correctness issue — wire format is byte-equal
regardless. But:
- Test setups that build many APIs (e.g. per-test isolated `TestServer`
  instances) pay the cost N times.
- Apps that re-create their `RemotingOptions` mid-process (uncommon but
  possible for dynamic protocols) suffer measurably.
- The reflection registration is observable in startup traces — slower
  cold path.

**Evidence.**
[`Fable.Remoting.Server/Remoting.fs:28`](Fable.Remoting.Server/Remoting.fs#L28) — `JsonSerializer = SystemTextJson (Fable.Remoting.Json.SystemTextJson.FableConverters.create())`.
This is inside the record literal in `createApi()` — runs every call.

**Proposed fix.** Cache a default `JsonSerializerOptions` at module level:

```fsharp
let private defaultStjOptions =
    Fable.Remoting.Json.SystemTextJson.FableConverters.create()

let createApi() =
    { ...
      JsonSerializer = SystemTextJson defaultStjOptions
      ... }
```

This means every `createApi()` returns options pointing at the same
shared instance. Consumers who mutate (via `withSerializerOptions`)
explicitly pass their own, so no shared-state aliasing risk for them.
The shared default has identical converters, so behaviour is identical.

**PR fit.** This PR. One-line change, zero risk, real perf win.

**Severity.** HIGH (perf, fixable cheaply, ships in this PR).

### 3. Dead code in `DotnetClient/Proxy.fs` `serializeArgs` STJ branch

**Fingerprint.** Three unused declarations in the STJ branch of
`serializeArgs`:

```fsharp
let arr = args |> List.toArray             // never read
let sb = StringBuilder()
use sw = new System.IO.StringWriter(sb)    // never read
use writer = new Utf8JsonWriter(new System.IO.MemoryStream())  // never read
// Simpler: build a JSON array manually
sb.Append '[' |> ignore
args |> List.iteri (fun i a -> ...)
sb.Append ']' |> ignore
sb.ToString()
```

The STJ path builds the JSON array via `StringBuilder` manually — the
`StringWriter` + `Utf8JsonWriter` were presumably a previous-attempt path
that got abandoned. They allocate (and dispose) but contribute nothing
to the output.

**What breaks.** Code review reading. Two allocations + two dispose calls
per `createRequestBody` call when STJ is opted in. Trivial perf cost,
non-trivial review confusion.

**Evidence.**
[`Fable.Remoting.DotnetClient/Proxy.fs:70-73`](Fable.Remoting.DotnetClient/Proxy.fs#L70-L73).

**Proposed fix.** Delete the three unused lines (`let arr`, `use sw`,
`use writer`). Leave the StringBuilder-based manual JSON array assembly
(which is the actually-used code).

**PR fit.** This PR. Trivial cleanup, makes the Phase 4d diff cleaner
for Zaid's review.

**Severity.** HIGH (visible in any diff scan of the DotnetClient changes).

### 4. Documentation drift across `withSerializerOptions` docstring, UPSTREAM-ISSUE-DRAFT, UPSTREAM-PR-DRAFT

**Fingerprint.** Three places where the documentation describes the
pre-Phase-5 state ("Newtonsoft is default; STJ is opt-in"):

1. [`Fable.Remoting.Server/Remoting.fs:56-57`](Fable.Remoting.Server/Remoting.fs#L56-L57) — `withSerializerOptions` docstring says "Without `withSerializerOptions`, the API uses Newtonsoft (existing behaviour)." This is **false** after Phase 5 — the default is STJ.
2. [`UPSTREAM-ISSUE-DRAFT.md`](UPSTREAM-ISSUE-DRAFT.md) — Section "Approach" describes opt-in via `withSerializerOptions` with Newtonsoft as the default. Phase 5 inverted this; the draft was written before that decision.
3. [`UPSTREAM-PR-DRAFT.md`](UPSTREAM-PR-DRAFT.md) — Migration story snippet shows opt-in adding `|> Remoting.withSerializerOptions (FableConverters.create())`, which is now unnecessary (it's the default).

**What breaks.** The PR's cover letter is the first thing Zaid reads.
Inconsistency with the actual code state would cost reviewer trust
immediately ("does the author even know what shipped?"). Even worse, a
confused user reading `withSerializerOptions`'s docstring after merge
would be told to call a function that's already the default.

**Evidence.** Cited above.

**Proposed fix.** Refresh all three:
- `Remoting.fs:56-57`: invert wording — "Without `withSerializerOptions`,
  the API uses the default STJ converter set from `FableConverters.create()`.
  Pipe through `withSerializerOptions` to pass a customised
  `JsonSerializerOptions` (e.g. with additional converters, different
  `WriteIndented` setting)."
- Both `UPSTREAM-*.md`: rewrite Approach + Migration sections to describe
  the actual landed state — STJ default, Newtonsoft opt-in via
  `[<Obsolete>]`-marked `withNewtonsoftJson`. Three-PR stack restructured
  with default flip as part of PR #2.

**PR fit.** This PR. Drafts are the cover letter for the PR itself.

**Severity.** HIGH.

---

## MEDIUM

### 5. `MapNonStringKey` writer encoder fallback to `JavaScriptEncoder.Default` is inconsistent with the rest of the converter set

**Fingerprint.** `FSharpMapNonStringKeyConverter.writerOptionsFor` falls
back to `JavaScriptEncoder.Default` when `options.Encoder` is null.
`Default` escapes a much wider set than `UnsafeRelaxedJsonEscaping` (which
`FableConverters.addTo` configures elsewhere). If a consumer hand-rolls
a `JsonSerializerOptions` without an explicit encoder and uses *only*
this converter, key serialisation gets different escape behaviour than
value serialisation through the same options.

**What breaks.** A `Map<DateTime, _>` with a UTC DateTime key serialised
through partial-config options would produce keys with escaped colons
(`:`) — Newtonsoft never emits that. Byte-compat divergence in a
narrow path.

**Evidence.**
[`Fable.Remoting.Json/FableSystemTextJsonConverter.fs:757-762`](Fable.Remoting.Json/FableSystemTextJsonConverter.fs#L757-L762):

```fsharp
let writerOptionsFor (options: JsonSerializerOptions) =
    JsonWriterOptions(
        Encoder = (if isNull options.Encoder then JavaScriptEncoder.Default else options.Encoder),
        Indented = false,
        SkipValidation = false)
```

**Proposed fix.** Use `UnsafeRelaxedJsonEscaping` as the fallback (matches
what `FableConverters.addTo` sets explicitly):

```fsharp
Encoder = (if isNull options.Encoder then JavaScriptEncoder.UnsafeRelaxedJsonEscaping else options.Encoder)
```

**PR fit.** This PR.

**Severity.** MEDIUM (corner case; only triggers if consumer hand-rolls
options without an encoder).

### 6. No test for `Remoting.withNewtonsoftJson` opt-back-in

**Fingerprint.** Phase 5 added the `withNewtonsoftJson` helper (marked
`[<Obsolete>]`) to let consumers explicitly pin their API to the legacy
backend. Built it, documented it in `MIGRATION.md` — but no automated
test exists that *calling* it actually routes through the Newtonsoft
branch.

**What breaks.** Same root cause as gap #1 — the legacy path is
under-tested. If `withNewtonsoftJson` silently became a no-op (e.g.
during a future refactor of `JsonSerializerBackend`), nothing in CI would
catch it. Consumers pinning to the legacy path for verification would
get STJ behaviour instead, defeating the migration safety net.

**Evidence.**
[`Fable.Remoting.Server/Remoting.fs:77-78`](Fable.Remoting.Server/Remoting.fs#L77-L78). No
caller in any `.Tests` project.

**Proposed fix.** Add one test per major adapter that opts back in
explicitly and round-trips a representative shape (a record with
DateTimeOffset is the canonical "would catch the regression" case, since
that's where the Newtonsoft path's `newtonsoftArgSettings` matters most).

**PR fit.** This PR. Closes alongside gap #1.

**Severity.** MEDIUM.

### 7. `JsonSerializerOptions.IsReadOnly` not checked in `FableConverters.addTo`

**Fingerprint.** `addTo` mutates `options.Encoder` and adds to
`options.Converters`. STJ freezes `JsonSerializerOptions` after the first
serializer call against them. If a consumer calls `addTo` on already-used
options, STJ throws `InvalidOperationException` with a fairly opaque
message about "this instance is in use".

**What breaks.** Users who try `FableConverters.addTo myExistingOptions`
where `myExistingOptions` has already been used elsewhere get a confusing
runtime error instead of a clear "call addTo before first use" message.

**Evidence.**
[`Fable.Remoting.Json/FableSystemTextJsonConverter.fs:~995-1024`](Fable.Remoting.Json/FableSystemTextJsonConverter.fs#L995-L1024) — `addTo` body has no `IsReadOnly` check.

**Proposed fix.** Top of `addTo`:

```fsharp
let addTo (options: JsonSerializerOptions) : unit =
    if options.IsReadOnly then
        invalidOp "FableConverters.addTo must be called before the JsonSerializerOptions has been used for serialization. Pass a fresh JsonSerializerOptions instance, or use FableConverters.create() to get one configured from scratch."
    options.Encoder <- JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    ...
```

**PR fit.** This PR.

**Severity.** MEDIUM (user-facing diagnostics).

### 8. `MIGRATION.md` missing the `UnsafeRelaxedJsonEscaping` security note

**Fingerprint.** The encoder choice (driven by byte-compat with
Newtonsoft) does NOT escape HTML-sensitive characters (`<`, `>`, `&`,
`'`). Newtonsoft has identical behaviour with default settings, so
consumers are presumably aware — but the migration document is consumers'
first stop, and the note belongs there.

**What breaks.** A consumer reading `MIGRATION.md` and considering
embedding Fable.Remoting's JSON output directly into an HTML response
(without proper HTML-context escaping) could ship an XSS vector. The
risk existed pre-PR (Newtonsoft default did the same), but the PR is the
natural place to surface the constraint to anyone re-auditing their
serialiser setup.

**Evidence.**
[`MIGRATION.md`](MIGRATION.md) — no security section. Note already
present in [`BYTE-COMPAT-MAP.md:§10.1`](BYTE-COMPAT-MAP.md) and §12.2.2,
but those are internal docs.

**Proposed fix.** Add a "Security note" section to `MIGRATION.md`
explicitly calling out that `UnsafeRelaxedJsonEscaping` is the chosen
encoder, what it doesn't escape, and the responsibility this places on
consumers who interpolate JSON into HTML.

**PR fit.** This PR.

**Severity.** MEDIUM (security-adjacent documentation, easy to land).

---

## LOW

### 9. `MapNonStringKey` writer allocates `MemoryStream` + `Utf8JsonWriter` per map entry

**Fingerprint.** The non-string-key map writer constructs a fresh
`MemoryStream` + `Utf8JsonWriter` for every key in the map, serializes
the key into them, reads the resulting bytes, then disposes both. For a
`Map<Guid, V>` with 10,000 entries, that's 20,000 allocations on the
serialise hot path.

**What breaks.** Not correctness — but allocations on a per-element
basis is the classic JSON-serialiser perf trap. For workloads that
serialise large keyed maps, this would underperform Newtonsoft (which
uses a single internal `JTokenWriter`).

**Evidence.**
[`Fable.Remoting.Json/FableSystemTextJsonConverter.fs:788-799`](Fable.Remoting.Json/FableSystemTextJsonConverter.fs#L788-L799).

**Proposed fix.** Allocate one `MemoryStream` + `Utf8JsonWriter` per
*Write* call, reset and reuse for each key. Or use a pooled buffer
(`ArrayBufferWriter<byte>`) shared across the converter's lifetime.

**PR fit.** Follow-up. Note in BYTE-COMPAT-MAP §17 or the open-questions
section of the upstream issue.

**Severity.** LOW (perf-only; only matters for workloads with very large
non-string-keyed maps).

### 10. AzureFunctions test infrastructure unchanged post-Phase-5

**Fingerprint.** `Fable.Remoting.AzureFunctions.Worker.Tests/Client/AdapterTests.fs`
requires a manually-running FunctionApp at `localhost:7071`. Pre-existing
constraint (not introduced by this PR), but post-Phase-5 the test cover
the STJ default path through the Functions runtime *only if a developer
manually starts the FunctionApp*. CI almost certainly doesn't.

**What breaks.** Same general theme as gaps #1 and #6 — legacy + new
paths through this adapter are essentially un-tested in CI. Specific to
AzureFunctions: even less coverage than Suave/Giraffe/Falco because the
test rig isn't headless.

**Evidence.**
[`Fable.Remoting.AzureFunctions.Worker.Tests/Client/AdapterTests.fs:17-21`](Fable.Remoting.AzureFunctions.Worker.Tests/Client/AdapterTests.fs#L17-L21) — `let postReq path body = let url = "http://localhost:7071/api" + path`.

**Proposed fix.** Pre-existing limitation. Worth a one-line acknowledgement
in `BYTE-COMPAT-MAP.md §17.3` (where Phase 4g's deferred adapters are
listed) noting that AzureFunctions specifically inherits this manual-rig
constraint independent of the STJ work. Could also propose, in the
upstream issue, an in-process FunctionApp test using the Azure Functions
worker SDK's testing primitives — but that's a separate PR.

**PR fit.** Documentation only — no code.

**Severity.** LOW.

---

## Recommended priorities

**Land in this PR — close gaps #1, #3, #4 (and ideally #2, #6, #7).**
These are the gaps that would either:

- Make Zaid suspicious about the rigor of the work (the docs drift in #4
  is the riskiest social-cost issue).
- Leave the v5.0 retirement under-tested (gaps #1 and #6 are coupled —
  together they ensure the legacy path is automated-test-covered through
  the deprecation window).
- Be a one-line fix that improves perf or diagnostics (gaps #2, #3, #5,
  #7 — all small, all increase reviewer confidence in the work).

**Defer to follow-up — gaps #8 (with a tiny note added now), #9, #10.**
These are either real-but-narrow (#9 — perf-only on a narrow workload
shape) or documentation-only acknowledgements of pre-existing limitations
(#10).

If you only do one thing before opening the upstream conversation: fix
gap #4 (docs drift). Inconsistent docs make a reviewer suspect everything
else, even when the code is right.
