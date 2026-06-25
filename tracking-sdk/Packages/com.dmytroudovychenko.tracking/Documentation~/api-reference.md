# API Reference

All public types are in the `DmytroUdovychenko.Tracking` namespace.

---

## Tracker (static facade)

The primary entry point. A static class that wraps a single global `TrackingSystem` instance.
Thread-safe. All methods are no-ops (returning safe defaults) before `Init` is called.

### Initialization

```csharp
// Sync — immediate, no reachability check
static void Init(string userId,
    TrackingLogLevel minLogLevel = TrackingLogLevel.Warning)

static void Init(string userId, ServerEnvironment server,
    TrackingLogLevel minLogLevel = TrackingLogLevel.Warning)

static void Init(string userId, string endpoint,
    TrackingLogLevel minLogLevel = TrackingLogLevel.Warning)

// Async — probes server reachability first; returns false if unreachable
static Task<bool> InitAsync(string userId, ServerEnvironment server,
    CancellationToken cancellationToken = default,
    TrackingLogLevel minLogLevel = TrackingLogLevel.Warning)

static Task<bool> InitAsync(string userId, string endpoint,
    CancellationToken cancellationToken = default,
    TrackingLogLevel minLogLevel = TrackingLogLevel.Warning)

// Adopt a pre-built instance (advanced DI / tests)
static void Init(TrackingSystem tracker, bool attachLifecycle = true)
```

**Sync overloads** initialize immediately. The `ServerEnvironment` overload selects a named target;
the `string endpoint` overload uses real HTTP to a custom URL. The parameterless-endpoint variant
defaults to `FakeServer` (simulated, no network).

**Async overloads** first check device connectivity, then send a HEAD ping to the target. If
unreachable, nothing is initialized and `false` is returned. `FakeServer` variants skip the probe.

**Double Init** is safe — the second call is ignored (with a warning). Call `Dispose()` first to
re-initialize with a different user or target.

### Event recording

```csharp
static bool SendMessage(string message)
```

Records a single message event. Non-blocking: validates, enriches with metadata, enqueues, and
returns immediately. Returns `true` if accepted, `false` on invalid input, disabled tracking, or
before `Init`.

```csharp
static Task<bool> SendMapAsync(Dictionary<string, object> map)
```

Records a structured (key/value) event. The dictionary is snapshot-copied — later mutation doesn't
affect the recorded event. The returned `Task<bool>` resolves `true` when the batch containing this
event is delivered to the server, or `false` after retries are exhausted, on invalid input, or when
disabled. Never hangs — all code paths complete the task.

### Pipeline control

```csharp
static Task FlushAsync()            // Force-deliver everything buffered
static void Persist()               // Snapshot to durable storage
static void SetEnabled(bool)        // Master switch (disabling purges data)
static void SetPrivacyMode(bool)    // Anonymous mode (userId -> "anonymous")
static void Purge()                 // Discard all events everywhere
static void Dispose()               // Dispose tracker, clear global state
```

### Reachability

```csharp
static Task<bool> IsServerReachableAsync(string endpoint,
    CancellationToken cancellationToken = default)
```

Checks whether an endpoint is reachable (network interface up + server answers a HEAD ping). Used
internally by `InitAsync`; exposed for manual gating.

### Properties

| Property | Type | Description |
|---|---|---|
| `IsInitialized` | `bool` | Whether a tracker is live |
| `Current` | `TrackingSystem` | The underlying instance, or `null` |
| `IsEnabled` | `bool` | Whether events are being accepted |
| `IsPrivacyMode` | `bool` | Whether anonymous mode is on |
| `Metrics` | `TrackingMetricsSnapshot` | Live diagnostic counters |
| `DeadLetter` | `IDeadLetterSink` | Events that exhausted retries |
| `SessionId` | `string` | Stable session identifier |
| `UserId` | `string` | Current user identifier |

---

## ITracker (interface)

The minimal public contract. Implement this for custom tracker wrappers.

```csharp
public interface ITracker
{
    bool SendMessage(string message);
    Task<bool> SendMapAsync(Dictionary<string, object> map);
}
```

---

## TrackingSystem (implementation)

The concrete `ITracker` implementation. Also implements `IDisposable`. Public for advanced DI,
multiple tracker instances, and deterministic testing.

### Static factories

```csharp
static TrackingSystem Init(string userId,
    TrackingLogLevel minLogLevel = TrackingLogLevel.Warning)

static TrackingSystem Init(string userId, ServerEnvironment server,
    TrackingLogLevel minLogLevel = TrackingLogLevel.Warning)

static TrackingSystem Init(string userId, string endpoint,
    TrackingLogLevel minLogLevel = TrackingLogLevel.Warning)
```

### Constructor (full DI)

