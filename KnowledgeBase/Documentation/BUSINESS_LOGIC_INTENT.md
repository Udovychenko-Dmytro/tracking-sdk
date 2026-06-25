# Dmytro Udovychenko Tracking SDK — Business Logic Intent (developer-authored)

> **Purpose:** the **developer's** high-level description of *desired* SDK behavior. You (the developer)
> write what should happen, in plain language; Claude reads each entry and implements it — code, tests,
> and the downstream docs ([BUSINESS_LOGIC.md](BUSINESS_LOGIC.md), [DESIGN.md](DESIGN.md), etc.).
> **Authored by:** the developer. **Read & implemented by:** Claude.
>
> **Direction of flow:** this file is the *source* (what you want). [BUSINESS_LOGIC.md](BUSINESS_LOGIC.md)
> is the *result* (what the SDK actually does, kept in sync after implementation). When the two disagree,
> **this file wins** — it's the intent; Claude reconciles the code and the descriptive docs to match.

---

## How this works

1. **You write** one entry per behavior below — plain language, high level. No code, no implementation detail.
2. **Claude implements** it: SDK code + tests + updates to the descriptive docs, following the project rules
   in [CLAUDE.md](../../CLAUDE.md).
3. **Claude reports back** in the entry's `_Claude notes_` line (what it built, where, any clarifying
   questions or edge cases) and flips the **Status**.

### Who edits what

- **You own the prose.** Claude **does not author or rewrite** your entries.
- Claude may only **lightly tidy**: fix an obvious typo, normalize formatting, assign the next `BLI-xxx` id,
  set the `Added` date, and append questions/results **inside the `_Claude notes_` line** — never elsewhere
  in your text. If something you wrote is ambiguous or risky to guess on, Claude asks **before** implementing
  (per the "Ambiguity" Work Rule) rather than editing your intent.

### Writing a good entry (low effort is fine)

- One behavior per entry. Describe the **trigger** ("when X happens") and the **expected result**
  ("the SDK should Y"). A concrete example helps but isn't required.
- Don't worry about *how* — names, APIs, data shapes, thresholds are Claude's job. If you do have a
  preference (a specific config knob, a number, a name), say so and it'll be honored.
- It's fine to leave a number as "TBD" or "you pick a sensible default" — Claude will choose and note it back.

### Status legend

| Status | Meaning |
| ------ | ------- |
| `Proposed` | Written by you; not started. |
| `Questions` | Claude needs a clarification before implementing (see `_Claude notes_`). |
| `In progress` | Being implemented. |
| `Done` | Implemented, verified, docs synced. `_Claude notes_` records what landed. |
| `Parked` | Deferred on purpose. |

---

## Entry template (copy this for a new request)

```markdown
### BLI-00X — <short title>
**Status:** Proposed · **Added:** YYYY-MM-DD

<Plain-language behavior. When does it apply, and what should the SDK do? Add an example if it helps.>

**Acceptance (optional):** how you'll know it's right.

> _Claude notes:_ —
```

> **Next id:** `BLI-008`. (Claude bumps this as entries are added.)
>
> **Drawing a process/flow?** Put it in a fenced ` ```text ` block and draw it like **BLI-001** below
> (boxes for decisions, `yes`/`no` branches, `│`/`▼` spine). Inside a code block the diagram stays aligned
> and generics like `Task<bool>` render literally (outside one, `<bool>` gets eaten as an HTML tag).

---

## Example (illustrative — delete or replace)

This shows the altitude to aim for: behavior and outcome, not implementation. It is **not** an active request.

### BLI-000 — (example) warn once when offline too long
**Status:** Parked · **Added:** 2026-06-24

When the device has stayed offline long enough that the buffer is getting close to full, emit a **single**
warning through the logging hook so the host app can react (e.g. show "you're offline"). Don't spam it — one
warning per offline stretch, and a matching "back online" note when connectivity returns.

**Acceptance:** exactly one warning per offline period; nothing logged while online; threshold is configurable.

> _Claude notes:_ Example only — illustrates trigger → outcome phrasing. Real threshold/wording TBD if activated.

---

## Requests

> **Context (applies to all entries):** user has acces only to the static facade with API Calls.

### BLI-001 — Init process
**Status:** Done · **Added:** 2026-06-24

```text
  Task<bool> InitAsync(userId, server)
            │
            ▼
  ┌───────────────────┐
  │   fake server?    │── yes ──► return true
  └─────────┬─────────┘
         no │
            ▼
  ┌───────────────────┐
  │  device online?   │── no ───► return false
  └─────────┬─────────┘
        yes │
            ▼
  ┌───────────────────┐
  │ server reachable? │── no ───► return false
  └─────────┬─────────┘
        yes │
            ▼
       return true

  · log each step with its result  (steps at Debug, connection failure at Error)
  · "device online?" = NetworkReachability    "server reachable?" = IsServerReachable
