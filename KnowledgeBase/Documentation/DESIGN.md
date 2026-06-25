# Design notes

Deeper rationale behind the non-obvious decisions. The high-level picture and the
implicit-requirements scorecard live in [`README.md`](../../README.md); the build log is in
[`TASK_PROGRESS.md`](TASK_PROGRESS.md).

## 1. Why a queue + background worker (not "just send")

The API is a hot path inside a game. Network I/O on that path would stall frames. So the
public methods do the minimum on the caller's thread — validate, enrich, enqueue — and a single
background `EventDispatcher` owns all I/O. This is the source of requirements 1–4 (non-blocking,
thread-safe, off-main-thread, batching) falling out naturally.

## 2. Making `SendMapAsync`'s result mean something

A `Task<bool>` that just says "validated OK" would be theatre. Instead each `SendMapAsync` event
carries a `TaskCompletionSource<bool>` that the dispatcher resolves **when the batch containing it is
actually delivered** (or `false` after retries are exhausted / on eviction / on purge). This
correlates an async result to real batched delivery — the most interesting part of the design.
`RunContinuationsAsynchronously` keeps awaiter continuations off the dispatcher thread.

## 3. Determinism: the testing strategy is the design

Every external dependency is an interface: `ITransport`, `IEventStore`, `IClock`, `IDelayer`,
`IConnectivity`, `IRuntimeInfo`, `ITrackingLogger`, `IDeadLetterSink`. The two ideas that make the
concurrent core deterministic:

- **The worker is opt-in.** Tests construct the `TrackingSystem` with `startWorker: false` and drive delivery
  explicitly via `FlushAsync()` / the dispatcher's `PumpOnceAsync` / `DrainAsync`. No real thread, no
  race — the test *is* the scheduler.
- **Time is injected.** Batching-by-interval uses `IClock`; retry backoff uses `IDelayer`; the circuit
  breaker uses `IClock`. Tests advance a `FakeClock` and record a `RecordingDelayer` instead of
  sleeping. A "5-second flush interval" test runs in microseconds.

`[InternalsVisibleTo]` exposes the internal `EventQueue` / `EventDispatcher` so the concurrency core
can be white-box tested directly, not only through the public surface.

## 4. Drop policy: two correct answers, both shipped

When the buffer is full while offline, something has to give. One natural contract has `SendMessage`
return `false` when "queue full" — that's `RejectNew`. But telemetry usually prefers the newest data
and a never-blocking producer — that's `DropOldest`. Rather than pick one, `OverflowPolicy` offers
both, default `DropOldest` (producer never rejected; an old event is evicted and counted; its awaiter,
if any, resolves `false`). Both paths are tested.

## 5. Persistence model: at-least-once, idempotent

`FileEventStore` writes the undelivered backlog as JSON (temp-file + atomic `File.Replace`, so a crash
never leaves the destination absent) and reloads it on
construction. Persistence is a **snapshot of the live queue** taken on lifecycle events (pause/quit),
not a write-ahead log on every enqueue — lighter I/O, and a hard crash between snapshots loses only
the most recent tail. The cost is at-least-once delivery (a snapshot may include events that then
deliver, and re-send after a crash). That's why every event has a stable `Id`: the **idempotency key**
the server uses to de-duplicate. `JsonUtility` can't serialize `Dictionary<string, object>`, so
`EventSerializer` flattens each payload entry to a type-tagged `{k,t,v}` triple and restores the
original primitive type on load — no third-party JSON dependency.

## 6. Reliability layering (retry vs circuit breaker vs connectivity)

These operate at different scopes and compose:

- **Retry** (`RetryPolicy`) is *per batch*: capped exponential backoff with **equal jitter**
  (delay ∈ [base/2, base]) so retries grow and spread, up to `MaxRetryAttempts`, then give up.
- **Circuit breaker** is *across batches*: after N consecutive batch give-ups it opens and the
  dispatcher stops attempting further batches until a cooldown, then a half-open trial probes recovery.
  This prevents every queued batch from independently retrying against a known-down server. A **failed
  half-open probe batch is dead-lettered after that single attempt** (not re-queued): the probe counts as a
  give-up, so `MaxRetryAttempts` is intentionally not spent on a server that is only being probed for recovery.
- **Connectivity-awareness** short-circuits before any of that: if the device is offline, don't even
  attempt — hold events and flush when back online (default is `AlwaysOnline`, so it's strictly opt-in
  and never makes `new TrackingSystem()` or tests depend on real reachability).

Give-up events are not dropped: they go to an `IDeadLetterSink` for inspection/replay, and the
`TrackingMetrics` counters (enqueued/sent/dropped/retried/givenUp/deadLettered) make the whole
pipeline observable.

## 7. Privacy

`SetEnabled(false)` stops accepting events and immediately `Purge()`s — clearing the live queue, the
dead-letter queue, and the persisted store, and resolving pending awaiters `false`. That's the GDPR
"withdraw consent + delete my data" shape in two calls.

## 8. Initialization: one command, a required `userId`, three destinations