```csharp
public TrackingSystem(
    TrackingConfig       config,
    ITransport           transport    = null,  // default: per config.TransportMode
    IClock               clock        = null,  // default: SystemClock
    IRuntimeInfo         runtime      = null,  // default: UnityRuntimeInfo
    bool                 startWorker  = true,
    IDelayer             delayer      = null,  // default: real Task.Delay
    IEventStore          store        = null,  // default: FileEventStore
    ITrackingLogger      logger       = null,  // default: UnityTrackingLogger
    IConnectivity        connectivity = null,  // default: UnityConnectivity
    IDeadLetterSink      deadLetter   = null   // default: bounded InMemoryDeadLetterQueue
)
```

Every parameter (except `config`) is optional with production-default fallback. Tests inject fakes
for deterministic control over time, transport, storage, and connectivity.

### Methods

Same as `Tracker` but instance-level: `SendMessage`, `SendMapAsync`, `FlushAsync`, `Persist`,
`SetEnabled`, `SetPrivacyMode`, `Purge`, `Dispose`.

### Properties

`SessionId`, `UserId`, `Metrics`, `DeadLetter`, `IsEnabled`, `IsPrivacyMode`.

---

## ServerEnvironment (enum)

Named target servers for `Init`. Two groups (100-block scheme): simulated/offline (0-block) and
real-HTTP (100-block), each with clean and chaos variants.

```csharp
public enum ServerEnvironment
{
    FakeServer          = 0,    // simulated, offline, clean
    FakeServerChaos     = 1,    // simulated, offline, ~20% failures
    HttpTestServer      = 101,  // real HTTP, clean
    HttpTestServerChaos = 102   // real HTTP, ~20% 503s
}
```

| Value | Transport | Network | Chaos | Endpoint |
|---|---|---|---|---|
| `FakeServer` | `SimulatedHttpTransport` | No | No | `https://fakeserver.com` |
| `FakeServerChaos` | `SimulatedHttpTransport` | No | ~20% | `https://fakeserver.com` |
| `HttpTestServer` | `HttpTransport` | Yes | No | `https://udovychenko.xyz/test/track.php` |
| `HttpTestServerChaos` | `HttpTransport` | Yes | ~20% | `https://udovychenko.xyz/test/track.php?fail=20` |

The `HttpTestServer` variants target a bundled developer **live test receiver (stub)**, not a
production backend.

---

## TrackingConfig

See [configuration.md](configuration.md) for the complete reference.

---

## OverflowPolicy (enum)

What happens when an event arrives and the queue is full.

```csharp
public enum OverflowPolicy
{
    // 0 reserved as the unset/None sentinel
    DropOldest = 1,   // Evict the oldest event to make room (default)
    RejectNew  = 2    // Reject the incoming event
}
```

---

## TransportMode (enum)

Which built-in transport to use when none is injected.

```csharp
public enum TransportMode
{
    // 0 reserved as the unset/None sentinel
    Simulated = 1,   // In-process, no network
    Http      = 2    // Real HTTP POST
}
```

---

## TrackingLogLevel (enum)

Severity levels for the logging hook.

```csharp
public enum TrackingLogLevel
{
    // 0 reserved as the unset/None sentinel
    Debug   = 1,   // Most verbose — per-step traces, event payloads
    Info    = 2,
    Warning = 3,   // Default MinLogLevel
    Error   = 4    // Least verbose — errors only
}
```

---

## ITrackingLogger (interface)

Logging hook for SDK diagnostics. Implement to route SDK logs to your own system.

```csharp
public interface ITrackingLogger
{
    void Log(TrackingLogLevel level, string message, Exception exception = null);
}
```

The default implementation (`UnityTrackingLogger`) routes to `Debug.Log` / `Debug.LogWarning` /
`Debug.LogError`.

---

## TrackingMetricsSnapshot (struct)

Live diagnostic counters returned by `Tracker.Metrics`.

Key fields (all `long`): `Enqueued`, `Sent`, `Dropped`, `Retried`, `GivenUp`, `DeadLettered`.

---

## IDeadLetterSink (interface)

Inspect events that exhausted their retries.

```csharp
public interface IDeadLetterSink
{
    void DeadLetter(IReadOnlyList<TrackingEvent> events);
    IReadOnlyList<TrackingEvent> Snapshot();
    int Count { get; }
    void Clear();
}
```

---

## DI interfaces

The SDK injects these seams for testability:

| Interface | Default | Purpose |
|---|---|---|
| `ITransport` | `SimulatedHttpTransport` / `HttpTransport` | Pluggable event delivery |
| `IEventStore` | `FileEventStore` | Durable persistence |
| `IClock` | `SystemClock` | Timestamps (injectable for deterministic tests) |
| `IDelayer` | Real `Task.Delay` | Retry backoff delays |
| `IRuntimeInfo` | `UnityRuntimeInfo` | Device/app metadata |
| `IConnectivity` | `UnityConnectivity` | Network reachability |
| `IConnectivityProbe` | `HttpConnectivityProbe` | Server-level reachability (HEAD ping) |
| `ITrackingLogger` | `UnityTrackingLogger` | Log routing |
| `IDeadLetterSink` | `InMemoryDeadLetterQueue` | Give-up event storage |
