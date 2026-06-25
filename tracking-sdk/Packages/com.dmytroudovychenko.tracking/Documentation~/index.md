# Dmytro Udovychenko Tracking SDK — Documentation

Production-grade, in-process event tracking for Unity. Two public methods over a non-blocking
delivery pipeline with retries, persistence, and full testability.

## Contents

| Document | Description |
|---|---|
| [API Reference](api-reference.md) | All public types, methods, properties, and enums |
| [Configuration](configuration.md) | Every tunable with defaults and usage notes |
| [Architecture](architecture.md) | Pipeline internals, components, DI seams, delivery semantics |
| [track.php](track.php) | Live test receiver (PHP stub) — see [below](#live-test-receiver) |

## Quick start

```csharp
using DmytroUdovychenko.Tracking;

Tracker.Init("user-123");
Tracker.SendMessage("level_completed");
```

`Tracker` is a static facade — call `Init` once, then `SendMessage` / `SendMapAsync` from anywhere.
Lifecycle (persist-on-pause, dispose-on-quit) is auto-wired.

For advanced DI, multiple trackers, or deterministic tests, construct `TrackingSystem` directly:

```csharp
var config = new TrackingConfig { UserId = "user-123" };
var system = new TrackingSystem(config, transport: myTransport);
system.SendMessage("hello");
```

## Namespace

All public types are in `DmytroUdovychenko.Tracking`.

## Minimum Unity version

Unity **2022.3** or newer.

## Package layout

```
com.dmytroudovychenko.tracking/
  Runtime/                   Production code
    Connectivity/            Reachability checking
    DeadLetter/              Give-up event preservation
    Diagnostics/             Logging + metrics
    Dispatch/                Background worker, retries, circuit breaker
    Model/                   Data types (TrackingEvent, TrackingConfig, ServerEnvironment)
    Persistence/             Durable event storage
    Queue/                   Bounded thread-safe buffer
    Transport/               Pluggable delivery (Simulated / Http)
    Util/                    Clock, delayer, runtime info, lifecycle
  Tests/Editor/              147 EditMode tests
  Samples~/BasicUsage/       Canvas demo (import via Package Manager)
  Documentation~/            This documentation + live test receiver
```

## Live test receiver

The package includes [`track.php`](track.php) — a minimal PHP endpoint that acts as a developer
**test stub** for verifying end-to-end HTTP delivery. It is **not** a production backend.

**What it does:**

1. Accepts `POST` with JSON body `{"events": [...]}`
2. Validates the payload structure
3. Appends every request (timestamp, IP, event count, HTTP status) to `track.log` next to the script
4. Returns `200 {"ok": true, "received": <count>}`

**Chaos mode:** append `?fail=<0-100>` to the URL — that percentage of requests will get a
transient `503` instead of `200`. This exercises the SDK's retry/backoff/circuit-breaker pipeline
against a real server. `ServerEnvironment.HttpTestServerChaos` uses `?fail=20` (~20% failures).

**Deployed instance:** `ServerEnvironment.HttpTestServer` and `HttpTestServerChaos` both point to a
copy deployed at `https://udovychenko.xyz/test/track.php`. To run your own, upload `track.php` to
any PHP-capable hosting and point the SDK at it:

```csharp
Tracker.Init("user-123", "https://yourdomain.com/test/track.php");
```