```

> _Claude notes:_ Maps to existing `Tracker.InitAsync(userId, server)` → `Task<bool>` (verified init). Mostly a logging add-on — today only failures log; I'll add the fake-skip, positive, and final-result steps. **Decided:** name stays **`InitAsync`** (sync `Init` overloads stay); per-step logs at **`Debug`**; connectivity failures at **`Error`** (was `Warning`, changed per request). One nuance: "no internet" (device offline) is a normal user state, not a server fault — I'll log it at `Error` too as asked, but say the word to keep just that case at `Warning` while server-no-response stays `Error`. **Implemented 2026-06-24:** per-step `Debug` logs added in `HttpConnectivityProbe` (device-online, server-reachable) + `Tracker.InitAsync` (fake-skip, final result); both connectivity failures now log at `Error` (carve-out not taken — say the word). +3 probe log-level tests.

### BLI-002 — bool SendMessage(string message) process
**Status:** Done · **Added:** 2026-06-24

```text
  bool SendMessage(string message)
             │
             ▼
  ┌─────────────────────┐
  │  not initialized?   │── yes ──► log "not initialized" (Warning) ──► return false
  └──────────┬──────────┘
          no │
             ▼
  ┌─────────────────────┐
  │ tracking disabled?  │── yes ──► return false
  └──────────┬──────────┘
          no │
             ▼
  ┌─────────────────────┐
  │   message empty?    │── yes ──► log "empty message" (Error) ──► return false      (null / "" / whitespace)
  └──────────┬──────────┘
          no │
             ▼
   build MESSAGE event  (stamp id, timestamp, session, user, platform, app version)
             │
             ▼
   enqueue into bounded buffer:
       ├─ full + RejectNew ──► drop new, metric Dropped ──► return false
       ├─ full + DropOldest ─► evict oldest (metric Dropped, its awaiter → false), accept new
       └─ has room ──────────► accept
             │
             ▼
   metric Enqueued + signal dispatcher ──► return true
   (true = accepted into the buffer, fire-and-forget — NOT yet delivered)

   ⚠ any exception ──► log Error ──► return false   (never throws into game code)
```

> _Claude notes:_ Drafted from current code (`Tracker.SendMessage` → `TrackingSystem.SendMessage` → `Enqueue`). Refine freely. Key thing to confirm: `SendMessage` is **fire-and-forget** — `true` means *accepted into the buffer*, not *delivered*. **Added (per request):** the "message empty?" branch now logs at **Error** ("nothing to send") before returning `false` — was silent until now. Decided alongside BLI-003: a fully-empty/invalid payload is an **Error** (host-app bug, nothing to send); BLI-003 additionally has a **Warning** tier for partially-bad payloads (some map entries invalid, others still valid). **Implemented 2026-06-24:** the input-validation logging landed in `SendMessage`/`SendMapAsync` (CHANGELOG `[Unreleased]` → Added).

### BLI-003 — Task<bool> SendMapAsync(Dictionary<string, object> map) process
**Status:** Done · **Added:** 2026-06-24

```text
  Task<bool> SendMapAsync(Dictionary<string, object> map)
             │
             ▼
  ┌─────────────────────┐
  │  not initialized?   │── yes ──► log "not initialized" (Warning) ──► Task(false)
  └──────────┬──────────┘
          no │
             ▼
  ┌─────────────────────┐
  │ tracking disabled?  │── yes ──► Task(false)
  └──────────┬──────────┘
          no │
             ▼
  ┌─────────────────────┐
  │     map empty?      │── yes ──► log "empty map" (Error) ──► Task(false)   (null / Count == 0)
  └──────────┬──────────┘
          no │
             ▼
   filter entries: drop any where the key is missing/empty or the value is missing
   (no key/value = no information; each drop logs "invalid map entry" at Warning
    — bad data, but other valid entries may still go out)
             │
             ▼
  ┌─────────────────────┐
  │ empty after filter? │── yes ──► log "empty map" (Error) ──► Task(false)   (every entry was invalid)
  └──────────┬──────────┘
          no │
             ▼
   build MAP event  (copy the filtered map; stamp id, timestamp, session, user, platform, version)
             │
             ▼
   create completion (awaitable result)
             │
             ▼
   enqueue with completion:
       ├─ full + RejectNew ──► complete false ──► return Task(false)
       ├─ full + DropOldest ─► evict oldest (metric Dropped, its awaiter → false), accept new
       └─ has room ──────────► accept
             │
             ▼
   return completion.Task
   (resolves LATER on delivery:  true  = delivered / acked
                                 false = retries exhausted / evicted / purged / disabled
    — always completes, never hangs)

   ⚠ any exception ──► log Error ──► return Task(false)   (never throws)