The DI constructor (`new TrackingSystem(config, …)`) stays the testing seam, but the *intended* entry point is
`Tracker.Init`, which makes the two things a caller always decides explicit: **who** the events belong to
and **where** they go. `userId` is a required first argument — blank/null throws `ArgumentException` (a
setup-time error, distinct from the hot-path error-isolation rule). The destination is chosen one of three
ways, distinguished by overload: nothing (default endpoint, simulated), a `ServerEnvironment` enum value
(named server → URL via `TrackingConfig.EndpointFor`), or a custom endpoint string. `FakeServer`/`FakeServerChaos`
map to the offline fake (simulated, no network — the `*Chaos` twin injects ~20% transient failures to exercise
the reliability paths); `HttpTestServer`/`HttpTestServerChaos` and custom endpoints use the
real HTTP transport — so "pick a real server" actually talks to it, while the default path stays network-free.
`userId` is then stamped on every event (alongside `sessionId`) and round-trips through the serializer, so
it reaches both the durable store and the wire.

Picking a **real** server implies a network, so `Init` enforces it: when the device is offline,
`TrackingSystem.Init(...)` against any live server (or custom endpoint) returns `null` and the `Tracker`
facade stays uninitialized rather than spinning up a worker that can't deliver — the caller retries once
connectivity returns. This is deliberately *not* applied to the simulated `FakeServer*` path: there, offline
is the normal case the queue + durable store + hold-while-offline machinery exists to handle, so it always
proceeds. The gate keys on the resolved `TransportMode` (`IsBlockedOffline`), not on the enum, so the custom
`Init(userId, endpoint)` overload is covered too. Reachability is `Application.internetReachability`, which
reports interface state, not a true-internet probe — see `KnowledgeBase/Documentation/WARNINGS.md`.

For callers who need a **real** check (not just interface state), `Tracker.InitAsync(...)` confirms the
**target tracking server** is reachable before bringing the pipeline up — because internet can be up while
the destination is down, in which case events would never arrive. The check (`IConnectivityProbe` /
`HttpConnectivityProbe`) is two-stage: (1) interface fast-fail via `internetReachability` ("no internet"),
then (2) a HEAD ping to the resolved endpoint. Reachability means **the server returned any HTTP response** —
notably a 405, which is exactly what `track.php` replies to a non-POST; that still proves DNS + TCP + host
are alive. Only a transport error/timeout is "server not responding". (A transient 5xx counts as reachable
on purpose: the delivery retry / circuit-breaker is the right place to absorb those, not the init gate.) It's
built on `HttpClient` rather than `UnityWebRequest` (consistent with the transport, thread-agnostic), and the
probe is `Task`-based rather than a coroutine — with one deliberate twist: the post-probe init does **not**
`ConfigureAwait(false)`, so it resumes on the Unity main thread to build the `UnityConnectivity` snapshot and
the lifecycle `GameObject` (both main-thread-only). A throwing/misbehaving probe resolves `false` (error
isolation) rather than escaping into caller code.

`Init` is also the **production entry point**, so it wires the real Unity-backed seams — durable
persistence (`FileEventStore`) and connectivity-awareness (`UnityConnectivity`) — on by default; the DI
constructor deliberately defaults those to the bare Null/always-online seams so tests stay deterministic.
A one-line `Tracker.Init("user-123")` is therefore a fully production-grade tracker, not a stripped one.

## 8a. The static `Tracker` facade over `TrackingSystem`

Game code wants `Tracker.SendMessage(...)`, not an instance to hold and thread through every caller — so
the public entry point is a **static facade**. But a global mustn't cost the thing this SDK is built on:
the DI seams. So `Tracker` is a *thin wrapper*, never a replacement. The concrete pipeline is
`TrackingSystem` (the old `Tracker`, renamed): still public, still constructed with every seam injected,
still the testing surface and the way to run multiple trackers. `Tracker` just holds one configured
`TrackingSystem` behind `Init(...)` and forwards.

The global pulls in the usual static-state hazards; each is closed deliberately:
- **Call before `Init`** no-ops safely (returns `false` / a completed `Task`, never throws, never hangs) and warns once.
- **Double `Init`** is ignored (the first tracker is kept); reconfiguring is an explicit `Dispose()` then `Init()`.
- **Lifecycle** is auto-wired by the production `Init` overloads (persist on background/quit, dispose on quit), so game code drops the manual `TrackingLifecycle.Attach` + `OnDestroy`.
- **Domain-reload-off** play sessions reset the statics via `[RuntimeInitializeOnLoadMethod]`, disposing any leftover so the previous run's worker/`HttpClient` don't leak — the same `Dispose()` keeps EditMode tests isolated.

Full reference: [`KnowledgeBase/Documentation/STATIC_FACADE.md`](STATIC_FACADE.md).

## 9. Why `HttpClient` for the real transport

The dispatcher runs on a background thread; `UnityWebRequest` is main-thread-only, so it can't be
driven from there without marshalling. `HttpClient` is thread-safe and thread-agnostic, so the real
`HttpTransport` works directly from the worker — and it's unit-testable by injecting a fake
`HttpMessageHandler` (no live network). The one platform where this doesn't hold is **WebGL** (no
socket stack): a WebGL build would supply a `UnityWebRequest`-based transport that marshals sends to
the main thread. Called out here as known, deliberately-deferred work.

## 10. Deliberately out of scope

gzip compression, request auth/signing, server-side rate limiting, PII scrubbing, a WebGL transport,
and a real backend datastore. Each is a known production concern; none is core to the pipeline this
SDK sets out to demonstrate.
