# Architecture

## Pipeline overview

```
   Caller code
       |
       v
   Public API (ITracker)
   SendMessage(string)  /  SendMapAsync(Dictionary)
       |
       |  1. Validate input (null/empty check)
       |  2. Enrich with metadata (timestamp, sessionId, userId,
       |     sdkVersion, platform, appVersion, device context, idempotency key)
       |  3. Wrap as TrackingEvent
       |
       v
   EventQueue (bounded, thread-safe, lock-free hot path)
       |
       |  Overflow policy: DropOldest or RejectNew
       |  SendMapAsync receives a TaskCompletionSource that resolves on delivery or give-up
       |
       v
   EventDispatcher (background worker thread)
       |
       |  Batch triggers: BatchSize reached OR FlushInterval elapsed OR explicit FlushAsync
       |
       |  For each batch:
       |    - Check connectivity (real HTTP only — offline = hold)
       |    - Check circuit breaker (open = hold until cooldown)
       |    - Attempt delivery via ITransport
       |    - On failure: retry with exponential backoff + jitter
       |    - After MaxRetryAttempts: dead-letter the event
       |    - Resolve each event's TaskCompletionSource (true/false)
       |
       |  Persistence:
       |    - On pause/quit: snapshot queue to IEventStore
       |    - On restart: reload persisted events back into queue
       |
       v
   ITransport
       |
       +-- SimulatedHttpTransport (in-process, no network, optional chaos)
       +-- HttpTransport (real HTTP POST, System.Net.Http.HttpClient)
       +-- NullTransport (always succeeds, discards — for tests)
       +-- (your custom ITransport)
```

## Components

### EventQueue

Thread-safe bounded buffer. The hot path (enqueue from the main thread) is non-blocking — it never
waits on the network. Supports two overflow policies:

- **DropOldest** — evicts the oldest event to make room. Recent events are always accepted.
- **RejectNew** — rejects the incoming event. Preserves the order of what's already buffered.

The queue is the boundary between the caller's thread and the background worker.

### EventDispatcher

Background worker that drains the queue in batches. Responsible for:

- **Batching** — collects up to `BatchSize` events, or flushes after `FlushInterval`.
- **Retry with backoff** — exponential delay (doubled each attempt, with random jitter, capped at
  `MaxRetryDelay`) up to `MaxRetryAttempts`.
- **Circuit breaker** — after `CircuitBreakerThreshold` consecutive failures, stops attempting
  delivery for `CircuitBreakerCooldown`, then sends a trial batch. Success closes the circuit.
- **Connectivity gate** — for real HTTP transport, delivery is held while the device is offline.
  When connectivity returns, buffered events flush automatically.
- **Dead-letter** — events that exhaust retries are moved to `IDeadLetterSink` for inspection.

### ITransport

Pluggable delivery backend. Two built-in implementations:

- **SimulatedHttpTransport** — in-process, no network. Validates the JSON payload, optionally
  injects transient failures (chaos mode). Default for `FakeServer` / `FakeServerChaos`.
- **HttpTransport** — real HTTP POST using `System.Net.Http.HttpClient` (process-lifetime, reused).
  Default for `HttpTestServer` / `HttpTestServerChaos` and custom endpoints.

Implement `ITransport` for custom delivery (e.g., WebSocket, gRPC, custom auth).

### IEventStore (persistence)

Durable storage for events that survive app restarts:

- **FileEventStore** — writes JSON to `Application.persistentDataPath`. Default in production.
- **InMemoryEventStore** — volatile, for tests.
- **NullEventStore** — no-op, for tests that don't need persistence.

The lifecycle hook (`TrackingLifecycle` MonoBehaviour) snapshots the queue to the store on
`OnApplicationPause` and `OnApplicationQuit`. On next startup, persisted events are reloaded into
the queue ahead of new events.

### Connectivity

Two-level reachability checking:

1. **IConnectivity** — device-level: "is a network interface up?" (`UnityConnectivity` wraps
   `Application.internetReachability`).
2. **IConnectivityProbe** — server-level: "does the endpoint answer?" (`HttpConnectivityProbe`
   sends a HEAD request).

The dispatcher checks `IConnectivity` before each batch (real HTTP only). `InitAsync` additionally
runs `IConnectivityProbe` to confirm the target server is reachable before initializing.

### Circuit breaker

Three states: **Closed** (normal), **Open** (halted), **HalfOpen** (trial).

```
Closed --[threshold failures]--> Open --[cooldown]--> HalfOpen
HalfOpen --[trial succeeds]--> Closed
HalfOpen --[trial fails]--> Open
```

