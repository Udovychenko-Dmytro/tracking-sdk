# Changelog

All notable changes to this package are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [1.0.0] - 2026-06-24

Initial release of the **Dmytro Udovychenko Tracking SDK** (`com.dmytroudovychenko.tracking`) — a
production-grade, in-process event-tracking SDK for Unity. The public surface is tiny; the value is
the non-blocking pipeline behind it and the dependency-injection seams that make it deterministically
testable.

### Added
- **Public API.** `ITracker` with two methods — `SendMessage(string)` (non-blocking; returns whether
  the event was accepted into the pipeline) and `SendMapAsync(Dictionary<string, object>)` (returns a
  `Task<bool>` that resolves when the **batch** carrying the event is actually delivered, via a
  per-event `TaskCompletionSource`; `false` after retries are exhausted / on eviction / on purge).
- **Static `Tracker` facade + DI'd `TrackingSystem`.** `Tracker.Init(userId)` holds one configured
  tracker so game code keeps no instance; calls before `Init` no-op safely (never throw, never hang)
  and warn once. Construct `new TrackingSystem(…)` directly for advanced DI, multiple trackers, or
  deterministic tests. Teardown via `Tracker.Dispose()` (the instance `TrackingSystem` is the real
  `IDisposable`).
- **Initialization.** `Init(userId)` (default endpoint, simulated), `Init(userId, ServerEnvironment)`
  (named target → URL), and `Init(userId, endpoint)` (custom endpoint, real HTTP) — each with an
  optional `minLogLevel` argument. `Init` wires durable persistence (`FileEventStore`) and
  connectivity-awareness (`UnityConnectivity`) by default, so a one-line `Tracker.Init("user")` is
  production-grade. `InitAsync(…)` adds an async server-reachability probe (`IConnectivityProbe` /
  `HttpConnectivityProbe`, built on `HttpClient`): it checks the device, then HEAD-pings the target
  server and only initializes if the server answers; offline targeting a live server is gated (factory
  returns `null`, one warning logged). `FakeServer` skips the check.
- **`ServerEnvironment` targets — a transport × chaos matrix.** `FakeServer` (default) and
  `FakeServerChaos` are simulated and offline (no network); the chaos variant injects ~20% transient
  send failures via `TrackingConfig.SimulatedFailPercent`, so the retry / circuit-breaker / dead-letter
  pipeline can be exercised without a server. `HttpTestServer` and `HttpTestServerChaos` POST over real
  HTTP to the receiver stub; the chaos variant is a `?fail=20` query preset (~20% transient 503s).
  `CHAOS_FAIL_PERCENT` and `TrackingConfig.SimulatedFailPercentFor(ServerEnvironment)` are the single
  source of truth for the chaos rate, shared by the simulated transport and the HTTP query.
- **Non-blocking pipeline.** Thread-safe bounded `EventQueue` with a configurable `OverflowPolicy`
  (`DropOldest` default / `RejectNew`) and dropped-count tracking; a background `EventDispatcher` that
  batches by size **or** elapsed time and keeps all I/O off the caller's thread.
- **Transports.** `SimulatedHttpTransport` (default; no network, simulates the POST, with optional
  offline chaos via `SimulatedFailPercent`) and `HttpTransport` (real `HttpClient` POST of the JSON
  batch — `HttpClient`, not `UnityWebRequest`, so delivery runs on a background thread). Selected by
  `TrackingConfig.TransportMode`.
- **Reliability.** `RetryPolicy` (capped exponential backoff with equal jitter; gives up after
  `MaxRetryAttempts`), a circuit breaker (open after consecutive failures, single half-open probe after
  cooldown), connectivity-awareness (hold while offline → flush when back online), and a dead-letter
  queue (`IDeadLetterSink` / `InMemoryDeadLetterQueue`; give-up events preserved, not silently dropped).
- **Durable persistence + lifecycle flush.** `FileEventStore` (atomic temp-file writes under
  `persistentDataPath`; missing/corrupt files treated as "no backlog", never throw), reload-on-start,
  and `TrackingLifecycle` which persists on `OnApplicationPause`/`Quit`. At-least-once delivery; a stable
  event `Id` reused on retries lets the server de-duplicate (idempotency). `EventSerializer` encodes
  `object` payloads as type-tagged JSON via Unity's dependency-free `JsonUtility`.
