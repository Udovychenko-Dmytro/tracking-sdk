# Final Report — PR/MR description + how-to-verify + commit messages

Produced during finalization (step 7, see [FINALIZATION_RULES.md](FINALIZATION_RULES.md) and the `/finalize` command). Goal: hand the user three copy-paste-ready blocks — the **PR / Merge Request description**, the **how-to-verify** steps, and the **commit message(s)** — so they paste them verbatim.

> **Ownership:** this file is the **template source of truth** that `/finalize` step 7 and [FINALIZATION_RULES.md](FINALIZATION_RULES.md) step 7 point to. Edit the block templates **here**; those two only reference them.

> Context: this is an **SDK**, not a broadcast app. The remote is GitHub; a change ships either as a GitHub **Pull Request** or as part of the packaged release. There is no Bitbucket/Jira and no separate QA team — "verification" is the deterministic EditMode test suite plus exercising the public API via the demo sample. There are **no submodules** and **no UI screenshots** to attach (it's a code SDK; the only UI is the optional Canvas/uGUI demo panel).

## How to produce it

1. Draw the **description** from `TASK_PROGRESS.md` + the actual diff — describe the *why* and which implicit requirements it covers, not a file dump. Cross-reference the root `README.md` scorecard and the `KnowledgeBase/Documentation/DESIGN.md` rationale.
2. Draw the **how-to-verify** from what the change actually affects: the headless EditMode run, plus any specific behaviour a human can confirm via the demo (`Assets/TrackingDemo`, auto-spawns on Play) or the `[Category("Live")]` live tests.
3. Draw the **commit message(s)** from the `Changes` entries — concise, lowercase, imperative. One repo (no submodule ordering).
4. Present the blocks in fenced ```` ```markdown ```` blocks so the user can copy them as-is. Keep it honest: only list verification steps that exercise what this task changed.

## Block 1 — PR / MR description

```markdown
## Summary
<1-3 sentences: what changed and why.>

## Changes
- <feature/file-level change 1>
- <feature/file-level change 2>

## Implicit requirements covered
- <which production-grade concerns this touches: non-blocking hot path, bounded queue,
  retries/backoff, persistence, lifecycle flush, privacy, determinism — or "n/a">

## Tests
- EditMode suite: `total=NN passed=NN skipped=N` (headless run). <note new tests added>

## Notes / risks
- <behaviour changes, new config defaults, public-API surface changes — or "none">
- Rollback: <how to revert if it misbehaves — or "low risk">
```

Notes:
- Title (set separately): short lowercase imperative — e.g. `add dead-letter replay api`.
- Call out any **public API surface change** (new/renamed type or method on `ITracker` / config) prominently — it affects consumers and the package `CHANGELOG.md`.

## Block 2 — How to verify

```markdown
**Pre-conditions:** open the Unity project `tracking-sdk/` in Unity <read from ProjectVersion.txt>; Unity Editor closed for the headless run.

**Automated (authoritative):**
1. Run the headless EditMode suite (see CLAUDE.md "Build & Verify") → expect `failed="0"`, `passed=NN`.
   (Or: Window → General → Test Runner → EditMode → Run All inside the Editor.)

**Manual (optional, via the demo):**
1. Press Play → the Canvas/uGUI demo panel appears (auto-spawned). → <action> → <expected on-screen counter/result>.

**Live transport (`[Category("Live")]`, part of the default suite — needs network):**
- <note the live tests (`LiveTransportTests` / `LiveRetryTests`) against the deployed receiver, if this change touches transport/retry>.

**Edge cases to confirm:**
- <only those this change can hit: empty/null input, overflow policy, offline hold→flush, cancel/Dispose, corrupt persisted file>.
```

Notes:
- One step = one observable check. Avoid "verify it works" — say what the green test count or the demo panel should show.
- Pull edge-case categories from [FINALIZATION_RULES.md](FINALIZATION_RULES.md) step 6.

## Block 3 — Commit message(s)

One message (single repo, no submodules). Convention: short lowercase imperative, matching the existing log (`base is ready`, `phase 6`, `add comands`).

```markdown
<short lowercase imperative, e.g. "add circuit-breaker half-open recovery">
```

Notes:
- **The assistant does not commit or stage on its own** — these are messages for the user to paste, or to use only when the user explicitly asks the assistant to commit. See [CLAUDE.md](../../CLAUDE.md) "Commits & staging".
