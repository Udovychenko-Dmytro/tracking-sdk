# Dmytro Udovychenko Tracking SDK

A **production-grade event-tracking SDK for Unity**. It exposes a tiny public API and tracks the
usage of its own APIs through an internal event mechanism that "sends" events to a server.

The *business* logic of the API is intentionally minimal — the value is in the **implicit technical
requirements of a production-grade event pipeline** being identified and implemented, with automated
test coverage. This SDK does exactly that: a non-blocking API on top
of a thread-safe queue → background batching dispatcher → pluggable transport, with retries, durable
persistence, lifecycle flush, connectivity-awareness, a circuit breaker, dead-lettering, metrics, a
logging hook, and privacy controls — all behind dependency-injection seams that make the whole thing
deterministically testable.

- **Unity:** `2022.3.62f3` (requires `2022.3+`) · **Package:** `com.dmytroudovychenko.tracking` · namespace `DmytroUdovychenko.Tracking`
- **Tests:** 147 EditMode tests (green) — 145 deterministic (no real network or wall-clock delays) + 2 live tests that POST to the deployed receiver (need network)
- **License:** MIT

---

## Install

Via the **Unity Package Manager** — *Window → Package Manager → + → Add package from git URL*:

```
https://github.com/Udovychenko-Dmytro/tracking-sdk.git?path=tracking-sdk/Packages/com.dmytroudovychenko.tracking
```

…or add the dependency to `Packages/manifest.json` directly:

```json
"dependencies": {
  "com.dmytroudovychenko.tracking": "https://github.com/Udovychenko-Dmytro/tracking-sdk.git?path=tracking-sdk/Packages/com.dmytroudovychenko.tracking"
}
```

…or install the built UPM tarball offline — *+ → Add package from tarball* → pick
`dist/com.dmytroudovychenko.tracking-1.0.0.tgz` (rebuild it any time with the `/release-package`
command). Then import the **Basic Usage** sample via *Package Manager → this package → Samples → Import*.

---

## The public API

Two methods, exactly as specified:

```csharp
using DmytroUdovychenko.Tracking;

// Initialize once for a user — durable persistence + connectivity-awareness are wired in.
// The static Tracker facade holds the configured tracker; there's no instance to keep or pass.
// userId is required; pick a server (enum) or pass a custom endpoint.
Tracker.Init("user-123");                                          // fake server, simulated, no network
//   = Tracker.Init("user-123", ServerEnvironment.HttpTestServer);    // named target → live test receiver (stub), real HTTP
//   = Tracker.Init("user-123", "https://your.host/track.php");       // custom endpoint, real HTTP
//   = Tracker.Init("user-123", minLogLevel: TrackingLogLevel.Info);  // trace the pipeline via ITrackingLogger (Warning→Info→Debug)
//   = await Tracker.InitAsync("user-123", ServerEnvironment.HttpTestServer); // gate init on the server actually answering (HEAD probe)

// Non-blocking. Returns true if the event was accepted into the pipeline.
bool accepted = Tracker.SendMessage("level_completed");

// Returns true when the BATCH containing this event is actually delivered; false after retries
// are exhausted / on invalid input / when disabled.
bool delivered = await Tracker.SendMapAsync(new Dictionary<string, object>
{
    ["event"] = "purchase",
    ["sku"]   = "coins_500",
    ["price"] = 4.99,
});
```

> Need to manage the instance yourself — DI, multiple trackers, deterministic tests? Construct
> `new TrackingSystem(…)` directly; the static `Tracker` is a thin facade over it.

`SendMessage` never blocks the game frame. `SendMapAsync`'s `Task<bool>` is made *meaningful*: it is
backed by a per-event `TaskCompletionSource` that resolves when the batch carrying the event is
delivered by the background worker.

---

## Architecture

Data flows **producer → bounded queue → background worker → transport**, with an interface on every
seam so network, disk, and time can be faked in tests.