- **Privacy controls.** `SetEnabled(false)` (opt-out) + `Purge()` (GDPR delete — clears queue,
  dead-letter, and persisted store; fails pending awaiters), and `SetPrivacyMode(bool)` (anonymous mode
  — every event built afterwards is stamped with userId `"anonymous"`; forward-only, not a retroactive
  scrub).
- **Metadata enrichment.** Every event carries timestamp, sessionId, userId, sdkVersion, platform,
  appVersion, plus coarse, non-identifying device context (`deviceModel`, `osVersion`, `networkType`,
  `timezone`, `locale`, `bundleId`). No stable device id, advertising id, IP, location, or carrier is
  ever collected (locked in by a regression test). `UnityRuntimeInfo` snapshots context on the main
  thread so off-thread enrichment never touches main-thread-only Unity APIs.
- **Diagnostics + logging hook.** `TrackingMetrics` / `TrackingMetricsSnapshot`
  (enqueued/sent/dropped/retried/given-up/dead-lettered) and a pluggable `ITrackingLogger`
  (`UnityTrackingLogger` / `NullTrackingLogger`); the SDK never calls `UnityEngine.Debug` directly.
  `TrackingConfig.MinLogLevel` (default `Warning`) governs step-by-step trace logging uniformly via a
  `LevelFilteringTrackingLogger`: `Warning` (quiet) → `Info` (lifecycle: initialized / enqueued /
  delivered) → `Debug` (adds event contents and the exact serialized JSON sent on the wire).
- **Dependency-injection seams.** `ITransport`, `IEventStore`, `IClock`, `IDelayer`, `IConnectivity`,
  `IConnectivityProbe`, `IRuntimeInfo`, `ITrackingLogger`, `IDeadLetterSink` — injecting fakes for
  network, disk, and time makes batching, retries, backoff, persistence, and the circuit breaker fully
  deterministic (no `Thread.Sleep`, no flaky timing).
- **Cancellation / Dispose.** A `CancellationToken` flows through the worker; shutdown drains within
  `TrackingConfig.ShutdownDrainTimeout`, then fails any unresolved awaiters as a backstop and releases
  its sync primitives only once the worker has stopped — a `SendMapAsync` `Task` can never hang on
  shutdown.
- **Runnable demo + sample.** A Canvas/uGUI `TrackingSdkDemo` (host copy under `Assets/TrackingDemo`,
  auto-spawns on Play) with four initialization targets (`Fake` clean/chaos, `Test stub` clean/chaos),
  valid/error sends, runtime controls, and live status + metrics panels — shipped as the
  `Samples~/BasicUsage` UPM sample (the `Samples~` folder is hidden in the Project window; import via
  *Package Manager → this package → Samples → Import*).
- **Optional live test receiver.** `Documentation~/track.php` — a developer diagnostic **test
  stub** (validate → log → `200`, with an optional `?fail=` chaos knob) for exercising real HTTP
  traffic, payload shape, status handling, and retries. It is **not** a backend service required by the
  SDK; the `HttpTestServer` / `HttpTestServerChaos` targets both resolve to this one stub.
- **Test suite.** 147 EditMode tests — 145 deterministic (DI fakes + a virtual clock; no real network
  or wall-clock waits) + 2 live tests (`[Category("Live")]`) that POST to the deployed receiver.

### Notes
- **Enum numbering convention.** `OverflowPolicy`, `TransportMode`, `CircuitState`, and
  `TrackingLogLevel` start at **1**, with **0 reserved** as the unset/`None` sentinel, so a
  default-initialized value is detectably "not set". `ServerEnvironment` is the deliberate exception:
  `FakeServer = 0` is the intended safe default (offline, simulated). Enum values are never serialized
  (by-name only). Documented in `CODING_STANDARDS.md`.

[Unreleased]: https://github.com/Udovychenko-Dmytro/tracking-sdk/compare/v1.0.0...HEAD
[1.0.0]: https://github.com/Udovychenko-Dmytro/tracking-sdk/releases/tag/v1.0.0
