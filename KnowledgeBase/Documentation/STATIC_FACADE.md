# Static `Tracker` facade

> **Purpose:** how the public static `Tracker` entry point relates to the `TrackingSystem` instance core, and the policy for every static-state hazard the facade closes.
> **Key files:** `Runtime/Tracker.cs` (facade), `Runtime/TrackingSystem.cs` (instance core), `Runtime/Util/TrackingLifecycle.cs`, `Tests/Editor/StaticFacadeTests.cs`.

## Shape: a thin facade, never a replacement

Game code wants `Tracker.Init(userId)` once and then `Tracker.SendMessage(...)` — no instance to hold or thread through callers. So the **public entry point is the static `Tracker` class**. It is a *thin wrapper*: it holds one configured `TrackingSystem` behind `Init(...)` and forwards every call. It contains no pipeline logic.

The pipeline lives in **`TrackingSystem`** (formerly the class named `Tracker`, renamed in this change). It stays `public`, constructed with every external concern injected (transport / clock / store / connectivity / logger / dead-letter), and is still:
- the **testing surface** — tests build `new TrackingSystem(…, startWorker: false)` and pump delivery deterministically;
- the way to run **multiple trackers** or do advanced DI.

`Tracker.Current` exposes the underlying instance (or `null` before `Init`).

## Init overloads

| Call | Behaviour |
| ---- | --------- |
| `Tracker.Init(userId)` | Builds the production `TrackingSystem` (default endpoint, simulated) and adopts it. |
| `Tracker.Init(userId, ServerEnvironment)` | Named server (`FakeServer`/`FakeServerChaos` simulated; `HttpTestServer`/`HttpTestServerChaos` real HTTP). The `*Chaos` variants inject ~20% transient failures. |
| `Tracker.Init(userId, endpoint)` | Custom endpoint, real HTTP. |
| `Tracker.InitAsync(userId, ServerEnvironment)` → `Task<bool>` | Async: confirms the **target server** is reachable (HEAD ping) for HTTP servers, then inits; the simulated `FakeServer*` variants skip the check. Returns whether a tracker is now live. |
| `Tracker.InitAsync(userId, endpoint)` → `Task<bool>` | Async: confirms the custom endpoint is reachable, then inits. |
| `Tracker.Init(TrackingSystem, attachLifecycle = true)` | Adopts a **pre-built** instance (advanced / DI / tests). `attachLifecycle: false` skips all Unity scene/quit wiring — the path the tests use. |

The three `string` overloads mirror `TrackingSystem.Init(...)` and always auto-wire lifecycle.

**Trace verbosity.** Every `Init` / `InitAsync` overload takes an optional trailing `minLogLevel`
(`TrackingConfig.MinLogLevel`, default `Warning`): `Info` adds lifecycle step traces (init / enqueue /
deliver), `Debug` adds event payloads + the serialized wire JSON. On `InitAsync` it sits after the
`CancellationToken` (`InitAsync(userId, server, ct, minLogLevel)`); the pre-init reachability-probe
diagnostics stay always-on (it governs the initialized tracker's pipeline, not the probe).

**Server-reachability check.** `Tracker.IsServerReachableAsync(endpoint)` → `Task<bool>` (`IConnectivityProbe` / `HttpConnectivityProbe`) runs two stages: (1) interface fast-fail (`internetReachability`) — failure logs "no internet"; (2) a HEAD ping to the endpoint — **any HTTP response means reachable** (even 405, which `track.php` returns to a non-POST; only a transport error/timeout is "not responding", logged with the endpoint). Use it to gate your own Init, or let `InitAsync` call it (it pings the resolved server endpoint). It's `HttpClient`-based; `InitAsync`'s post-probe init resumes on the main thread (no `ConfigureAwait(false)` on that await) because it builds Unity objects. The probe is **error-isolated** (never throws into caller code — a throwing probe resolves `false`).

**Offline gate.** When a `string` overload targets real HTTP delivery (an `HttpTestServer*` `ServerEnvironment` or a custom endpoint) while the device is offline, `TrackingSystem.Init(...)` returns `null` and the facade stays uninitialized (`IsInitialized == false`) — the factory logs one actionable warning. The simulated `FakeServer*` path (and `Init(userId)`) never blocks, so offline buffering still works there. No auto-retry: the caller polls `IsInitialized` and re-`Init`s once connectivity returns. Gate logic: `TrackingSystem.IsBlockedOffline(mode, connectivity)`; reachability comes from `Application.internetReachability` (interface-level, not a true-internet probe — see [WARNINGS.md](WARNINGS.md)).

## Static-state hazards and how each is closed

| Hazard | Policy |
| ------ | ------ |
| **Call before `Init`** | No-op that returns a safe default — `SendMessage` → `false`, `SendMapAsync` → completed `Task` (`false`), `FlushAsync` → `Task.CompletedTask`, diagnostics → zero/null. **Never throws, never hangs.** Warns **once** per session (these calls land in `Update` loops). |
| **Double `Init`** | Ignored — the first tracker is kept and a warning is logged. Reconfiguring (e.g. a new user) is an explicit `Dispose()` then `Init()`. |
| **Bad input to `Init`** | The only throwing path, by design (setup-time error, distinct from hot-path error isolation): blank `userId` → `ArgumentException`, null instance → `ArgumentNullException`. A failed `Init` leaves **no** half-built global. |
| **Disposal / lifecycle** | The production overloads auto-wire `TrackingLifecycle` (persist on background/quit) **and** `Application.quitting += Dispose` (dispose on quit), so game code drops the manual `TrackingLifecycle.Attach` + `OnDestroy`. |
| **Domain reload off** | `[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]` calls `Dispose()` at play start, disposing any leftover so the previous session's worker thread / `HttpClient` don't leak. The same `Dispose()` keeps EditMode tests isolated — `StaticFacadeTests` calls it in `[TearDown]`. |
| **Thread-safety** | `m_instance` is `volatile`; the hot path reads it into a local then forwards. `Init` / `Dispose` / adopt mutate state under `m_gate`; the instance `Dispose` runs **outside** the lock (it may block on the worker's final drain). |

## Test guidance

Facade tests adopt a deterministic instance via `Tracker.Init(instance, attachLifecycle: false)` (no GameObject, no quit hook), drive delivery with `Tracker.FlushAsync()`, and **must** call `Tracker.Dispose()` in `[TearDown]` so the global static state cannot leak across tests.