```

> _Claude notes:_ Drafted from current code (`SendMapAsync` → `Enqueue` with a completion). Refine freely. The returned `Task<bool>` resolves on the **delivery** outcome and is completed on every path (delivered / retries-exhausted / evicted / purged / disabled) so it never hangs. **Added (per request):** per-entry filter after the empty-map check — drop entries with a missing/empty key or a missing (`null`) value, since an entry with no key or no value carries no information (`Dictionary<string, object>` can't hold a literal `null` key, so in practice this means empty/whitespace-only keys — exact check TBD at implementation). **Inferred, flag if wrong:** if filtering empties the map entirely, that's treated the same as the original empty-map case → `Task(false)`, rather than building an event with zero payload entries. **Decided (log levels):** the two "nothing to send" branches (original empty map; empty after filtering) log at **Error**; each entry dropped by the per-entry filter (bad data, but other valid entries may remain) logs at **Warning** instead. Same Error treatment applied to BLI-002's empty-`message` case. **Implemented 2026-06-24:** the input-validation logging landed in `SendMessage`/`SendMapAsync` (CHANGELOG `[Unreleased]` → Added).

### BLI-004 — Task FlushAsync() process
**Status:** Done · **Added:** 2026-06-24

```text
  Task FlushAsync()
             │
             ▼
  ┌─────────────────────┐
  │  not initialized?   │── yes ──► log "not initialized" (Warning) ──► completed Task
  └──────────┬──────────┘
          no │
             ▼
   drain the dispatcher  (force-deliver everything buffered now;
                          don't wait for the batch timer)
             │
             ▼
   await until the queue is empty ──► Task completes
   (returns Task, no bool — just "buffered work has been flushed")

   ⚠ any exception (e.g. drain racing Dispose) ──► log Error ──► swallow, complete normally
```

> _Claude notes:_ Drafted from current code (`Tracker.FlushAsync` → `TrackingSystem.FlushAsync` → dispatcher `DrainAsync`). Refine freely. Returns `Task` (no success bool); errors are logged and swallowed. **Implemented 2026-06-24:** `FlushAsync` is `async` + try/catch/log (error isolation). Note: `DrainAsync` is gated by `CanSend()`, so it force-delivers only what is currently sendable rather than literally awaiting an empty queue while offline/breaker-open (see WARNINGS #8).

### BLI-005 — Dispose process
**Status:** Done · **Added:** 2026-06-24

```text
  Tracker.Dispose()        (renamed from Shutdown)
             │
             ▼
   under lock:
       ├─ take the global instance
       └─ clear static state (m_instance = null)
             │
             ▼
  ┌─────────────────────┐
  │  lifecycle wired?   │── yes ──► unsubscribe Application.quitting,
  └──────────┬──────────┘            tear down lifecycle GameObject
          no │                       (teardown throws ──► log Error, best-effort)
             ▼
   reset "not initialized" warning flag
             │
             ▼
   instance?.Dispose()  (outside the lock):
       ├─ stop & dispose dispatcher/worker  (may block on the final drain)
       └─ dispose owned transport (HttpClient)
             │
             ▼
   done   (safe when not initialized — no-op; call before a second Init to reconfigure)
```

> _Claude notes:_ **Decided:** facade entry point is **`Tracker.Dispose()`**, via **rename `Shutdown` → `Dispose`** (option A — single public name). Public-API change: needs SemVer bump + CHANGELOG, plus updating every `Shutdown` ref (tests, internal `Tracker.cs` calls, DESIGN.md, STATIC_FACADE.md, TASK_PROGRESS.md). Caveat: a static class can't implement `IDisposable`, so `Tracker.Dispose()` is just a named method (no `using` semantics); the instance `TrackingSystem.Dispose()` stays the real `IDisposable`. Flow above drafted from current `Tracker.Shutdown` + `TrackingSystem.Dispose` — refine freely. **Implemented 2026-06-24:** renamed `Tracker.Shutdown()` → `Tracker.Dispose()` across runtime + tests + docs; behaviour unchanged. Version left at `1.0.0` — the breaking change is logged under CHANGELOG `[Unreleased]`; the SemVer bump + release is a user action (per CLAUDE.md "never bump unprompted").

### BLI-006 — Runtime privacy controls (changeable while running)
**Status:** Done · **Added:** 2026-06-24

Knobs the host can flip **at runtime** (not only at Init) to react to consent changes. Two controls:

**1) `SetEnabled(bool)` — master opt-out** (already exists)

```text
  SetEnabled(true)   ──► resume accepting   (new events flow again)

  SetEnabled(false)  ──► stop accepting, then PURGE everything pending
                         (buffer + dead-letter + persisted;
                          awaiting SendMapAsync tasks resolve false)
```

**2) `SetPrivacyMode(bool)` — anonymous mode (GDPR), flip any time** (new)

```text
  SetPrivacyMode(true)   ──► anonymize from now on   (userId → "anonymous")
  SetPrivacyMode(false)  ──► back to normal          (real userId again)

  …and on every event built afterwards:

  build event  (every SendMessage / SendMapAsync)
                │
                ▼
  ┌────────────────────────────┐
  │      privacy mode on?      │── no ──► stamp the real userId  (as today)
  └──────────────┬─────────────┘
            yes  │
                 ▼
   drop the person, keep the rest:
       ✗ userId    → "anonymous"   (no real user identity sent)
       ✓ sessionId → kept          (groups one session; not a person)
       ✓ keep: all device context (BLI-007) + type, payload, timestamp, versions, event-id
                 │
                 ▼
   rest of pipeline unchanged  (batch → retry → persist → dead-letter → deliver)
```

Both react to consent at runtime: **`SetEnabled(false)`** = stop collecting entirely (and erase what's
pending); **privacy mode** = keep collecting, but drop the *user* identity (userId → `"anonymous"`) so
events can't be tied to a person. Privacy mode starts from the `PrivacyMode` config flag (default OFF) and
can be flipped any time via `SetPrivacyMode(bool)`; when ON, the userId passed to Init is ignored.

**Acceptance:** `SetEnabled(false)` → no new events accepted and all pending data purged; `SetEnabled(true)`
→ collection resumes. Privacy mode ON → no event leaving the SDK carries a real userId (it's sent as the
constant `"anonymous"`); sessionId and every other field + the developer's payload still deliver and the
pipeline behaves identically; OFF restores today's behavior. Both togglable while the app runs.

> _Claude notes:_ Two runtime controls. **`SetEnabled`** already exists (`Tracker.SetEnabled` → `TrackingSystem.SetEnabled`: `false` flips the gate under the lifecycle lock, then `Purge()`s) — no code change beyond documenting it here. **`PrivacyMode`** is new: bool on `TrackingConfig` (default `false`) read in `CreateEvent`, plus a runtime setter **`SetPrivacyMode(bool)`** (`Tracker.SetPrivacyMode` + `TrackingSystem.SetPrivacyMode`) so it can change mid-session — parallel to `SetEnabled`. **Decided:** **(a)** keep `sessionId` — it's a per-session id, not per-user (groups one session without naming the person; note: a stable per-session id is still a weak/borderline identifier under strict GDPR, but accepted here). **(b)** `userId` sent as the constant `"anonymous"`. **(c)** keep `event-id` (random per-event GUID for idempotency — describes the event, not the user). **(d)** the developer's payload may carry PII — the SDK can't scrub it; that stays the host's responsibility. **Open:** **(e)** flipping anonymous mode ON only affects *new* events; data already buffered with identity still goes out identified — should turning it ON also purge the already-buffered identified events? **Implemented 2026-06-24:** `TrackingConfig.PrivacyMode` (default off) + `TrackingSystem.SetPrivacyMode`/`IsPrivacyMode` + `Tracker` facade; `CreateEvent` stamps userId `"anonymous"` when on, keeping sessionId + all context. **Decided (e): forward-only** — already-buffered identified events still deliver (no retroactive scrub; use `SetEnabled(false)`/`Purge()` to erase pending). +6 tests, incl. a forward-only test.

### BLI-007 — Data the SDK collects from the device
**Status:** Done · **Added:** 2026-06-24

```text
  Automatic context the SDK stamps on every event   (developer payload sits on top of this)

  ── collected today (code-confirmed) ──────────────────────────────────────
    field        example / source            GDPR
    event-id     random GUID per event       not personal — describes the event
    type         MESSAGE | MAP               not personal
    timestamp    UTC instant                 not personal
    sessionId    per-launch id               per-session, not a person → kept in anon mode
    userId       "player-42"                 PERSONAL → "anonymous" in anon mode
    sdkVersion   e.g. 1.2.3                  not personal
    platform     IPhonePlayer | Android      not personal
    appVersion   Application.version         not personal

  ── adopting: collect all of these (useful, still non-identifying) ─────────
    deviceModel  SystemInfo.deviceModel      not personal (coarse) *
    osVersion    SystemInfo.operatingSystem  not personal
    networkType  wifi | cellular | none      not personal  (SDK already detects this)
    timezone     local UTC offset / IANA     not personal (coarse region)
    locale       Application.systemLanguage  not personal (coarse)
    bundleId     Application.identifier       not personal (same for all users)

  ── deliberately NOT collected (identifying, or permission-gated) ──────────
    carrier      (CTCarrier / telephony)     SKIP — needs telephony permission (Android),
                                             deprecated/empty on iOS 16+
    advertising id (IDFA / AAID)             SKIP — personal, needs ATT/consent
    device id (IDFV / Android ID /           SKIP — stable per-device = identifying
               SystemInfo.deviceUniqueIdentifier)
    precise location / GPS                   SKIP — personal
    IP address                               not sent by the SDK (server still sees source IP)
    name / email / phone                     only if the app puts it in payload (its choice)

  * fingerprint note: deviceModel + osVersion + timezone + locale together
    approximate a device fingerprint → quasi-identifying under strict GDPR. Keep it coarse;
    never add stable per-device IDs.
```

**What differs under GDPR / privacy mode (BLI-006):** by design the SDK collects only coarse,
non-identifying context. **Rule — privacy mode drops only `userId` (→ `"anonymous"`) and keeps everything
else, including `deviceModel`.** Why `deviceModel` stays: a model like "iPhone14,5" is shared by millions,
so it can't single out a person on its own; fingerprinting only bites when many high-entropy signals are
combined with a *stable* identifier — and privacy mode has none (no userId, no device ID, `sessionId`
resets each launch). The guardrail that keeps this true: never add stable device IDs or high-entropy
fingerprint signals (screen, fonts, sensors). The SDK never collects advertising IDs, stable device IDs,
or location.

**Acceptance:** the "collected today" list matches what `CreateEvent` actually stamps; nothing in "NOT
collected" ever appears in an event; whichever "proposed" fields we adopt are documented here and added
end-to-end (event model → serializer → receiver); under anonymous mode only `userId` is replaced.

> _Claude notes:_ Catalog entry. "Collected today" is code-confirmed (`TrackingEvent` + `CreateEvent`; runtime values from `IRuntimeInfo`/`UnityRuntimeInfo`, which today expose only `Platform` + `AppVersion`). "Proposed" fields are NOT collected yet — adding them touches `IRuntimeInfo`, `TrackingEvent`, `CreateEvent`, `EventSerializer`, the JSON model, the `track.php` receiver, and tests. Feasibility: `carrier` via `CTCarrier` is deprecated on iOS 16+ (usually empty) and needs telephony perms on Android — lowest value, suggest skipping unless you need it. **Decided:** collect everything feasible — `deviceModel` + `osVersion` + `networkType` + `timezone` + `locale` + `bundleId`. `carrier` **skipped** (needs telephony permission on Android, deprecated/empty on iOS 16+ — per your "skip if it needs a permission"). Privacy-mode rule (decided): only `userId` → `"anonymous"`; **`deviceModel` and all other context kept** — none singles out a person without a stable ID, which we don't collect. (A stricter variant that also strips `deviceModel` + `timezone` + `locale` for max caution is one flag away if you want it.) **Implemented 2026-06-24:** added `deviceModel`, `osVersion`, `networkType`, `timezone` (UTC offset), `locale`, `bundleId` end-to-end (`IRuntimeInfo`/`UnityRuntimeInfo` → `TrackingEvent` → `CreateEvent` → `EventSerializer` round-trip → `track.php` accepts via raw-body log); `carrier` skipped. `UnityRuntimeInfo` snapshots all fields at ctor (main thread) to keep off-thread enrichment safe. +2 tests incl. a forbidden-fields guard (no device-id/IDFA/IP/location/carrier).
