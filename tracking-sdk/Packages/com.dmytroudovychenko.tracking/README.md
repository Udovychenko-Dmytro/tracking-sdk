# Dmytro Udovychenko Tracking SDK

A production-grade, in-process **event tracking SDK** for Unity.

Two public methods (`SendMessage` / `SendMapAsync`) sit over a non-blocking delivery pipeline:
thread-safe bounded queue, background batching dispatcher, pluggable transport — with retries,
exponential backoff, circuit breaker, connectivity-awareness, durable persistence, lifecycle flush,
dead-letter queue, metrics, logging hook, and privacy opt-out. All of it deterministically testable
through dependency-injection seams.

## Quick start

```csharp
using DmytroUdovychenko.Tracking;

// 1. Initialize once at startup
Tracker.Init("user-123");

// 2. Send events
bool accepted = Tracker.SendMessage("level_completed");

bool delivered = await Tracker.SendMapAsync(new Dictionary<string, object>
{
    ["event"] = "purchase",
    ["sku"]   = "coins_500",
    ["price"] = 4.99,
});

// 3. Dispose on shutdown (auto-wired to Application.quitting by default)
Tracker.Dispose();
```

## Install

**Package Manager > Add package from git URL:**

```
https://github.com/Udovychenko-Dmytro/tracking-sdk.git?path=tracking-sdk/Packages/com.dmytroudovychenko.tracking
```

Or develop as an **embedded package** at `Packages/com.dmytroudovychenko.tracking` inside your Unity
project. A `.tgz` tarball for **Add package from tarball** is produced on each release.

**Requirements:** Unity 2022.3+

## Initialization

### Sync (immediate)

```csharp
// Default — simulated transport, no network needed
Tracker.Init("user-123");

// Named server target
Tracker.Init("user-123", ServerEnvironment.HttpTestServer);

// Custom endpoint (real HTTP)
Tracker.Init("user-123", "https://your.host/track");
```

### Async (server reachability probe first)

```csharp
bool ok = await Tracker.InitAsync("user-123", ServerEnvironment.HttpTestServer);
if (!ok) Debug.Log("Server unreachable — tracker not initialized");

// Or with a custom endpoint
bool ok = await Tracker.InitAsync("user-123", "https://your.host/track");
```

### DI / multiple trackers

```csharp
var system = new TrackingSystem(config, transport, clock, runtime, ...);
Tracker.Init(system, attachLifecycle: false);   // adopt pre-built instance

// Or use TrackingSystem directly without the static facade
var tracker = TrackingSystem.Init("user-123", ServerEnvironment.FakeServer);
tracker.SendMessage("hello");
```

## Server environments

| Value | Transport | Network | Chaos |
|---|---|---|---|
| `FakeServer` | Simulated | No | No |
| `FakeServerChaos` | Simulated | No | ~20% transient failures |
| `HttpTestServer` | Real HTTP | Yes | No |
| `HttpTestServerChaos` | Real HTTP | Yes | ~20% transient 503s |

`FakeServer` / `FakeServerChaos` stay fully offline — no real network is hit.
`HttpTestServer` / `HttpTestServerChaos` target a bundled developer **live test receiver (stub)**
(validate, log, `200`; chaos = `?fail=20`), not a production backend.

Pass your own endpoint via `Tracker.Init(userId, "https://...")` for real HTTP delivery.

## Public API

