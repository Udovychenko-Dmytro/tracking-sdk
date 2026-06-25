# Coding Standards

> **Purpose:** the C# conventions the Dmytro Udovychenko Tracking SDK actually follows — grounded in the source, not a generic template. **Mandatory:** read before your first C# edit each task.
> **Key files:** `TrackingSystem.cs`, `Tracker.cs` (static facade), `EventDispatcher.cs`, `EventSerializer.cs`, `RetryPolicy.cs`, `ITrackingLogger.cs`, `TrackingConfig.cs`.

Conventions inferred from the existing `Packages/com.dmytroudovychenko.tracking` source, plus the always-on rules in [CLAUDE.md](../../CLAUDE.md). This SDK is **plain-C#-first with DI seams**, not a MonoBehaviour-heavy Unity gameplay codebase. Naming follows the SDK's house style (`m_` fields, `UPPER_SNAKE_CASE` consts, explicit-index enums); it still diverges from typical Unity game code on namespacing, async-not-coroutines, and `ITrackingLogger` (see the call-out).

---

## Naming Conventions

| Element            | Convention              | Example                                  |
| ------------------ | ----------------------- | ---------------------------------------- |
| Namespaces         | `DmytroUdovychenko.Tracking[.Sub]` | `DmytroUdovychenko.Tracking`, `DmytroUdovychenko.Tracking.HostDemo` |
| Classes            | PascalCase, `sealed` for concrete | `TrackingSystem`, `EventDispatcher`, `CircuitBreaker` |
| Interfaces         | `I` + PascalCase        | `ITracker`, `ITransport`, `IEventStore`  |
| Methods            | PascalCase              | `SendMessage`, `TryEnqueue`, `Snapshot`  |
| Async methods      | PascalCase + `Async` suffix | `SendMapAsync`, `FlushAsync`, `DrainAsync` |
| Public Properties  | PascalCase              | `SessionId`, `IsEnabled`, `Metrics`      |
| Private Fields     | `m_` prefix + camelCase | `m_config`, `m_queue`, `m_sessionId`      |
| Constants          | UPPER_SNAKE_CASE        | `VERSION`, `PREFIX`, `MAX_IDLE_WAIT_MS`   |
| `static readonly` fields / singletons | PascalCase | `Instance` (the default-instance singletons), `EmptyPayload`, `Empty` |
| Enums              | PascalCase (type + members) | `OverflowPolicy.DropOldest`, `TransportMode.Http` |
| Local Variables    | camelCase               | `queued`, `evicted`, `retryPolicy`       |
| Parameters         | camelCase               | `message`, `startWorker`, `delayer`      |

