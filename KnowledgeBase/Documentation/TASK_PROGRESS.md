# Task Progress

> Living progress log for the Tracking SDK project. Updated at the end of every phase.
> Plan of record: the original pre-implementation plan is preserved as an [appendix](#appendix-original-implementation-plan) at the end of this doc. Current package id: **`com.dmytroudovychenko.tracking`** (namespace `DmytroUdovychenko.Tracking`).

**Last updated:** 2026-06-25 — **All 8 phases done**, plus a post-review hardening pass, a
cross-vendor self-review (GPT → Gemini), a `Tracker.Init` + `userId` initialization API, a
**static `Tracker` facade** over the renamed `TrackingSystem` instance core, the
**BLI backlog** (intent doc BLI-001/005/006/007 + one fix), a Canvas demo refresh, and a
**`ServerEnvironment` rework** (4 transport×chaos variants + client-side simulated chaos).
**147 EditMode tests** — 145 deterministic + 2 live tests that POST to
`https://udovychenko.xyz/test/track.php`. The prior 82 were verified green (incl. live retry recovery
from injected 503s); later passes added 16 facade tests (`StaticFacadeTests`), a connectivity
main-thread fix (+1), an **offline init gate + `ServerEnvironment.FakeServer`** (+5), **async
server-reachability init** (`InitAsync`/`IsServerReachableAsync`, +8), the **BLI backlog** (+11),
and the **`ServerEnvironment` rework** (+17).
**Headless EditMode gate green: 147/147.**
See "Connectivity main-thread fix", "Static facade", "Initialization API" (offline gate + InitAsync),
"Cross-vendor self-review", and "BLI backlog" below.

---

## Snapshot

| Phase | Title | Status |
|------:|-------|--------|
| 0 | Scaffolding | ✅ Done — 1/1 EditMode test green (headless batchmode) |
| 1 | Public API + event model + validation | ✅ Done — 14/14 EditMode tests green |
| 2 | Queue + dispatcher + batching | ✅ Done — 27/27 EditMode tests green |
| 3 | Reliability: retries + backoff + cancellation | ✅ Done — 35/35 EditMode tests green |
| 4 | Durability: persistence + lifecycle flush | ✅ Done — 43/43 EditMode tests green |
| 5 | Showcase extras (all five) | ✅ Done — 60/60 EditMode tests green |
| 6 | Real transport (GoDaddy) + dual-mode | ✅ Done — 65/65 units + **live test passed** (HTTP 200) |
| 7 | Sample + packaging + docs | ✅ Done — sample, README/DESIGN, `.tgz` verified |
| — | Post-review hardening | ✅ Done — 5 fixes + 5 new tests → 70/70 EditMode tests green |
| — | Static facade (`Tracker` over `TrackingSystem`) | ✅ Done — +16 tests → **98** (headless gate green) |
| — | Connectivity main-thread fix | ✅ Done — +1 test → **99**, headless gate green |
| — | Offline init gate + `FakeServer` | ✅ Done — +5 tests → **104**, headless gate green |
| — | Async server-reachability init (`InitAsync`) | ✅ Done — +8 tests → **112**, headless gate green |
| — | BLI backlog (BLI-001/005/006/007 + reload-drop fix) | ✅ Done — +11 tests → **123**, headless gate green |
| — | Canvas demo refresh | ✅ Done — Canvas init/send/error controls; **128/128** headless gate green |
| — | Review-fix pass (GPT → Gemini) | ✅ Done — 4 code + 7 doc/packaging findings fixed; +1 test; count corrected 123→**128**; headless gate green |
| — | `MinLogLevel` step-trace logging | ✅ Done — `TrackingConfig.MinLogLevel` + `LevelFilteringTrackingLogger`; init/enqueue/deliver traces (Info) + payload/JSON (Debug); `Init(…, minLogLevel)`; demo at Debug; net +1 test → **130/130** headless gate green |
| — | `ServerEnvironment` rework (transport×chaos) | ✅ Done — renamed to `FakeServer`/`FakeServerChaos`/`HttpTestServer`/`HttpTestServerChaos` (dropped `Development`); client-side chaos in `SimulatedHttpTransport` (warns on each drop, like a 503); demo shows all 4; +17 tests → **147/147** headless gate green |

Legend: ✅ done · 🟡 in progress · ⬜ not started

---

## ServerEnvironment rework — transport×chaos matrix ✅

> Replaced the misleading `Fake`/`Production`/`Staging`/`Development` quartet with a symmetric
> **transport × chaos** matrix. The old "Production/Staging/Dev" names implied separate real backends;
> in reality all three hit one test receiver, differing only by clean vs `?fail=20`.

### Done
- ✅ **Enum** `ServerEnvironment` now: `FakeServer = 0` (simulated, clean), `FakeServerChaos = 1`
  (simulated, ~20% transient failures), `HttpTestServer = 101` (real HTTP, clean), `HttpTestServerChaos = 102`
  (real HTTP, `?fail=20`). 100-block group scheme (simulated 0-block / HTTP 100-block); `Development` removed.
- ✅ **Client-side chaos** in `SimulatedHttpTransport`: new `failPercent` (clamped [0,100]) + an injectable
  `nextRoll` DI seam (default per-instance `System.Random`); a positive rate fails sends transiently
  (returns `false`) so the retry / circuit-breaker / dead-letter pipeline runs **offline**, no server needed.
  Each injected drop logs a `Warning` (mirroring `HttpTransport`'s 503 log) so `FakeServerChaos` is as visible as the HTTP chaos.
- ✅ **Resolvers/config:** `TrackingConfig.SimulatedFailPercentFor(server)` + `SimulatedFailPercent` field +
  `CHAOS_FAIL_PERCENT` constant; endpoint constants renamed (`HTTP_TEST_ENDPOINT`, `HTTP_TEST_CHAOS_ENDPOINT`).
  `TrackingSystem.TransportModeFor` maps both `FakeServer*` → `Simulated`, both `HttpTestServer*` → `Http`.
- ✅ **Demo** shows all four (two fake buttons + two HTTP buttons; built in code, no prefab edit).
- ✅ **Tests +17** (→ 147): `EndpointFor`/`TransportModeFor`/`SimulatedFailPercentFor` mappings +
  `SimulatedTransportTests` (failPercent boundary; transient-recover; permanent → dead-letter; chaos-drop warns; clean send silent).
- ⚠️ **Breaking** public-API change (renamed/removed enum members) — logged under CHANGELOG `[Unreleased]`;
  version bump deferred to the developer (a major bump at release).

---

## Review-fix pass — cross-vendor (GPT → Gemini) ✅

> A second two-reviewer pass (GPT 5.5 via Codex, then Gemini 3.1 Pro High via Antigravity) scoped to the SDK
> package, the demo, and `KnowledgeBase/Documentation`. GPT raised 4 code + 7 doc/packaging findings; Gemini
> rejected the deliberate ones and added 2 concurrency findings. All confirmed items were fixed and re-verified.

### Done
- ✅ **In-flight batch never strands on Dispose.** `EventDispatcher` tracks the active batch and
  `FailRemainingAwaiters` now fails it too, so a custom transport that ignores cancellation past
  `ShutdownDrainTimeout` can't leave a `SendMapAsync` `Task` hanging. +1 deterministic shutdown test.
- ✅ **Owned transport teardown coordinated.** `TrackingSystem.Dispose` disposes an owned transport only once
  `EventDispatcher.WorkerStopped`, never out from under a still-running worker.
- ✅ **Enqueue can't strand an awaiter when raced by Dispose** — the `ObjectDisposedException` from a disposed
  signal is caught and the awaiter failed.
- ✅ **`Tracker.Init(TrackingSystem)` disposes the rejected instance** when already initialized (no leaked worker).
- ✅ **Packaging:** restored the advertised `Samples~/BasicUsage` sample and added an `.npmignore` negation
  (`!Samples~/` + `!Samples~/**`) so the UPM `Samples~` folder survives the `*~` backup-file ignore that had been
  dropping it from the `npm pack` tarball (`.npmignore` overrides `.gitignore` for `npm pack`, so the negation has
  to live there). Verified: the `1.0.0` `.tgz` now bundles `Samples~/BasicUsage/` (179 files).
- ✅ **Docs:** corrected the stale test count (123 → **128**) everywhere; fixed the CHANGELOG `Development`
  contradiction + duplicate `### Changed`; scoped the `BUSINESS_LOGIC.md` init-throw claim; marked BLI-002/003/004
  `Done`; refreshed the plan-of-record status.
- ❌ **Rejected (deliberate):** init `ArgumentException` on blank `userId` (DESIGN.md §8 setup-time validation);
  lifecycle "persist snapshot, not network flush" (DESIGN.md §5).

---

## BLI backlog ✅

> Implemented the developer-authored intents in [`BUSINESS_LOGIC_INTENT.md`](BUSINESS_LOGIC_INTENT.md) that
> were still `Proposed`. Driven by a full audit (18-agent, adversarially verified) of all 7 BLI entries vs the
> code: BLI-002/003/004 were already faithful to their flows; these four + one fix were the gaps.

### Done
- ✅ **BLI-007 — device context.** Added `deviceModel`, `osVersion`, `networkType` (wifi/cellular/none),
  `timezone` (UTC offset), `locale`, `bundleId` end-to-end: `IRuntimeInfo`/`UnityRuntimeInfo` →
  `TrackingEvent` → `CreateEvent` → `EventSerializer` (round-trip) → optional `track.php` receiver example
  (accepts via raw-body log).
  `UnityRuntimeInfo` snapshots all fields in its ctor (main thread) so off-thread enrichment never calls
  `Application.internetReachability` (main-thread-only). `carrier` skipped (permission-gated). +2 tests
  (device enrichment + a forbidden-fields guard: no device-id/IDFA/IP/location/carrier).
- ✅ **BLI-006 — anonymous (privacy) mode.** New `TrackingConfig.PrivacyMode` (default off),
  `TrackingSystem.SetPrivacyMode`/`IsPrivacyMode`, and `Tracker` facade. `CreateEvent` stamps userId
  `"anonymous"` when on (sessionId + context kept). **Decided (open question e): forward-only** — buffered
  identified events still deliver (no retroactive scrub). +6 tests.
- ✅ **BLI-001 — init step logging.** Per-step `Debug` logs (fake-skip, device-online, server-reachable,
  final result) in `Tracker.InitAsync` + `HttpConnectivityProbe`; connectivity failures now log at `Error`
  (was `Warning`). +3 probe log-level tests.
- ✅ **BLI-005 — `Tracker.Shutdown()` → `Tracker.Dispose()`** (single public teardown name; behaviour
  unchanged; instance `TrackingSystem.Dispose()` stays the real `IDisposable`). BREAKING — logged under
  CHANGELOG `[Unreleased]`; version stays `1.0.0` (the SemVer bump + release is a user action).
- ✅ **Fix (from the audit):** `ReloadPersistedBacklog` now counts overflow drops (metric + summary warning),
  mirroring the live enqueue path — reload-time data loss is no longer silent.

### Out of scope (logged to [`WARNINGS.md`](WARNINGS.md))
- Half-open probe-failure → dead-letter is now documented in `DESIGN.md §6` (was an undocumented behaviour).
- Remaining test-coverage nits (SendMapAsync RejectNew Task, FlushAsync warning/swallow) and two
  developer-owned intent-prose imprecisions (BLI-002 sdkVersion, BLI-004 "queue empty" wording) — surfaced,
  not auto-edited.

---

## Connectivity main-thread fix ✅

> **Bug (pre-existing, surfaced by `InitTests`):** `UnityConnectivity.IsOnline` read
> `Application.internetReachability` — a **main-thread-only** Unity API — but the dispatcher's `CanSend`
> gate runs on the background delivery worker. Off-thread it threw `UnityException:
> get_internetReachability can only be called from the main thread`, failing the two `Init` tests flakily
> (race: did the worker reach the gate before `Dispose`). In a real build it would throw on every
> offline-gate check too.

### Done
- ✅ `UnityConnectivity` now caches a `volatile` snapshot taken on the main thread (ctor + `Refresh()`);
  `IsOnline` returns the cached value, so the worker never touches a main-thread-only API.
- ✅ `TrackingLifecycle.Update()` re-polls connectivity each frame on the main thread (production
  liveness for offline→online), routed via `TrackingSystem.RefreshConnectivity()` (no-op for fakes /
  `AlwaysOnlineConnectivity`).
- ✅ +1 regression test (`ConnectivityTests.UnityConnectivity_IsOnline_DoesNotThrow_FromWorkerThread`):
  reading `IsOnline` from a `Task.Run` thread must not throw. **Headless gate green: 99/99.**

## Initialization API — `Tracker.Init` + `userId` ✅

**Goal:** a single, intention-revealing initialization command with a required `userId` and three ways
to choose the destination — default, a named server (enum), or a custom endpoint.

### Done
- ✅ `ServerEnvironment` enum (`FakeServer` / `FakeServerChaos` / `HttpTestServer` / `HttpTestServerChaos`) → URL via `TrackingConfig.EndpointFor`.
  Endpoints: `FakeServer`/`FakeServerChaos` = the offline fake host (simulated; the chaos twin injects ~20% transient
  failures client-side), `HttpTestServer` `…/track.php`, `HttpTestServerChaos` `…/track.php?fail=20` (chaos, real HTTP).
  (Renamed 2026-06-25 from `Production`/`Staging`/`Development`; see "ServerEnvironment rework" below.)
- ✅ Three `Tracker.Init` overloads: `Init(userId)` (default, simulated) · `Init(userId, ServerEnvironment)`
  (named server; `FakeServer*` simulated, `HttpTestServer*` real HTTP) · `Init(userId, endpoint)` (custom, real HTTP).
  `userId` is **required** — blank/null throws `ArgumentException`.
- ✅ **Offline init gate**: targeting a real HTTP server while the device is offline makes `Init` a no-op
  (factory returns `null`, facade stays uninitialized, one warning logged); `FakeServer*`/`Init(userId)` bypass it.
  Logic: `TrackingSystem.IsBlockedOffline` (keyed on the resolved `TransportMode`).
- ✅ **Async server-reachability init**: `Tracker.InitAsync(userId, server|endpoint)` + `Tracker.IsServerReachableAsync(endpoint)`
  check the interface (fast-fail), then **ping the target server** (HEAD; any HTTP response — even 405 from `track.php` —
  = reachable; only transport error/timeout = down) and init only if it answers; `FakeServer*` skips the check.
  Distinct reason (no internet / server down) is logged. Post-probe init resumes on the main thread (Unity objects).
- ✅ `Init` is the **production entry point**: wires durable persistence (`FileEventStore`) + connectivity-awareness
  (`UnityConnectivity`) by default, so a one-line `Tracker.Init("user")` is production-grade. The DI constructor
  stays bare (Null store / always-online) for deterministic tests. The demo now initializes via `Tracker.Init`.
- ✅ `userId` is first-class: `TrackingConfig.UserId`, stamped on every `TrackingEvent`, round-trips through
  `EventSerializer` (persistence **and** the wire), and is exposed as `Tracker.UserId`.
- ✅ +9 deterministic tests (`InitTests` + `userId` enrichment/round-trip) → **82/82 green**.

---

## Static facade — `Tracker.X` over `TrackingSystem` ✅

> **Naming:** the concrete pipeline class called `Tracker` throughout the earlier log is now
> `TrackingSystem`; the name **`Tracker` now denotes the static facade**. `new Tracker(...)` →
> `new TrackingSystem(...)`; `TrackingSystem.Init(...)` is the instance factory, `Tracker.Init(...)`
> configures the global facade.

**Goal:** make the public entry point `Tracker.Init(userId)` then `Tracker.SendMessage(...)` — no
instance to hold or pass — without losing the DI seams the test suite depends on.

### Done
- ✅ Renamed concrete `Tracker` → `TrackingSystem` (file + class + every test/doc/demo reference). It stays
  public — still the DI/testing surface and the multiple-trackers path.
- ✅ New `public static class Tracker` (`Runtime/Tracker.cs`): holds one `TrackingSystem` behind `Init(...)`
  and forwards `SendMessage` / `SendMapAsync` / `FlushAsync` / `Persist` / `SetEnabled` / `Purge` plus
  `IsEnabled` / `Metrics` / `DeadLetter` / `SessionId` / `UserId` / `Current` / `IsInitialized`. Adopt
  overload `Init(TrackingSystem, attachLifecycle)` for advanced DI / tests.
- ✅ Static-state edge cases closed: call-before-Init no-ops safely (never throws, never hangs; warns once),
  double-Init is ignored (keep first; `Dispose()` to reconfigure), production overloads auto-wire lifecycle
  persistence + quit dispose, and `[RuntimeInitializeOnLoadMethod]` resets the statics when domain reload is
  off (also what isolates the EditMode tests).
- ✅ Both demos converted to the facade — dropped the held instance, the manual `TrackingLifecycle.Attach`,
  and `OnDestroy`.
- ✅ Adversarial multi-dimension review (5 lenses → verify) → 2 medium fixes: `Shutdown` now disposes the
  tracker even if lifecycle teardown throws (no leak, no escape); `Adopt` publishes `m_instance` **last** and
  rolls back on a failed wiring (no half-init global). Plus a demo `DeadLetter?.Count ?? 0` null-guard.
- ✅ +16 deterministic tests (`StaticFacadeTests`) → **98** total (96 deterministic + 2 live).
- ✅ Headless EditMode gate **green** — re-verified on a later headless run (the Editor was open during this
  specific change; the gate was run once it was closed).
- 📄 Rationale: `DESIGN.md` §8a + [`KnowledgeBase/Documentation/STATIC_FACADE.md`](STATIC_FACADE.md).

---

## Phase 0 — Scaffolding ✅

**Goal:** Clean UPM package skeleton with the Test Runner wired and one trivial green test.

### Done
- ✅ Confirmed environment: Unity `2022.3.62f3` (matches `ProjectVersion.txt`), Test Framework `1.1.33`, npm `11.9.0`.
- ✅ Locked package id / namespace: **`com.dmytroudovychenko.tracking`** / `DmytroUdovychenko.Tracking`.
- ✅ Created embedded package at `tracking-sdk/Packages/com.dmytroudovychenko.tracking/`:
  - `package.json` (UPM manifest, `unity: 2022.3`, MIT, version `1.0.0`).
  - `Runtime/DmytroUdovychenko.Tracking.asmdef` + `Runtime/TrackingSdk.cs` (version constant).
  - `Tests/Editor/DmytroUdovychenko.Tracking.Tests.asmdef` (EditMode, references Runtime + TestRunner + nunit).
  - `Tests/Editor/SmokeTests.cs` (one trivial green `[Test]`).
  - `README.md`, `CHANGELOG.md`, `LICENSE.md`.
- ✅ Repo-root `.gitignore` (Unity-aware, matches the nested project).
- ✅ `TASK_PROGRESS.md` (this file).
- ✅ Ran EditMode tests **headless** (batchmode `-runTests`): **1/1 passed**, exit code 0, no compile
  errors. Unity imported the package and generated all `.meta` files.

### Verification (reproducible)
```bash
"/Applications/Unity/Hub/Editor/2022.3.62f3/Unity.app/Contents/MacOS/Unity" \
  -runTests -batchmode -nographics \
  -projectPath tracking-sdk \
  -testPlatform EditMode \
  -testResults /tmp/unity-editmode-results.xml \
  -logFile /tmp/unity-editmode.log
# => test-run result="Passed" total="1" passed="1" failed="0"
```
> Requires the Unity editor to be **closed** (single-instance project lock).

### Exit criteria — met ✅
- Project opens; Test Runner discovers `DmytroUdovychenko.Tracking.Tests`; smoke test passes.

---

## Phase 1 — Public API + event model + validation ✅

**Goal:** The public API, the enriched event model, input validation, and the DI seams — with no
real network — all covered by deterministic tests.

### Done
- ✅ Public API: `ITracker` (`bool SendMessage(string)`, `Task<bool> SendMapAsync(Dictionary<string,object>)`)
  + `Tracker` implementation.
- ✅ Event model: `TrackingEvent` (immutable, id + full metadata envelope), `TrackingEventType`.
- ✅ `TrackingConfig` (forward-looking, documented defaults; `Enabled` gate active now).
- ✅ DI seams: `ITransport` (+ `NullTransport` default), `IClock` (+ `SystemClock`),
  `IRuntimeInfo` (+ `UnityRuntimeInfo`) — UnityEngine isolated behind a seam.
- ✅ Behaviour: non-blocking hot path, error isolation (never throws into game code), metadata
  enrichment from injected clock/runtime, defensive payload snapshot (caller mutation can't leak),
  stable per-tracker session id + unique per-event idempotency id.
- ✅ Tests (14/14 green): `ValidationTests` (6), `MetadataEnrichmentTests` (3),
  `EventCreationTests` (4), `SmokeTests` (1). Fakes: `RecordingTransport`, `FakeClock`, `FakeRuntimeInfo`.

### Deferred to later phases (intentional seams already in place)
- `SendMapAsync` currently returns the transport result directly; Phase 2 rewires it to a per-event
  `TaskCompletionSource` resolved on **batch** delivery.
- `Tracker → transport` is a direct call; Phase 2 inserts the bounded `EventQueue` + `EventDispatcher`.

### Exit criteria — met ✅
- Tests for validation, metadata enrichment, and event creation all pass headless.

---

## Phase 2 — Queue + dispatcher + batching ✅

**Goal:** The real non-blocking pipeline — bounded queue, background batching worker, simulated
transport — with the `SendMapAsync` async result made *meaningful* (resolves on batch delivery).

### Done
- ✅ `EventQueue` (internal): thread-safe, bounded, FIFO; `OverflowPolicy.DropOldest` (default) /
  `RejectNew`; tracks `DroppedCount`; returns the evicted item so its awaiter can be failed.
- ✅ `EventDispatcher` (internal): background worker (`Task.Run` loop + `SemaphoreSlim` signal);
  batches by **size** (≥ `BatchSize`) or **time** (oldest event age ≥ `FlushInterval`), driven by the
  injected `IClock`; `PumpOnceAsync` (due batches) + `DrainAsync` (force-flush); best-effort drain on
  shutdown; `IDisposable`.
- ✅ `SimulatedHttpTransport` (public) — new production default for `new TrackingSystem()`.
- ✅ `Tracker` rewired: validate/enrich → bounded queue → dispatcher → transport. `SendMapAsync`
  resolves its `Task<bool>` when the **batch** is delivered (per-event `TaskCompletionSource`,
  `RunContinuationsAsynchronously`); evicted/rejected ⇒ `false`. Adds `FlushAsync()` + `Dispose()` +
  `startWorker` ctor flag (tests pump deterministically).
- ✅ `[InternalsVisibleTo]` so tests white-box the queue/dispatcher.
- ✅ Tests (27/27 green): `EventQueueTests` (4), `BatchingTests` (4: N→1, partial-hold, time-flush on
  virtual clock, FIFO across batches), `AsyncDeliveryTests` (5: pending-until-delivered, false on
  failure, false on eviction, RejectNew→false, DropOldest→true). Phase 1 suites migrated to the
  `startWorker:false` + `FlushAsync()` deterministic model.

### Design notes
- `DropOldest` default keeps the producer non-blocking and memory bounded (prefers recent telemetry);
  `RejectNew` gives the documented "queue full ⇒ `SendMessage` false" behaviour. Both tested.
- No retries yet — a failed send surfaces `false`. Phase 3 inserts the retry/backoff policy.

### Exit criteria — met ✅
- Batching (N→1), FIFO, drop policy, async-resolves-true-on-delivery, virtual clock — all green.

---

## Phase 3 — Reliability: retries + backoff + cancellation ✅

**Goal:** Survive flaky networks without hammering the server, and shut down cleanly.

### Done
- ✅ `RetryPolicy` (public): capped exponential backoff with **equal jitter** (delay ∈ [base/2, base]),
  `MaxAttempts`. Pure/side-effect-free; injectable jitter source for exact assertions.
- ✅ `IDelayer` (+ `TaskDelayer`): the "wait between attempts" seam, so backoff is tested with **no real
  delays**.
- ✅ `EventDispatcher` send path retries transient failures, gives up after `MaxRetryAttempts`
  (awaiter ⇒ `false`), and honours cancellation mid-backoff. Same idempotency id reused on retry.
- ✅ `Tracker` threads an optional `IDelayer` through for deterministic tests.
- ✅ Tests (8 new, 35/35 total green): `RetryPolicyTests` (4: final-attempt stop, jitter bounds, cap,
  zero-jitter exactness), `RetryDispatchTests` (4: succeed-after-failures, give-up count, exact backoff
  sequence, cancel-during-backoff).

### Exit criteria — met ✅
- Retry counts, backoff timing, give-up path, cancellation — all green and deterministic.

---

## Phase 4 — Durability: persistence + lifecycle flush ✅

**Goal:** Buffered events survive a crash/kill and are resent on next start.

### Done
- ✅ `IEventStore` + `FileEventStore` (atomic temp+move writes under `persistentDataPath`; resilient to
  missing/corrupt files), `InMemoryEventStore`, `NullEventStore` (safe default — no disk I/O).
- ✅ `EventSerializer`: type-tagged JSON via Unity's dependency-free `JsonUtility`, so `object` payloads
  (string/bool/int/long/float/double/null) round-trip without an external JSON library.
- ✅ `Tracker` reloads the persisted backlog on construction; `Persist()` snapshots the live queue
  (non-destructive). `TrackingLifecycle` MonoBehaviour persists on `OnApplicationPause(true)`/`Quit`.
- ✅ Delivery is **at-least-once**; the event idempotency id lets the server de-duplicate resends.
- ✅ Tests (8 new, 43/43 total green): `PersistenceTests` (3), `FileEventStoreTests` (4),
  `LifecycleTests` (1).

### Exit criteria — met ✅
- persist→restart→resend; flush on shutdown; corrupt-file resilience — all green.

---

## Phase 5 — Showcase extras (all five) ✅

**Goal:** Round out the production story with the remaining implicit requirements.

### Done
- ✅ **Connectivity-awareness** — `IConnectivity` / `UnityConnectivity` / `AlwaysOnlineConnectivity`
  (default). Dispatcher holds events while offline, flushes when back online. Opt-in (default always-on
  to keep `new TrackingSystem()` + tests deterministic).
- ✅ **Circuit breaker** — `CircuitBreaker` (Closed/Open/HalfOpen, virtual-clock cooldown). Opens after
  N consecutive batch failures; dispatcher stops attempting until cooldown → half-open trial.
- ✅ **Dead-letter queue** — `IDeadLetterSink` / `InMemoryDeadLetterQueue` (bounded). Give-up events are
  preserved for inspection/replay instead of dropped.
- ✅ **Diagnostics + logging hook** — `TrackingMetrics` / `TrackingMetricsSnapshot`
  (enqueued/sent/dropped/retried/givenUp/deadLettered); `ITrackingLogger` (`UnityTrackingLogger`
  default, `NullTrackingLogger`) — SDK never writes to console directly.
- ✅ **Privacy opt-out + purge** — `SetEnabled(false)` (reject + auto-purge), `Purge()` (clears queue +
  dead-letter + persisted store; pending awaiters resolve `false`), `IsEnabled`.
- ✅ Tests (17 new, 60/60 total green): `MetricsTests` (3), `LoggingHookTests` (2), `ConnectivityTests`
  (1), `CircuitBreakerTests` (4), `DeadLetterTests` (3), `PrivacyTests` (4).

### Exit criteria — met ✅
- A test per extra, all green; existing 43 unaffected (permissive defaults).

---

## Phase 6 — Real transport (GoDaddy) + dual-mode ✅

**Goal:** A real HTTP delivery path to an optional live PHP receiver example, swappable with the simulated default.

### Done
- ✅ `HttpTransport` on `HttpClient` (works off the background worker thread — `UnityWebRequest` is
  main-thread-only). Serializes the batch (reusing `EventSerializer`), POSTs JSON, maps 2xx→true.
- ✅ `TransportMode` (`Simulated` default / `Http`) + `TrackingSystem.CreateDefaultTransport` factory; the
  Tracker disposes a transport it owns. Default endpoint stays `fakeserver.com`/simulated.
- ✅ `tools/live-receiver/track.php` optional receiver example (validate → log → `200 {ok,received}`)
  + `tools/live-receiver/README.md` deploy notes.
- ✅ Tests (5 new, 65/65 total green) via a fake `HttpMessageHandler`: 2xx→true, 5xx→false, empty-batch
  no-op, dual-mode factory selection, end-to-end routing through the Tracker.
- ✅ `[Explicit]` `LiveTransportTests` (excluded from CI) for the real endpoint.

### Live end-to-end — done ✅
- ✅ `track.php` deployed to `https://udovychenko.xyz/test/track.php` (PHP 8.3 / Apache); `curl` smoke
  returns `200 {"ok":true,"received":1}`.
- ✅ `TestConstants.LIVE_ENDPOINT` set; ran headless via
  `-testFilter "DmytroUdovychenko.Tracking.Tests.LiveTransportTests"` → **Passed** (real `HttpTransport` POST → 200).

### Exit criteria — met ✅
- HTTP transport + dual-mode unit-tested ✅; live end-to-end against the real endpoint ✅.

---

## Phase 7 — Sample + packaging + docs ✅

**Goal:** Make it runnable, documented, and importable.

### Done
- ✅ Demo (`TrackingSdkDemo`): self-contained Canvas UI — init targets, valid/error send buttons, live
  status, and metric counters, no
  scene wiring. Ships as the package **sample** (`Samples~/BasicUsage`, wired in `package.json`) and as
  a **runnable host copy** (`Assets/TrackingDemo`, auto-spawns on Play). Compiles clean in the project.
- ✅ Docs: full root `README.md` (architecture + implicit-requirements scorecard + AI note),
  `DESIGN.md` (rationale for the non-obvious calls); the AI-workflow record is preserved in the
  [plan appendix](#appendix-original-implementation-plan) of this doc.
- ✅ Packaging: the importable artifact is produced on demand via `npm pack` →
  `com.dmytroudovychenko.tracking-1.0.0.tgz` (bundles `package.json`, `Runtime/`, `Tests/`, `Samples~/BasicUsage/`,
  README/CHANGELOG/LICENSE). Importable via *Package Manager → Add package from tarball*. The `.tgz` is a
  build output (gitignored); rebuild it for the release zip.

### Exit criteria — met ✅
- Sample runs; docs complete; tarball builds and is well-formed.

---

## Post-review hardening ✅

**Goal:** close the bugs and gaps surfaced by a multi-agent review before release.

### Done
- ✅ **Hang fix** — `SendMapAsync` no longer hangs forever when the tracker is disposed while offline /
  breaker-open: `EventDispatcher.RunAsync` now fails any still-buffered awaiters (`false`) after the final
  drain, instead of stranding never-completed `Task`s.
- ✅ **Dead-letter isolation** — a throwing custom `IDeadLetterSink` can no longer strand a batch's
  awaiters: the sink call is wrapped, and per-event `TaskCompletionSource` completion runs in a `finally`.
- ✅ **Circuit breaker** — a half-open probe now makes a **single** attempt instead of re-running the full
  retry budget against a server that is only being probed for recovery.
- ✅ **Logging seam** — `FileEventStore` and `SimulatedHttpTransport` route diagnostics through
  `ITrackingLogger` instead of `UnityEngine.Debug` directly.
- ✅ **Tunable** — the worker shutdown-drain wait is `TrackingConfig.ShutdownDrainTimeout` (was a hard-coded
  `2000` ms).
- ✅ **Tests (+5, 70/70 total green):** dispose-while-offline never hangs, throwing-sink completes awaiters,
  half-open single probe, concurrent-producer thread-safety, null map value preserved.

### Deferred (tracked in [WARNINGS.md](WARNINGS.md))
- Crash-atomic `FileEventStore.Save` (`File.Replace`); cancellable final drain; duplicated literal
  defaults; silent `Dispose` catches; cosmetic naming — all low/nit, none blocking.

---

## Cross-vendor self-review (GPT → Gemini) ✅

**Goal:** a two-reviewer pass (GPT 5.5 via Codex, then Gemini 3.1 Pro High via Antigravity) over the whole
SDK + working tree, fixing what they confirm. GPT raised 1 blocker + 6 majors (5 accepted, 2 rejected);
Gemini then verified the fixes and caught 1 blocker + 2 majors **in the fixes themselves** — all closed.

### Done
- ✅ **Privacy opt-out race** — `TrackingSystem.m_lifecycleGate` serializes the enabled-check + enqueue with
  `SetEnabled(false)`/`Purge`, so an event can no longer slip into the queue after an opt-out has purged.
  (In-memory lock only — no I/O on the hot path. The already-dispatched in-flight batch is inherent
  at-least-once and noted in WARNINGS.)
- ✅ **Bounded final drain** — `EventDispatcher` shutdown drain runs under a `ShutdownDrainTimeout` CTS
  instead of `CancellationToken.None`, so a slow **online** backlog can't outlast `Dispose`. (Closed WARNINGS #2.)
- ✅ **Race-safe teardown** — `Dispose` always calls `FailRemainingAwaiters()` as an idempotent backstop
  (covers never-started **and** timed-out workers) and releases its sync primitives only once the worker
  has actually stopped — no `ObjectDisposedException` surfacing on the worker's next await.
- ✅ **Single owner of I/O** — a `SemaphoreSlim` send-gate serializes `SendBatchAsync`, so `FlushAsync`
  can't run transport I/O concurrently with the worker (and no double half-open probe). Cancel-/dispose-
  during-acquire still completes the batch's awaiters (catches `OperationCanceledException` **and**
  `ObjectDisposedException`), never stranding a `Task`.
- ✅ **Atomic persistence** — `FileEventStore.Save` uses `File.Replace` (temp in the same dir) instead of
  delete-then-move; a crash can no longer leave the destination absent. (Closed WARNINGS #1; docstring now true.)
- ✅ **Error isolation** — `Tracker.FlushAsync` is now `async` + try/catch + log, so a drain racing `Dispose`
  can't fault a `Task` into game code (the last public method that wasn't isolated).
- ✅ **Tests (+1, 71/71 total green):** Dispose completes a buffered awaiter even when the worker never
  started (deterministic, no real worker/time).
- ❌ **Rejected** (recorded): the wall-clock `Wait(5s)` in `ShutdownTests` is a bounded hang-guard on a
  genuinely-threaded shutdown test (returns immediately on completion); `FileEventStoreTests` using a real
  temp file is the legitimate integration test of the disk store (isolated + cleaned up + deterministic).

---

## Appendix: original implementation plan

> The original **pre-implementation** plan, preserved as the AI-workflow record — the "how it was
> planned" companion to the as-built log above. Package ids and paths below reflect the **planning
> stage** (`com.example.tracking` and proposed names); the shipped package is
> `com.dmytroudovychenko.tracking`. For the as-built state and the current test count, see the top of
> this document. (The obsolete "how to continue (next chat)" startup section has been dropped.)

### 1. The requirements (a faithful summary)

Build a **Unity Package** that exposes a small tracking API and tracks the usage of its own
APIs through an **internal event mechanism** that "sends" events to a fake server
(`https://fakeserver.com`).

Public API (two methods on a public class/interface):

```csharp
bool SendMessage(string message);
Task<bool> SendMapAsync(Dictionary<string, object> map);
```

The *business* logic of these methods is intentionally **not** the focus. What matters is
identifying and implementing the **implicit technical requirements of a production-grade event
mechanism that talks to a server**, with **automated test coverage**.

Constraints / requirements:
- Implementing a real backend is **out of scope**; simulate sending the payload to the URL.
- **Production readiness:** anticipate the implicit requirements (queueing, batching, retries,
  offline, persistence, thread-safety, flush on quit, testability, …) and implement them.
- **Test coverage:** the event mechanism and its simulated server interaction must be covered
  by automated tests.
- **Documentation:** `README.md` explaining architecture decisions and the reasoning behind the
  implicit requirements addressed, plus a note on AI usage.
- **Artifacts:** the complete Unity project (open & run tests), the package as an importable
  artifact (`.unitypackage` or UPM `.tgz`), and the project `.md` docs.

#### What this really exercises
The function logic is intentionally minimal. The real focus is the line *"anticipate the
implicit technical requirements."* The goal is to **name the full list of production problems**
(queue, batching, retries, offline, persistence, thread-safety, flush on lifecycle, idempotency,
testability via DI) and implement them cleanly, covered by tests.

---

### 2. Locked design decisions

| Decision | Choice | Rationale |
|---|---|---|
| **Scope / ambition** | **Maximal showcase**, delivered in incremental phases | Each phase = a bit of code + tests + a "try it" checkpoint. |
| **`SendMapAsync` semantics** | `Task<bool>` backed by a **per-event `TaskCompletionSource`** that resolves `true` when **the batch containing the event is actually delivered**, `false` after retries are exhausted | Makes the async signature *meaningful* (correlates an async result to batched delivery) — a strong design point. |
| **Transport** | `ITransport` with **two** implementations: `SimulatedHttpTransport` (default; used by the deterministic tests) **and** a real HTTP transport posting to the **deployed optional receiver example** | The SDK default honors "server out of scope" (runtime + deterministic tests use simulation); 2 live tests exercise the real end-to-end path. Transport is config-swappable. |
| **`SendMessage` semantics** | Synchronous-looking, **non-blocking**; returns `true` if the event is **accepted into the pipeline** (valid + enqueued), `false` on invalid input / disabled / queue full | Keeps the hot path fast; never waits on the network. |
| **Default endpoint** | Keep `https://fakeserver.com` as the documented default; the real receiver URL is supplied via config | The SDK runs with no live server; the 2 live tests need the deployed receiver. |
| **Project location** | A separate, clean repo (this project) | Decoupled from any host app. |
| **Unity version** | `2022.3.62f3` | Matches the installed editor (LTS baseline). |
| **Package id / namespace** | Proposed default `com.example.tracking` / `Example.Tracking` — **confirm at Phase 0** (alt: `com.dmytroudovychenko.tracking`) | Easily renamed early. |

---

### 3. Architecture

Data flow: **producer → bounded buffer → background worker → transport**, with interfaces on
every seam for deterministic testing.

```
   Public API (ITracker)
   SendMessage(string)  /  SendMapAsync(Dictionary)
            │  validate → wrap in TrackingEvent (id, ts, session, metadata)
            ▼
   ┌─────────────────────────────┐
   │  EventQueue (thread-safe,    │  ← callable from any thread, non-blocking
   │  bounded, drop-policy)       │
   └─────────────┬───────────────┘
                 │  flush triggers: batch size / timer / on quit
                 ▼
   ┌─────────────────────────────┐     ┌──────────────────┐
   │  EventDispatcher (worker)    │────▶│  IEventStore     │  durable persistence
   │  batching + retry + backoff  │     │  file / memory   │
   └─────────────┬───────────────┘     └──────────────────┘
                 ▼
        ┌──────────────────┐
        │   ITransport     │  ← seam for tests AND for "fakeserver.com" / GoDaddy
        │ Simulated / Http │
        └──────────────────┘
```

**Key seams (the basis of all tests):** `ITransport`, `IEventStore`, `IClock`. Injecting fakes
for network / disk / time makes retries, backoff, batching and persistence fully deterministic.

---

### 4. Implicit requirements — the actual scorecard

These are documented in `README.md` with rationale. **Implemented:**

| # | Requirement | Why |
|---|---|---|
| 1 | Non-blocking hot path | API returns instantly; never stalls the game frame |
| 2 | Thread-safety | API may be called from any thread; queue is concurrent |
| 3 | I/O off the main thread | Sending must not drop frames |
| 4 | Batching | One request per N events — saves network/battery |
| 5 | Bounded buffer + drop policy | Backpressure; bounded memory when offline |
| 6 | Retries: exponential backoff + jitter, max attempts | Survive flaky networks without hammering the server |
| 7 | Durable persistence | Survive crash/kill; flush on pause/quit; reload on start |
| 8 | Lifecycle flush (`OnApplicationPause/Quit`) | Mobile lifecycle — don't lose the tail |
| 9 | Idempotency (event id) | Retries must not double-count server-side |
| 10 | Metadata enrichment | timestamp, sessionId, userId, sdkVersion, platform, appVersion + device context (model, OS, network, timezone, locale, bundle) |
| 11 | Error isolation | Never throw into game code — swallow + log |
| 12 | Configurability | endpoint, batch size, intervals, max queue, on/off, privacy mode |
| 13 | Privacy / opt-out | `SetEnabled(false)` + purge queue (GDPR); `SetPrivacyMode(true)` (anonymous userId) |
| 14 | Testability via DI | `ITransport` / `IEventStore` / `IClock` — basis of tests |
| 15 | Cancellation / Dispose | Clean worker shutdown, `CancellationToken` |

**Acknowledged in README as deliberately out of scope** (shows we see further without
over-engineering): gzip compression, request auth/signing, server-side rate limiting,
PII scrubbing, and a WebGL `UnityWebRequest` transport.
*(Connectivity-awareness — held offline → flush online, via an HTTP probe rather than a
native-platform reachability API — and the dead-letter queue **are** implemented under the
"maximal showcase" scope; see Phase 5.)*

---

### 5. Package & project structure

```
ProjectRoot/                         ← the "Complete Unity Project" (open & run tests)
  Packages/
    com.example.tracking/            ← the package → npm pack produces the .tgz artifact
      package.json
      README.md  CHANGELOG.md  LICENSE.md
      Runtime/
        Example.Tracking.asmdef
        ITracker.cs  TrackingSystem.cs  Tracker.cs  (static facade)
        Model/      TrackingEvent.cs  TrackingConfig.cs
        Queue/      EventQueue.cs
        Dispatch/   EventDispatcher.cs  RetryPolicy.cs
        Transport/  ITransport.cs  SimulatedHttpTransport.cs  UnityWebRequestTransport.cs
        Persistence/ IEventStore.cs  FileEventStore.cs  InMemoryEventStore.cs
        Util/       IClock.cs  SystemClock.cs  TrackingLifecycle.cs
      Tests/
        Example.Tracking.Tests.asmdef    (EditMode + PlayMode)
      Samples~/BasicUsage/
  Assets/                            ← thin host: demo scene with buttons
  tools/live-receiver/track.php      ← optional live receiver example (Phase 6)
  README.md  DESIGN.md  AI_NOTES.md  PLAN.md   ← docs + AI workflow files
```

**Artifacts produced:** (1) this repo; (2) `npm pack` in the package dir →
`com.example.tracking-1.0.0.tgz` (Package Manager → *Add package from tarball*); (3) `README.md`;
(4) the AI `.md` files (`PLAN.md`, `DESIGN.md`, `AI_NOTES.md`).

---

### 6. Testing strategy

Unity Test Framework, **EditMode** for the deterministic core, **PlayMode** for lifecycle/real
transport. Everything deterministic via fakes:
- Validation (`null`/empty → `false`); metadata enrichment.
- Event reaches transport; batching (N events → 1 request); FIFO order.
- **Retries:** fake transport fails twice then succeeds (injected `IClock` — no real delays).
- Drop policy on overflow; give-up path → async returns `false`.
- **Persistence:** events survive a simulated restart (store saved → new dispatcher resends).
- Flush on shutdown; concurrent calls (thread-safety).
- `SendMapAsync` resolves the expected `bool` per the batch-delivery semantics.
- Real GoDaddy endpoint exercised in a **manual/PlayMode** test, kept out of unit/CI.

---

### 7. Phased roadmap

Strictly incremental. Each phase ends green and "touchable."

- [ ] **Phase 0 — Scaffolding**
  Clean Unity 2022.3 LTS project + UPM package skeleton (`package.json`, asmdefs Runtime+Tests,
  README/CHANGELOG/LICENSE stubs). Test Runner wired with **one trivial green test**.
  ✅ Project opens, Test Runner green.

- [ ] **Phase 1 — Public API + event model + validation (no network)**
  `ITracker`, `TrackingSystem` (+ the static `Tracker` facade), `TrackingEvent`, `TrackingConfig`. `SendMessage` validation → `bool`;
  `SendMapAsync` skeleton. In-memory enqueue + recording transport.
  ✅ Tests: validation, metadata enrichment, event creation.

- [ ] **Phase 2 — Queue + dispatcher + batching (simulated transport)**
  Thread-safe bounded `EventQueue` + drop policy. `EventDispatcher` worker: batch by size/time.
  `ITransport` + `SimulatedHttpTransport`, `IClock`. Wire `SendMapAsync` →
  `TaskCompletionSource` resolved on batch delivery.
  ✅ Tests: batching (N→1), FIFO, drop policy, async resolves true on delivery, virtual clock.

- [ ] **Phase 3 — Reliability: retries + backoff + cancellation**
  `RetryPolicy` (exp backoff + jitter, max attempts, virtual clock). Fail → retry → success/fail;
  async → `false` after exhaustion. `CancellationToken` / `Dispose`.
  ✅ Tests: retry counts, backoff timing, give-up, cancellation.

- [ ] **Phase 4 — Durability: persistence + lifecycle flush**
  `IEventStore` + `FileEventStore` (`persistentDataPath`) + `InMemoryEventStore`. Persist unsent;
  reload on init; flush+persist on `OnApplicationPause/Quit` (`TrackingLifecycle`).
  ✅ Tests: persist→restart→resend; flush on shutdown; corrupt-file resilience.

- [ ] **Phase 5 — Showcase extras**
  Connectivity-awareness (hold offline → flush online); dead-letter queue; diagnostics/metrics
  (enqueued/sent/dropped/retried) + logging hook; circuit breaker; privacy opt-out + purge.
  ✅ Tests per item.

- [ ] **Phase 6 — Real transport (GoDaddy PHP) + dual-mode**
  `UnityWebRequestTransport` (or `HttpClient`) posting JSON batch to a real URL. Tiny optional
  `track.php` receiver example (validate, log to file, return `200 {ok}`) — authored here, optionally deployed to GoDaddy.
  Config switch Simulated ↔ Real; default URL stays `fakeserver.com`.
  ✅ PlayMode/manual test against the live endpoint (outside CI).

- [ ] **Phase 7 — Sample scene + packaging + docs**
  `Samples~/BasicUsage` + demo scene (buttons + live counters). `README.md` (architecture +
  implicit-requirements rationale + AI note). `DESIGN.md`, `AI_NOTES.md`. `npm pack` → `.tgz`,
  verify importable. Final packaged artifact.

---

### 8. AI usage note

The architecture, the implicit-requirements analysis, and this phased plan were developed in a
working session with an AI assistant (Claude / Claude Code). The AI was used to (a) enumerate the
production-grade implicit requirements, (b) design the queue→dispatcher→transport architecture with
DI seams for testability, and (c) drive incremental implementation with tests per phase. `PLAN.md`,
`DESIGN.md`, and `AI_NOTES.md` are retained as the record of that workflow.