Prevents hammering a down server. While open, batches are held (not discarded), so events are
delivered once the server recovers.

### Dead-letter queue

Bounded in-memory store for events that exhausted retries. Capacity is `DeadLetterCapacity` (default
1000). Inspect via `Tracker.DeadLetter.Snapshot()`, clear via `Tracker.DeadLetter.Clear()`. Events in
the dead-letter queue are never retried automatically — they're preserved for diagnostics.

### Metadata enrichment

Every event is automatically enriched with:

| Field | Source |
|---|---|
| `timestamp` | `IClock.UtcNow` (ISO 8601) |
| `sessionId` | Stable GUID per tracker instance |
| `userId` | From config (or `"anonymous"` in privacy mode) |
| `sdkVersion` | `TrackingSdk.VERSION` constant |
| `platform` | `Application.platform` |
| `appVersion` | `Application.version` |
| `id` | Stable GUID per event — the event `Id`, which doubles as the server-side dedup key |

---

## Delivery semantics

**At-least-once.** An event may be delivered more than once (e.g., if the server received it but
the response was lost). Each event carries a stable `Id` that doubles as the key for server-side deduplication.

**Non-blocking.** `SendMessage` returns immediately. `SendMapAsync` returns a `Task<bool>` that
resolves when the batch is delivered (or give-up), but the caller doesn't have to await it.

**Ordered within a batch.** Events within a single batch are delivered in FIFO order. Cross-batch
ordering is best-effort (retries may reorder batches).

---

## Lifecycle

```
Application start
    |
    v
Tracker.Init(userId)
    |  -> TrackingSystem created
    |  -> TrackingLifecycle MonoBehaviour attached (hidden GameObject)
    |  -> Persisted events reloaded from IEventStore
    |  -> Background worker started
    |
    v
Running (accepting events)
    |
    +-- OnApplicationPause(true) --> Persist() snapshot
    +-- OnApplicationPause(false) --> continue
    |
    v
Application.quitting
    |  -> Tracker.Dispose()
    |  -> Persist() final snapshot
    |  -> Worker drain (up to ShutdownDrainTimeout)
    |  -> HttpClient disposed
    |  -> Lifecycle GameObject destroyed
```

---

## DI seams

Every dependency is injectable via `TrackingSystem`'s constructor. All parameters (except `config`)
have production-default fallbacks, so you only inject what you need to control.

```csharp
// Deterministic test: fake clock, recording transport, no delays, no persistence
var config = new TrackingConfig { UserId = "test-user" };
var clock = new FakeClock();
var transport = new RecordingTransport();
var tracker = new TrackingSystem(
    config,
    transport:    transport,
    clock:        clock,
    delayer:      Delayers.Immediate,      // no real delays
    store:        new NullEventStore(),     // no disk I/O
    connectivity: new FakeConnectivity()   // always online
);

// Advance time, trigger flush, inspect what was sent
clock.Advance(config.FlushInterval);
await tracker.FlushAsync();
Assert.AreEqual(1, transport.SentBatches.Count);
```

| Interface | Default | Inject to control |
|---|---|---|
| `ITransport` | `SimulatedHttpTransport` / `HttpTransport` | Where events go |
| `IEventStore` | `FileEventStore` | Durable persistence |
| `IClock` | `SystemClock` | Timestamps and time-based decisions |
| `IDelayer` | Real `Task.Delay` | Retry backoff timing |
| `IRuntimeInfo` | `UnityRuntimeInfo` | Device/app metadata |
| `IConnectivity` | `UnityConnectivity` | Network reachability |
| `ITrackingLogger` | `UnityTrackingLogger` | Log output destination |
| `IDeadLetterSink` | `InMemoryDeadLetterQueue` | Give-up event storage |
| `IConnectivityProbe` | `HttpConnectivityProbe` | Server-level reachability |

---

## Error isolation

The SDK never throws into caller code. Every public method catches exceptions internally and returns
a safe default (`false`, completed task, etc.). Errors are routed through `ITrackingLogger`. A
misbehaving transport, store, or probe can't crash the game.

---

## Thread safety

- `EventQueue.Enqueue` is thread-safe (multiple threads can call `SendMessage` concurrently).
- The background worker is a single thread — no concurrent batch delivery.
- `TrackingConfig` properties are not thread-safe after construction; set them before `Init`.
- `Tracker` (static facade) uses `lock` for initialization; `SendMessage`/`SendMapAsync` are
  lock-free reads of a `volatile` field.