| Method | Description |
|---|---|
| `Tracker.Init(userId)` | Initialize with default (simulated) transport |
| `Tracker.Init(userId, server)` | Initialize with a named `ServerEnvironment` |
| `Tracker.Init(userId, endpoint)` | Initialize with a custom URL (real HTTP) |
| `Tracker.InitAsync(userId, ...)` | Async init with server reachability probe |
| `Tracker.SendMessage(message)` | Record a message event (non-blocking, returns `bool`) |
| `Tracker.SendMapAsync(map)` | Record a structured event (returns `Task<bool>`) |
| `Tracker.FlushAsync()` | Force delivery of all buffered events |
| `Tracker.Persist()` | Snapshot buffered events to durable storage |
| `Tracker.SetEnabled(bool)` | Enable/disable tracking (disabling purges data) |
| `Tracker.SetPrivacyMode(bool)` | Anonymous mode (userId becomes `"anonymous"`) |
| `Tracker.Purge()` | Discard all buffered, dead-lettered, and persisted events |
| `Tracker.Dispose()` | Dispose tracker and clear global state |
| `Tracker.IsInitialized` | Whether a tracker is live |
| `Tracker.IsEnabled` | Whether tracking is accepting events |
| `Tracker.IsPrivacyMode` | Whether anonymous mode is on |
| `Tracker.Metrics` | Live diagnostic counters |
| `Tracker.DeadLetter` | Events that exhausted retries |
| `Tracker.SessionId` | Stable session identifier |
| `Tracker.UserId` | Current user identifier |

The static `Tracker` is a thin facade over the DI'd `TrackingSystem`. Construct
`new TrackingSystem(...)` directly for advanced DI, multiple trackers, or deterministic tests.

See [Documentation~/api-reference.md](Documentation~/api-reference.md) for the full API reference.

## Configuration

All tunables live on `TrackingConfig` with sensible defaults:

```csharp
var config = new TrackingConfig
{
    UserId           = "user-123",
    BatchSize        = 20,           // events per batch
    FlushInterval    = TimeSpan.FromSeconds(5),
    MaxQueueCapacity = 10_000,
    OverflowPolicy   = OverflowPolicy.DropOldest,
    MaxRetryAttempts = 5,
    MinLogLevel      = TrackingLogLevel.Debug,  // verbose tracing
};
```

See [Documentation~/configuration.md](Documentation~/configuration.md) for the complete reference.

## Architecture

```
   Public API (ITracker)
   SendMessage(string)  /  SendMapAsync(Dictionary)
            |  validate -> enrich -> TrackingEvent
            v
   EventQueue (bounded, thread-safe)
            |  batch triggers: size / time / explicit flush
            v
   EventDispatcher (background worker)
   |  retry + backoff + jitter
   |  circuit breaker
   |  connectivity gate
   |  dead-letter on give-up
   |  <-> IEventStore (durable persistence)
            v
   ITransport (SimulatedHttp / Http / custom)
```

See [Documentation~/architecture.md](Documentation~/architecture.md) for details.

## Documentation

The package includes full documentation in `Documentation~/` (shipped with the UPM tarball):

| Document | Description |
|---|---|
| [index.md](Documentation~/index.md) | Overview, quick start, package layout |
| [api-reference.md](Documentation~/api-reference.md) | All public types, methods, properties, enums |
| [configuration.md](Documentation~/configuration.md) | Every tunable with defaults and usage notes |
| [architecture.md](Documentation~/architecture.md) | Pipeline internals, components, DI seams, delivery semantics |
| [track.php](Documentation~/track.php) | Live test receiver (PHP stub) — see below |

`track.php` is a minimal PHP endpoint for verifying HTTP delivery end-to-end. It accepts
`POST {"events": [...]}`, validates the payload, logs every request to `track.log`, and returns
`200 {"ok": true}`. Append `?fail=20` for chaos mode (~20% transient `503`s — exercises
retry/backoff). `ServerEnvironment.HttpTestServer` / `HttpTestServerChaos` point to a deployed
copy at `udovychenko.xyz/test/track.php`. Deploy your own on any PHP hosting and pass the URL
to `Tracker.Init`. **Not a production backend** — no auth, no storage, no dedup.

## Samples

The package ships a **Basic Usage** sample — a Canvas demo wiring init targets, valid/error sends,
and live metric counters. Import from **Window > Package Manager > select this package > Samples >
Import**. Unity copies it into `Assets/Samples/Dmytro Udovychenko Tracking SDK/<version>/Basic Usage/`.

## Tests

147 EditMode tests (145 deterministic + 2 live tests against the deployed receiver).
Run via **Window > General > Test Runner > EditMode > Run All**, or headless:

```bash
Unity -runTests -batchmode -nographics -projectPath <project> \
  -testPlatform EditMode -testResults results.xml
```

## License

MIT — see [LICENSE.md](LICENSE.md).