> ⚠️ **House-style conventions — applied across the package.** The SDK uses this house style for **field naming, constant naming, enums, and local-variable typing**; `com.dmytroudovychenko.tracking` + tests were migrated to match and stay green on the EditMode suite. The rules:
> - Private fields use **`m_camelCase`** (e.g. `m_config`, `m_queue`) — the `m_` prefix, **not** `_`. Private **static** fields follow the same convention (e.g. the `Tracker` facade's `m_instance`, `m_gate`); reserve PascalCase for `static readonly` singletons/sentinels (`Instance`, `EmptyPayload`).
> - Constants are **`UPPER_SNAKE_CASE`** (e.g. `VERSION`, `MAX_IDLE_WAIT_MS`), **not** PascalCase.
> - Enums use **explicit indices + the 100-block group scheme** (see [Enums](#enums)), **not** auto-increment.
> - Locals declare **explicit types** — `var` is not used (see [Formatting](#formatting)).
> - Code remains **namespaced** under `DmytroUdovychenko.Tracking` (still diverges from typical global-scope Unity game code), and logging still routes through `ITrackingLogger` (no `LogCenter`).
> - **Exception — serialization DTOs:** the internal `PayloadEntry` DTO uses public lowercase fields `k` / `t` / `v` on purpose — those names *are* the compact JSON wire keys (`JsonUtility` serializes public fields by name). Keep wire-format field names as-is; don't PascalCase or `m_`-prefix them.

### Descriptive Names — Spell Words Out, No Cryptic Abbreviations

An identifier must read as words a person can understand at a glance — **not** a vowel-stripped consonant squeeze the reader has to decode. Prefer the full word over a truncation: `event`, not `evt`; `message`, not `msg`; `config`, not `cfg`; `context`, not `ctx`; `value`, not `val`; `index`, not `idx`; `temp`/`tempPath`, not `tmp`; `count`, not `cnt`; `response`, not `resp`; `button`, not `btn`. This applies to every identifier you author — locals, parameters, fields, properties, methods, **and type names**.

```csharp
// BAD — cryptic truncations
TrackingEvent evt = CreateEvent(...);
foreach (PayloadEntry kv in e.payload) { ... }
private static PayloadEntry Encode(string key, object val) { ... }

// GOOD — readable words
TrackingEvent trackingEvent = CreateEvent(...);   // `event` is a C# keyword → use a descriptive full name
foreach (PayloadEntry entry in e.payload) { ... }
private static PayloadEntry Encode(string key, object value) { ... }
```

**Allowed short forms** — established, dictionary- or domain-standard abbreviations that are *as* clear as (or clearer than) the long form, plus the language idioms already used in green code:

- Domain/standard terms: `id`, `config`, `json`, `http`, `url`, `db`, `max` / `min`, `ms` (as the `_MS` constant suffix, e.g. `MAX_IDLE_WAIT_MS`).
- Language idioms kept by house style: `e` for the caught exception in a `catch (Exception e)`; the loop counter `i` / `j` in a tight numeric `for`; single-letter `switch` type-pattern bindings (`case string s:`, `case int i:` — the canonical `EventSerializer.Encode` pattern).
- **Wire-format DTO field names** stay abbreviated by design — the `PayloadEntry` fields `k` / `t` / `v` *are* the JSON keys (see the House-style exception above). The exception is the **field names only**; everything else still spells words out.

> **Standard for new and edited code.** The `com.dmytroudovychenko.tracking` package was migrated to comply — `evt`→`trackingEvent`, `val`→`value`, `kv`→`entry`, `tmp`→`tempPath`, and the wire DTOs `Kv`/`Evt`→`PayloadEntry`/`SerializedEvent`. Keep it that way; don't open a standalone churn pass on unrelated code just to rename.

---

## Formatting

- **Indentation**: 4 spaces (not tabs).
- **Braces**: Allman style — opening brace on its own line.
- **Access modifiers**: always explicit (`private`, `public`, `internal`, `protected`). `internal` is used deliberately to expose a member to tests without making it public API (e.g. `TrackingSystem.CreateDefaultTransport`).
- **No `var` — declare explicit types.** Spell out the type on every local, including `foreach`, `using`, and `out` declarations (`QueuedEvent queued = new QueuedEvent { ... };`, `out QueuedEvent evicted`). The type is always visible at the declaration site; `var` is not used anywhere in SDK code.
- **`sealed`**: concrete classes are `sealed` by default (`public sealed class TrackingSystem`). Leave a type open only when it is intended to be subclassed.

---

## Control Flow — Always Use Braces

All `if`, `else`, `for`, `foreach`, `while`, and `using` blocks use `{}`, even single-line bodies — it prevents bugs when a second statement is added later.

```csharp
// GOOD
if (map == null || map.Count == 0)
{
    return Task.FromResult(false);
}
```

Exception, used consistently in the hot path: a bare early-return guard may stay on one line for density (`if (!m_enabled) return false;`). Match the surrounding method — anything more than a lone `return`/`continue`/`throw` gets braces.

---

## Expression-Bodied Members

For a property or method whose body is a single expression, use the `=>` form. The four-line `{ get { return …; } }` block is ceremony.

```csharp
public string SessionId => m_sessionId;
public Task FlushAsync() => m_dispatcher.DrainAsync();
public override string ToString() => $"TrackingEvent({Type} #{Id} @ {TimestampUtc:O})";
```

Use the full block only for multi-statement bodies, side effects, or a `get`/`set` pair.

---

## Dependency Injection — Constructor Seams With Production Defaults

The SDK's central pattern: every external concern is an **interface** injected through the constructor, and every parameter is **optional with a production-default fallback**. This is what makes `new TrackingSystem()` valid *and* the whole pipeline deterministically testable.

```csharp
public TrackingSystem(
    TrackingConfig config = null,
    ITransport transport = null,
    IClock clock = null,
    /* … */
    bool startWorker = true)
{
    m_config = config ?? new TrackingConfig();
    m_clock  = clock  ?? SystemClock.Instance;
    // …
}
```

Rules:
- A new external dependency (network, disk, clock, time, randomness, connectivity) gets an **interface** + a production default + a test fake under `Tests/Editor/Fakes/`. Never hard-code `DateTime.UtcNow`, `Task.Delay`, `File.*`, or `new HttpClient()` deep in logic — route it through a seam (`IClock`, `IDelayer`, `IEventStore`, `ITransport`).
- Tests construct with `startWorker: false` and pump delivery via `FlushAsync()` — no real waits, no real network. Keep that contract intact.

---

## Architecture — Prefer Patterns Over Ad-hoc Edits

Before changing code, consider whether the change fits an established pattern. Isolated patches cause inconsistency and technical debt. **Patterns actually used in this SDK:**

- **Strategy via interfaces** — behaviour that varies by context is an interface with swappable implementations: `ITransport` (`HttpTransport` / `SimulatedHttpTransport` / `NullTransport`), `IEventStore` (`FileEventStore` / `InMemoryEventStore` / `NullEventStore`), `IConnectivity`, `IDeadLetterSink`.
- **Null Object** — every seam has a do-nothing default so callers never null-check it: `NullTransport`, `NullEventStore`, `NullTrackingLogger`, `AlwaysOnlineConnectivity`. Prefer a Null Object over a nullable dependency.
- **Singleton default instance** — stateless production defaults expose a shared `static readonly … Instance` (`SystemClock.Instance`, `UnityTrackingLogger.Instance`).
- **Pipeline stages** — producer (hot path) → bounded `EventQueue` (with `OverflowPolicy`) → background `EventDispatcher` (batching) → `RetryPolicy` + `CircuitBreaker` → `ITransport`, with `IDeadLetterSink` as the terminal sink. New reliability behaviour should slot into a stage, not bypass the pipeline.

**Rule of thumb:** if a change touches multiple stages or will recur, fit it to a pattern; if it's genuinely one-off, keep it simple — but say why.

---

## Error Isolation — Tracking Must Never Throw Into Caller Code

Every public API entry point wraps its body in `try/catch`, logs via the logger seam, and returns a safe default. A telemetry SDK throwing into game/host code is a defect.

```csharp
public bool SendMessage(string message)
{
    try
    {
        if (!m_enabled) return false;
        if (string.IsNullOrWhiteSpace(message)) return false;
        // …
    }
    catch (Exception e)
    {
        m_logger.Log(TrackingLogLevel.Error, "SendMessage failed", e);
        return false;
    }
}
```

---

## Logging — Route Through `ITrackingLogger`, Never the Console Directly

Runtime SDK code never calls `Debug.Log` / `Console.WriteLine` directly — it logs through the injected `ITrackingLogger` seam so the host can forward or silence diagnostics. The only place that touches `UnityEngine.Debug` is the `UnityTrackingLogger` implementation.

```csharp
m_logger.Log(TrackingLogLevel.Error, "Persist failed", e);   // GOOD
Debug.LogError("Persist failed");                            // BAD in runtime SDK code
```

**Severity & level filtering.** `TrackingSystem` wraps the resolved logger in `LevelFilteringTrackingLogger` keyed on `TrackingConfig.MinLogLevel` (default `Warning`), so the threshold governs every component fed that logger uniformly. Step traces are tiered: lifecycle (`initialized` / `enqueued` / `delivered`) at `Info`, event payload + wire JSON at `Debug`. Guard a verbose-only log with `ShouldLog(level)` when building its string is non-trivial (e.g. JSON serialization), so the work is skipped when filtered out.

> Deliberate exceptions that must stay: the demo's on-screen Canvas/uGUI output, and `LiveRetryTests`' `[live-retry]` metrics line. Don't strip those during a diagnostics cleanup.

---

## Async / Concurrency

The hot path (`SendMessage` / `SendMapAsync`) must stay **non-blocking** — it validates, enriches, enqueues, and returns; the network is touched only on the background dispatcher thread.

- **`ConfigureAwait(false)`** on every library `await` (the worker has no Unity main-thread affinity). See `HttpTransport`, `EventDispatcher`.
- Never block on async (`.Result` / `.Wait()`) on the hot path.
- The bounded `EventQueue` is reachable from any thread and drained by the worker — keep lock scopes correct and small; no torn state.
- Honour the `CancellationToken` mid-backoff; on `Dispose`, stop the worker cleanly and release `HttpClient` / `SemaphoreSlim`.
- A `SendMapAsync` `TaskCompletionSource` must be completed on **every** path: delivered → `true`; retries-exhausted / evicted / rejected / purged / cancelled → `false`. A never-completed `Task` is a hang. Create it with `TaskCreationOptions.RunContinuationsAsynchronously`.

---

## Unity-Specific Patterns (MonoBehaviour edges)

The runtime SDK is plain C#; MonoBehaviours appear only as thin glue at the edges — `TrackingLifecycle` (lifecycle persistence) and `TrackingSdkDemo` (the demo). Conventions there:

- **Wire dependencies in code, not the Inspector.** Edge MonoBehaviours receive their tracker through a factory / `Bind` method (`TrackingLifecycle.Attach` → `Bind`) and guard use with `?.` (`m_tracker?.Persist()`) — they expose **no `[SerializeField]`** and need no scene wiring. MonoBehaviour fields use `m_camelCase`, same as the rest of the package.
- **Async, not coroutines.** The SDK is `Task`-based throughout and uses no Unity coroutines. Reach for the `IEnumerator` coroutine pattern only if a future edge genuinely needs frame-spread work.

If you ever **do** add an Inspector-wired MonoBehaviour, follow the standard Unity rules (ported here so they aren't lost):

- **Prefer `[SerializeField] private`** over public fields for Inspector exposure; group related fields with `[Header("…")]`.
- **Null-check every serialized reference** before use in a public method and **log via the logger seam, returning early — never let a `NullReferenceException` throw**:
  ```csharp
  if (m_label == null)
  {
      m_logger.Log(TrackingLogLevel.Error, "m_label is not wired in the prefab");
      return;
  }
  m_label.text = value;
  ```
  The SDK has no `LogCenter` — route through `ITrackingLogger`. `ITrackingLogger.Log` takes no Unity context argument; if you must log a missing reference via `UnityEngine.Debug` directly from a MonoBehaviour, pass `this` as the context so clicking the console entry pings the object — but prefer the seam.
- **Extract repeated field access into a guarded accessor.** If the same field (or injected dependency) is read/written in more than one place, route every access through one dedicated method that does the null-check once, instead of duplicating the guard (or missing it). Callers never touch the field directly — one place for null-safety, formatting, or future behaviour.

## Null Safety & The `TryGet` Pattern

- Null-conditional / null-coalescing throughout (`evicted.Completion?.TrySetResult(false)`, `config ?? new TrackingConfig()`).
- Prefer a **Null Object** default over a nullable dependency (see Architecture) — then there is nothing to null-check.
- A method that can fail to produce a result returns `bool` and exposes the value via an `out` parameter — the failure path is then impossible to ignore. Don't return `null` to signal "not found".

```csharp
// GOOD — used by EventQueue
if (!m_queue.TryEnqueue(queued, out QueuedEvent evicted))
{
    // rejected (queue full under RejectNew)
}
```

---

## No Magic Values — Tunables Live in `TrackingConfig`

No raw literals inline for thresholds, capacities, delays, or endpoints. Pipeline tunables (queue capacity, batch size, retry attempts, backoff bounds, circuit-breaker threshold/cooldown, timeouts, endpoint, dead-letter capacity) belong on `TrackingConfig` so they are configurable and testable. A short fixed string used once (a log prefix, an event-type tag) is fine as a named `const`.

---

## Enums

> The SDK enums — `OverflowPolicy`, `TransportMode`, `TrackingLogLevel`, `CircuitState`, `ServerEnvironment` — follow these rules: explicit integer values, one enum per file (`TrackingLogLevel.cs`, `CircuitState.cs`).

### Explicit Index Values — Start at 1, Reserve 0

**Always assign explicit integer values** — never rely on auto-increment. Implicit values shift silently when members are added or reordered, which makes the mapping invisible and fragile.

**Numbering starts at 1.** Reserve **0** for the unset/`None` sentinel — an explicit `None = 0` member where that reads naturally, or an unused spare otherwise. A default-initialized field (`default(T)` == 0) is then a detectably-invalid "not set" rather than a silently-valid real value; `switch` `default:` branches treat 0/unknown as the safe fallback. (`OverflowPolicy`, `TransportMode`, `CircuitState`, `TrackingLogLevel` all start at 1 with 0 reserved.)

> **Documented exception — `ServerEnvironment.FakeServer = 0`.** Where 0 is the *safest* member (no side effects), placing it there is allowed: it makes `default(ServerEnvironment)` the offline simulated fake — no real network, never blocks Init on connectivity — which is the safe fallback the rule is really after, not a "silently-valid production value." `ServerEnvironment` then uses the [group-based blocks](#group-based-index-blocks) below: the **simulated** group occupies 0 (`FakeServer = 0`, `FakeServerChaos = 1`) and the **real-HTTP** group occupies 100 (`HttpTestServer = 101`, `HttpTestServerChaos = 102`) — so every real-network target stays at 100+.

```csharp
// BAD — implicit, and 0-based (a default-initialized value masquerades as a real one)
DropOldest,
RejectNew,

// GOOD — explicit, starting at 1; 0 left reserved as the unset/None sentinel
DropOldest = 1,
RejectNew  = 2,
```

### Group-Based Index Blocks

When an enum represents multiple semantic groups, give each group its own block of 100 — groups stay visually separated, values can't collide, and there's room for new groups (400, 500, …). Consistent with start-at-1, each group's **base offset** (0, 100, 200…) is that group's reserved `None`; real members occupy base+1 onward:

| Group       | Reserved (`None`) | First member |
| ----------- | ----------------- | ------------ |
| Interface   | 0                 | 1            |
| ClientColor | 100               | 101          |
| Success     | 200               | 201          |
| Danger      | 300               | 301          |

### Positional Index Consistency

Within every group, a value maps to the **same positional index** as the reference group (`Interface`):

```
step 1  (first)  →  Interface = 1,   Success = 201, Danger = 301
step 16          →  Interface = 16,  Success = 216, Danger = 316
```

Every group carries the **same steps** as the reference; don't skip steps — gaps break positional alignment. Reserve the **end** of each 100-block (e.g. 219–299) for group-specific non-positional slots (`SuccessAccent = 219`).

### One Enum Per File

Define each enum in its own dedicated file containing **only** enum definitions + their XML docs, separate from the class that uses it (`Queue/OverflowPolicy.cs`, `Transport/TransportMode.cs`). Document each enum and group block with `/// <summary>` plus inline separators.

> `TrackingEventType` stays a `static class` of `string` constants (`"message"`, `"map"`), not an enum, because the value is written into the serialized payload.

---

## Code Patterns

### LINQ — `Any` over `Count() > 0`

For an existence check, use `Any(predicate)` — it short-circuits on the first match and allocates no enumerator chain. Same for "none match" (`!collection.Any(p)`). Never use `Count()` to ask a yes/no question.

```csharp
// BAD                                 // GOOD
items.Where(x => x.Ready).Count() > 0; items.Any(x => x.Ready);
```

### String Interpolation

Use `$""`, with format specifiers where they matter:

```csharp
$"POST {m_endpoint} failed: HTTP {(int)response.StatusCode}"
$"TrackingEvent({Type} #{Id} @ {TimestampUtc:O})"   // round-trip ("O") timestamp
```

### Pattern Matching

Use type patterns and `switch` over type-tag chains. The serializer's payload encoder is the canonical example:

```csharp
switch (value)
{
    case null:     return new PayloadEntry { k = key, t = "n", v = string.Empty };
    case string s: return new PayloadEntry { k = key, t = "s", v = s };
    case bool b:   return new PayloadEntry { k = key, t = "b", v = b ? "1" : "0" };
    case int i:    return new PayloadEntry { k = key, t = "i", v = i.ToString(CultureInfo.InvariantCulture) };
    // …
}
```

### Culture-Invariant Formatting

Any value that crosses a serialization / network boundary is formatted with `CultureInfo.InvariantCulture` — never the ambient locale (a German device must not emit `4,99`). Applies to numbers, dates (`"O"`), and parsing on the way back.

### Static Utility Classes

Shared, stateless helpers are `static class`: `TrackingSdk` (`public const string VERSION = "1.0.0"`, kept in sync with `package.json`), `TrackingEventType`, and the static `EventSerializer.ToJson`.

### Data Structures

- `Dictionary<string, object>` is the event payload shape; prefer `Dictionary` for key-based lookups.
- Defensively **copy** mutable inputs the caller still owns (`new Dictionary<string, object>(map)` in `SendMapAsync`) so later caller mutations can't corrupt a queued event.
- **Nest related DTOs inside their owner.** A data-transfer type that exists only to shape one class's output lives as a nested `private` type, not a standalone file — `EventSerializer` nests `PayloadEntry` / `SerializedEvent` / `Batch` (its `JsonUtility` wire shapes) because they have no meaning outside the serializer.

### Conditional Compilation

The **runtime SDK avoids `#if UNITY_EDITOR`** — it stays plain, portable C#. Editor-only code lives in editor/test assemblies instead. Reach for `#if` only when there is no seam-based alternative.

---

## Comments & XML Documentation

- `/// <summary>` on every public type and member; `<see cref="..."/>` for cross-references; `<remarks>` for the non-obvious "why" (see `TrackingSystem`). `<remarks>` may run longer than a summary.
- **Length budget — ≤2 lines** for both `<summary>` content (the tag lines don't count) and inline `//` comments; complex/non-obvious logic only. Longer rationale → a `KnowledgeBase/**/*.md` doc with a 1-line pointer.
- This budget is the standard **going forward** (matching the [CLAUDE.md](../../CLAUDE.md) "Documentation" Work Rule), not a claim about every existing line: a few current summaries (e.g. `ITracker`, `EventDispatcher`) predate it and run 3–4 lines — trim them to ≤2 (or move detail into `<remarks>` / a doc) when you next touch the file. See [DOCUMENTATION_RULES.md](DOCUMENTATION_RULES.md).
- **No `#region`** — the runtime SDK uses none. Keep files small and single-purpose instead of partitioning a large file with regions.

---

## Tests — Deterministic, Editor, Fake-Backed

- Tests are **EditMode** under `Packages/com.dmytroudovychenko.tracking/Tests/Editor/`. The 145 deterministic tests use the DI fakes (`FakeClock`, `FakeConnectivity`, `RecordingTransport`, `FlakyTransport`, `FakeHttpMessageHandler`, …) + a virtual clock — **no real network or wall-clock sleeps** (the `FileEventStore` / `Init` tests touch a temp file by design).
- The 2 live tests (`LiveTransportTests` / `LiveRetryTests`, `[Category("Live")]`) POST to the deployed receiver and run as part of the suite, so a headless run needs network.
- New behaviour ships with new tests; keep the suite green on a headless run, and update the **test count** wherever it appears (see [CLAUDE.md](../../CLAUDE.md) "Build & Verify" + "Sync docs with code").