```
   Public API (ITracker)
   SendMessage(string)  /  SendMapAsync(Dictionary)
            │  validate → enrich into TrackingEvent (id, ts, session, user, sdk, platform, appVersion, device context)
            ▼
   ┌──────────────────────────────┐
   │  EventQueue                  │  thread-safe, bounded, FIFO, drop policy
   └──────────────┬───────────────┘
                  │  flush triggers: batch size / flush interval / explicit flush
                  ▼
   ┌──────────────────────────────┐      ┌────────────────────┐
   │  EventDispatcher (worker)    │─────▶ │  IEventStore       │  durable backlog (FileEventStore)
   │  batch · retry+backoff       │      │  reload on start    │
   │  connectivity · circuit brkr │      │  persist on quit    │
   │  dead-letter · metrics       │      └────────────────────┘
   └──────────────┬───────────────┘
                  ▼
        ┌────────────────────┐
        │   ITransport       │  Simulated (default) ⇆ Http (optional live receiver example)
        └────────────────────┘
```

**Key seams (the basis of every test):** `ITransport`, `IEventStore`, `IClock`, `IDelayer`,
`IConnectivity`, `IConnectivityProbe`, `IRuntimeInfo`, `ITrackingLogger`, `IDeadLetterSink`. Injecting fakes for network,
disk, and time makes batching, retries, backoff, persistence, and the circuit breaker fully
deterministic — no `Thread.Sleep`, no flaky timing.

---

## Implicit requirements — the scorecard

These were anticipated unprompted and implemented (with the reasoning that justifies each):

| # | Requirement | How it's handled |
|---|---|---|
| 1 | Non-blocking hot path | API enriches + enqueues, never waits on I/O |
| 2 | Thread-safety | `EventQueue` is lock-guarded; callable from any thread |
| 3 | I/O off the main thread | `EventDispatcher` runs on a background worker |
| 4 | Batching | One request per N events **or** per flush interval |
| 5 | Bounded buffer + drop policy | `OverflowPolicy.DropOldest` (default) / `RejectNew` |
| 6 | Retries: exp backoff + jitter, max attempts | `RetryPolicy` (equal jitter), give-up → `false` |
| 7 | Durable persistence | `FileEventStore` (atomic writes); reload on start |
| 8 | Lifecycle flush | `TrackingLifecycle` persists on pause/quit |
| 9 | Idempotency | Stable event `Id`; reused on retries for server dedupe |
| 10 | Metadata enrichment | timestamp, sessionId, userId, sdkVersion, platform, appVersion + device context (model, OS, network, timezone, locale, bundle) |
| 11 | Error isolation | API never throws into game code (swallow + log) |
| 12 | Configurability | userId, endpoint (enum or custom), batch size, intervals, capacity, retries, log level, … |
| 13 | Privacy / opt-out | `SetEnabled(false)` + `Purge()` (GDPR delete); `SetPrivacyMode(true)` (anonymous mode — userId → `"anonymous"`) |
| 14 | Testability via DI | 9 injectable seams; 147 tests (145 deterministic + 2 live) |
| 15 | Cancellation / Dispose | `CancellationToken` through the worker; clean shutdown |
| 16 | Connectivity-awareness | hold while offline, flush when back online |
| 17 | Circuit breaker | stop hammering a down server; half-open trial after cooldown |
| 18 | Dead-letter queue | give-up events preserved for inspection/replay |
| 19 | Diagnostics + logging hook | live metrics + pluggable `ITrackingLogger` with leveled trace logging (`MinLogLevel`: Warning/Info/Debug) |

**Delivery semantics:** at-least-once. Events survive crashes via the store and may be re-sent; the
idempotency `Id` lets the server de-duplicate.

**Deliberately out of scope** (seen, but intentionally not over-built): gzip compression, request
auth/signing, server-side rate limiting, PII scrubbing, and a WebGL `UnityWebRequest` transport
(noted in [DESIGN.md](KnowledgeBase/Documentation/DESIGN.md)).

---

## Running the tests

The whole suite is **EditMode** and deterministic. From the Unity Editor: *Window → General → Test
Runner → EditMode → Run All*. Headless (CI-style), with the Editor closed:

```bash
"/Applications/Unity/Hub/Editor/2022.3.62f3/Unity.app/Contents/MacOS/Unity" \
  -runTests -batchmode -nographics \
  -projectPath tracking-sdk \
  -testPlatform EditMode \
  -testResults results.xml -logFile test.log
# => test-run result="Passed" total="147" passed="147" ... skipped="0"  (2 are live tests that POST to the deployed receiver — need network)
```

---

## Transports & the optional live receiver example

`ITransport` has two implementations, selected by `TrackingConfig.TransportMode`:

- **`SimulatedHttpTransport`** (default) — the SDK's default runtime transport simulates the POST, so
  the **SDK itself** needs no live server (honouring "real backend out of scope"). It can also inject
  transient failures offline (`TrackingConfig.SimulatedFailPercent`) to exercise retries / circuit
  breaker / dead-letter without any network — that's what `ServerEnvironment.FakeServerChaos` selects.
- **`HttpTransport`** — real `HttpClient` POST of the JSON batch. `HttpClient` (not `UnityWebRequest`)
  because delivery runs on a background thread. A matching live test receiver
  ([`Documentation~/track.php`](tracking-sdk/Packages/com.dmytroudovychenko.tracking/Documentation~/track.php))
  ships with the package — a minimal PHP endpoint that accepts `POST {"events": [...]}`, validates
  the payload, logs each request to `track.log`, and returns `200 {"ok": true}`. Append `?fail=20`
  for chaos mode (~20% transient `503`s to exercise retry/backoff). This is a developer diagnostic
  stub, not a backend service required by the SDK. Two live tests (`LiveTransportTests`,
  `LiveRetryTests`) exercise the real round-trip against the deployed instance as part of the default
  suite, so a full headless run needs network.

`ServerEnvironment` is a small **transport × chaos matrix**, not a set of real environments:

| Value | Transport | Network | Chaos |
|-------|-----------|---------|-------|
| `FakeServer` (default) | simulated | none | — |
| `FakeServerChaos` | simulated | none | ~20% transient failures (`SimulatedFailPercent`) |
| `HttpTestServer` | real HTTP | yes | — |
| `HttpTestServerChaos` | real HTTP | yes | ~20% transient 503s (`?fail=20` preset) |

Both HTTP values resolve to the **one test stub** (`Documentation~/track.php`); the chaos variant
just adds the `?fail=20` query. The bundled demo exposes all four as init buttons — *Fake (clean)*,
*Fake (chaos)*, *Test stub (clean)*, *Test stub (chaos)*. For your own delivery, skip the enum and pass
your endpoint: `Tracker.Init(userId, "https://your.host/track.php")`.

---

## Repository layout

```
tracking-sdk/                                 ← repo root (working name)
  README.md                                    ← project overview (this file, stays at root)
  dist/                                        ← built UPM tarball (npm pack output, .tgz)
  KnowledgeBase/
    INDEX.md  BehaviourRules/                  ← dev conventions + on-demand rules
    Documentation/                             ← authored docs:
      DESIGN.md                                ← design rationale
      TASK_PROGRESS.md                         ← build log + original plan + AI-workflow (committed)
      BUSINESS_LOGIC.md                        ← high-level overview + Mermaid flowcharts (§13)
      BUSINESS_LOGIC_INTENT.md                 ← developer-authored intent log (source)
      STATIC_FACADE.md  WARNINGS.md            ← subsystem detail + open issues
  tracking-sdk/                                ← the Unity project (open & run tests)
    Assets/TrackingDemo/                       ← runnable on Play (auto-spawns the demo)
    Packages/com.dmytroudovychenko.tracking/   ← the package → npm pack → .tgz
      Runtime/  Tests/  Samples~/BasicUsage/
      package.json  CHANGELOG.md  README.md  LICENSE.md
      Documentation~/                          ← shipped with the package (in tarball):
        index.md                               ←   overview, quick start, package layout
        api-reference.md                       ←   full public API reference
        configuration.md                       ←   every tunable with defaults
        architecture.md                        ←   pipeline internals, DI seams, delivery semantics
        track.php                              ←   live test receiver (PHP stub) — deploy on any PHP hosting
```

---

## AI usage note

The architecture, the implicit-requirements analysis, this phased plan, and the incremental
implementation were developed in a working session with an AI assistant (Claude / Claude Code). The
AI helped (a) enumerate the production-grade requirements implicit in a real event pipeline, (b)
design the queue→dispatcher→transport architecture with DI seams, and (c) drive implementation
phase-by-phase with tests kept green at every step. `DESIGN.md` and `TASK_PROGRESS.md` (the latter
carries the original pre-implementation plan and the AI-workflow record in its appendix, under
`KnowledgeBase/Documentation/`) are retained as the record of that workflow.
All code was reviewed and verified (every phase ended on a green headless test run).
