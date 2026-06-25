# Configuration Reference

All tunables are properties on `TrackingConfig`. Construct with object-initializer syntax:

```csharp
var config = new TrackingConfig
{
    UserId           = "user-123",
    Endpoint         = "https://your.host/track",
    TransportMode    = TransportMode.Http,
    BatchSize        = 10,
    MaxRetryAttempts = 3,
    MinLogLevel      = TrackingLogLevel.Debug,
};
var tracker = new TrackingSystem(config);
```

When using the static `Tracker.Init(...)` helpers, the config is built internally from the
arguments. For full control, build `TrackingConfig` + `TrackingSystem` yourself.

---

## Identity

| Property | Type | Default | Description |
|---|---|---|---|
| `UserId` | `string` | `""` | User identifier stamped on every event |

---

## Transport

| Property | Type | Default | Description |
|---|---|---|---|
| `Endpoint` | `string` | `"https://fakeserver.com"` | URL the transport posts batches to |
| `TransportMode` | `TransportMode` | `Simulated` | Which built-in transport to construct when none is injected |
| `HttpTimeout` | `TimeSpan` | 10 seconds | Per-request timeout for the real HTTP transport |
| `SimulatedFailPercent` | `int` | `0` | Percent of simulated sends that fail transiently (chaos mode sets this to 20) |

---

## Batching

| Property | Type | Default | Description |
|---|---|---|---|
| `BatchSize` | `int` | `20` | Maximum events per batch |
| `FlushInterval` | `TimeSpan` | 5 seconds | Maximum time a partial batch waits before being flushed |

The dispatcher sends a batch when either `BatchSize` events accumulate or `FlushInterval` elapses,
whichever comes first. `FlushAsync()` triggers an immediate flush.

---

## Buffering

| Property | Type | Default | Description |
|---|---|---|---|
| `MaxQueueCapacity` | `int` | `10,000` | Upper bound on buffered events |
| `OverflowPolicy` | `OverflowPolicy` | `DropOldest` | What happens when the queue is full |

`DropOldest` evicts the oldest event to make room for new ones (no data loss for recent events).
`RejectNew` rejects the incoming event (preserves order of what's already buffered).

---

## Retries

| Property | Type | Default | Description |
|---|---|---|---|
| `MaxRetryAttempts` | `int` | `5` | Delivery attempts before giving up |
| `InitialRetryDelay` | `TimeSpan` | 500 ms | Base delay for the first retry |
| `MaxRetryDelay` | `TimeSpan` | 30 seconds | Ceiling for exponential backoff |

Retries use exponential backoff with jitter: delay doubles each attempt (with random jitter) up to
`MaxRetryDelay`. After `MaxRetryAttempts` failures, the event is moved to the dead-letter queue.

---

## Circuit breaker

| Property | Type | Default | Description |
|---|---|---|---|
| `CircuitBreakerThreshold` | `int` | `5` | Consecutive batch failures before the circuit opens |
| `CircuitBreakerCooldown` | `TimeSpan` | 30 seconds | How long the circuit stays open before a trial request |

When the circuit is open, batches are held (not attempted) until the cooldown expires, then a single
trial batch is sent. If it succeeds, the circuit closes and normal delivery resumes.

---

## Dead letter

| Property | Type | Default | Description |
|---|---|---|---|
| `DeadLetterCapacity` | `int` | `1,000` | Maximum events preserved in the dead-letter queue |

Events that exhaust retries are moved here. Inspect via `Tracker.DeadLetter.Events` and clear via
`Tracker.DeadLetter.Clear()`.

---

## Privacy

| Property | Type | Default | Description |
|---|---|---|---|
| `Enabled` | `bool` | `true` | Master switch — when `false`, all API calls are rejected |
| `PrivacyMode` | `bool` | `false` | Anonymous mode — events are stamped with `"anonymous"` instead of the real userId |

`SetEnabled(false)` also purges all buffered, persisted, and dead-lettered events.
`SetPrivacyMode(true)` only changes the userId on future events — existing buffered events retain
their original userId.

---

## Shutdown

| Property | Type | Default | Description |
|---|---|---|---|
| `ShutdownDrainTimeout` | `TimeSpan` | 2 seconds | How long `Dispose` waits for the worker's final drain |

---

## Logging

| Property | Type | Default | Description |
|---|---|---|---|
| `MinLogLevel` | `TrackingLogLevel` | `Warning` | Minimum severity that reaches the logger |

Set to `Debug` for verbose tracing: every pipeline step and event payload is logged. Set to `Error`
for near-silent operation (errors only).

| Level | What it shows |
|---|---|
| `Debug` | Per-step traces, event payloads, JSON bodies, queue/dispatch decisions |
| `Info` | High-level milestones (init, batch sent, circuit state changes) |
| `Warning` | Unusual but recoverable situations (double init, call before init, retry) |
| `Error` | Failures (transport errors, persistence errors, probe failures) |

---

## Constants

| Constant | Value | Description |
|---|---|---|
| `DEFAULT_ENDPOINT` | `"https://fakeserver.com"` | Placeholder for simulated transport |
| `HTTP_TEST_ENDPOINT` | `"https://udovychenko.xyz/test/track.php"` | Developer live test receiver |
| `HTTP_TEST_CHAOS_ENDPOINT` | `"https://udovychenko.xyz/test/track.php?fail=20"` | Chaos variant |
| `CHAOS_FAIL_PERCENT` | `20` | Transient failure rate in chaos mode |
| `CHAOS_QUERY` | `"?fail=20"` | Query suffix for server-side chaos |
| `DEFAULT_CONNECTIVITY_PROBE_TIMEOUT` | 5 seconds | HEAD ping timeout for reachability checks |

---

## Static helpers

```csharp
// Map a ServerEnvironment to its endpoint URL
string endpoint = TrackingConfig.EndpointFor(ServerEnvironment.HttpTestServer);

// Get the simulated failure rate for a server
int failPercent = TrackingConfig.SimulatedFailPercentFor(ServerEnvironment.FakeServerChaos); // 20
```
