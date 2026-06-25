---
description: Orchestrator mode — the lead Claude frames the task, cuts it into independent parts, and drives role-agents through the Agent tool (architect → implementer → tester → reviewer with an auto fix loop → docs), reviewing each one's diff and keeping only quality changes in the working tree. Verifies with the headless Unity EditMode test run. Does NOT commit and does NOT finalize. Invoke when a task is large enough to split into roles.
argument-hint: "<what to do; optionally a reference to a phase in TASK_PROGRESS.md>"
allowed-tools: Agent, Bash, Read, Edit, Write, Grep, Glob, AskUserQuestion
---

# /orchestrate-task — task orchestrator via role-agents

You are the **lead developer-orchestrator**. You don't write everything yourself: you frame the task, cut it into independent parts, and hand roles to subagents (`Agent` tool), while you stay in the loop — reviewing each agent's diff and keeping **only quality** changes in the working tree. This is the orchestrator pattern: one main process coordinating Architect/Implementer/Tester/Reviewer/Docs.

How this differs from its neighbours: `/self-review` and `/self-review-uncommitted` drive an **external CLI** (a fresh Claude process, clean context) for review. Here the orchestration is **in-process** — Claude subagents with fresh context, a shared working tree, structured returns. You conduct; they perform.

## Roles

| Role                          | `subagent_type`                                 | Access                    | What it does                                                                                                                                          |
| ----------------------------- | ----------------------------------------------- | ------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------- |
| **Orchestrator (you)**        | — (main session)                                | read/write/`Agent`        | frames the task, cuts it into parts, spawns agents, reviews each diff, keeps only quality work, verifies with the EditMode test run                   |
| **Agent 1 — Architect**       | `Plan`                                          | read-only                 | analyses the existing architecture for the task: critical files, patterns to follow, the DI/transport/persistence seams touched, risks, how to cut it |
| **Agent 2 — Implementer**     | `general-purpose`                               | read/write                | implements one part bottom-up: model/event → queue/dispatcher → transport/persistence → demo wiring                                                   |
| **Agent 3 — Tester**          | `general-purpose`                               | read/write                | writes deterministic EditMode tests via the DI seams (fakes + virtual clock, no real network/disk), runs them                                         |
| **Agent 4 — Reviewer**        | `general-purpose` (read-only **by instruction**)| returns findings only     | hunts bugs, concurrency hazards, error-isolation gaps, standards violations, SDK edge cases (see the risk list)                                       |
| **Fix-agent** (in the loop)   | `general-purpose`                               | read/write                | fixes the **accepted** reviewer findings                                                                                                              |
| **Agent 5 — Docs**            | `general-purpose`                               | read/write                | syncs docs ↔ code, updates README/CHANGELOG/KnowledgeBase, logs deferred items to WARNINGS.md                                                         |

> No read-only subagent type exists for audit: `Explore` is a "locator, not an auditor" by contract, so the reviewer is `general-purpose` with a hard instruction "analyse only, do not edit". The safety net is that you review the tree after every agent (Step 5).

## Boundaries (absolute — orchestration does NOT lift them)

- **Commits/push — never** without an explicit "commit" from the user in the current turn. On `main`, **branch first**. This command leaves changes in the working tree and reports — it never commits or stages. (CLAUDE.md "Commits & staging" rule.)
- **Finalization is NOT part of this command.** Stop at "functionally done + verification green" and report. Finalization (the [`/finalize`](finalize.md) pass with its "ready to finalize?" gate) is run by the user.
- **Deliberate-decision gate stays with the user.** If an agent hits something locked by a documented decision in [`DESIGN.md`](../../KnowledgeBase/Documentation/DESIGN.md) (async batch-delivery semantics, drop policy, at-least-once + idempotency, `HttpClient` vs `UnityWebRequest`, the DI seams), the task's stated intent in [`TASK_PROGRESS.md`](../../KnowledgeBase/Documentation/TASK_PROGRESS.md), or an established codebase convention → record the recommendation + "needs a decision" in `TASK_PROGRESS.md` and [WARNINGS.md](../../KnowledgeBase/Documentation/WARNINGS.md), and do the unblocked work.
- **Scale to the task.** Don't spawn 5 agents for a one-line fix: collapse stages or do trivial work yourself. Orchestration pays off only when the task genuinely splits. Pick the depth and state it in the plan.

## Constants

- `MAX_REVIEW_ROUNDS = 3` — ceiling for the auto review→fix→re-review loop (Step 5). A named threshold, not a magic number (per CODING_STANDARDS — no magic values).

The command argument (`$ARGUMENTS`) is the task description (+ an optional reference to a TASK_PROGRESS phase). Empty → this is not a branch, it's a missing spec: ask in one sentence for the task and the done criterion, and **stop**.

> **Timeout and compaction:** this run is long — several sequential `Agent` calls, each minutes long; context may compact along the way. Your anchors are `TASK_PROGRESS.md` (survives compaction) and `$ADIR`. Besides the `## Plan` with `[x]` items, keep a coarse phase tracker `## Checkpoint (orchestrate)` (created in Step 0): which of Steps 0–7 you're on. **Update it on EVERY Step N→N+1 transition.** After compaction, read the checkpoint + `## Plan` first and resume from the right phase, **without re-running finished steps** (e.g. architect/implementation already `[x]`).

---

## Step 0 — Setup: leftover, spec, scope, artifacts

1. **Resolve leftover — the very first step before any code edit:** if the `TASK_PROGRESS.md` belongs to a different task, resolve it (continue/restart) per [TASK_PROGRESS_RULES.md](../../KnowledgeBase/BehaviourRules/TASK_PROGRESS_RULES.md). In parallel, glance at [WARNINGS.md](../../KnowledgeBase/Documentation/WARNINGS.md) for a passing closeable item.
2. Parse `$ARGUMENTS` into a **Goal** (what we do) and **DoD** (done criterion). Empty → ask for a spec and stop (see above). A reference to a TASK_PROGRESS phase → read that phase + the plan appendix in [`TASK_PROGRESS.md`](../../KnowledgeBase/Documentation/TASK_PROGRESS.md) and [`DESIGN.md`](../../KnowledgeBase/Documentation/DESIGN.md).
3. **Ambiguity on architecture / API shape / data contract / naming** → ask a clarifying `AskUserQuestion` **before** spawning agents. Don't guess on decisions costly to reverse; small cosmetic choices resolve yourself. A conflict with a documented decision (`DESIGN.md`) or an established convention is always a question to the user, not a guess.
4. Create the artifacts dir (self-ignored) and capture the branch. Operate from the repo root so paths resolve from there, not the Unity subfolder:

   ```bash
   cd "$(git rev-parse --show-toplevel)"
   ADIR=".claude/artifacts/orchestrate-task"
   mkdir -p "$ADIR" && { [ -f .claude/artifacts/.gitignore ] || printf '*\n' > .claude/artifacts/.gitignore; }
   BRANCH=$(git rev-parse --abbrev-ref HEAD 2>/dev/null || echo "(no-git)")
   echo "branch=$BRANCH  artifacts=$ADIR"
   git --no-pager status --short
   ```

   The tree should be clean or hold only task-related changes — otherwise your "diff after an agent" is polluted. Dirty/unrelated → resolve it in step 1 (continue/restart) before spawning.

5. Create/extend `TASK_PROGRESS.md`: `## Goal / Context` (Goal + DoD) and `## Plan` (a draft — you refine it after Agent 1).
6. Add a `## Checkpoint (orchestrate)` section there — a coarse phase tracker for compaction resilience (update on every Step N→N+1 transition; read it first after a compaction):

   ```markdown
   ## Checkpoint (orchestrate)

   - [x] Step 0 — setup (branch <branch>, leftover resolved)
   - [~] Step 1 — Agent 1 architect
   - [ ] Step 2 — decomposition (## Plan finalized)
   - [ ] Step 3 — Agent 2 implementation (detail — ## Plan)
   - [ ] Step 4 — Agent 3 tests
   - [ ] Step 5 — Agent 4 review + fix loop (round ?/MAX_REVIEW_ROUNDS)
   - [ ] Step 6 — Agent 5 docs
   - [ ] Step 7 — final gate + whole-diff review
   ```

   Legend: `[x]` done · `[~]` in progress · `[ ]` not started. The coarse "which phase" lives here; "which step inside a phase" (parts, review rounds) lives in `## Plan`.

---

## Step 1 — Agent 1: architectural analysis (`Plan`, read-only)

Spawn the **architect** to understand where and how the task lands before writing anything. It's read-only by construction (`Plan` has no Edit/Write).

Build the agent prompt from the **context preamble** (see below) + the architect-specific ask:

> Analyse the existing architecture for the task "<Goal>". Read `CLAUDE.md` (already in your context), [`DESIGN.md`](../../KnowledgeBase/Documentation/DESIGN.md), and the relevant code under `tracking-sdk/Packages/com.dmytroudovychenko.tracking/Runtime`. Return strictly: **(1) critical files** (`path:symbol` + why); **(2) project patterns** the implementation must follow (with code examples); **(3) data/seam touch-points** — which interfaces (`ITransport`/`IEventStore`/`IClock`/`IDelayer`/`IConnectivity`), `TrackingConfig` fields, or the type-tagged payload encoding are affected; **(4) proposed cut** into independent parts (what depends on what, what can run in parallel); **(5) risks and edge cases** (the SDK failure-mode walk). Analyse only — change nothing.

Read the return. Save the gist to `$ADIR/architecture.md` (durable across compaction within this run; `$ADIR` is transient between runs but it's your anchor during one). → mark `[x] Step 1`, `[~] Step 2` in `## Checkpoint (orchestrate)`.

---

## Step 2 — Decomposition: cut into independent parts

**This is your job, not an agent's** (the lead developer cuts the task). Based on Agent 1:

1. Split the task into parts by layer and dependency: model/event → queue/dispatcher → transport/persistence → demo wiring. A part = an atomic, verifiable chunk.
2. Mark dependencies: `B after A`, `C ∥ D` (independent).
3. Write the final `## Plan` in `TASK_PROGRESS.md` — one item per part, `[ ]`, in execution order. Declare the chosen **orchestration depth** (how many parts, what runs in parallel) — that's "scale to the task".
4. **Checkpoint:** `## Plan` finalized → mark `[x] Step 2`, `[~] Step 3` in `## Checkpoint (orchestrate)`.

---

## Step 3 — Agent 2: implementation (one part at a time)

For each part in `## Plan`, spawn an **implementer** (`general-purpose`). The prompt = context preamble + the relevant excerpt from `$ADIR/architecture.md` (files/patterns) + the exact part spec + what to return (list of changed files + a one-line "what I did").

**Merge = your quality gate, not git-merge.** Edit/Write agents write straight into the shared working tree — the changes are "already in" when the agent returns.

**Before spawning each agent, snapshot the tree** so you can roll back ONLY its edits without disturbing accepted work from earlier parts or your own WIP in the same files:

```bash
SNAP=$(git stash create); [ -n "$SNAP" ] || SNAP=HEAD   # snapshot of tracked state WITHOUT touching the tree; clean tree → SNAP=HEAD
```

So after each one:

1. Look at `git --no-pager diff -- <touched paths>`.
2. **Quality** (style per CODING_STANDARDS — `m_camelCase` fields, `UPPER_SNAKE_CASE` consts, `sealed` classes, Allman braces, no `var`, DI via optional ctor params, error isolation, `ITrackingLogger` not `Debug.*`, `ConfigureAwait(false)`, `TryGet`+`out`; no placeholders/stubs; tunables on `TrackingConfig`, no magic values) → keep it.
3. **Broken/incomplete/violates a standard** → roll back **only this agent's edits**, restoring the touched files from the snapshot: `git restore --source="$SNAP" --worktree -- <files>` (working tree only, index untouched; NOT `git checkout "$SNAP" -- <files>`, which also stages, and NOT `git restore -- <files>` to HEAD, which would wipe accepted work from earlier parts and your WIP in the same files; a new file the agent created that isn't in `$SNAP` → remove with `git rm -f`/`rm`). Then **re-run the agent with corrective feedback**, or fix it surgically yourself. That's how "merge only quality changes" works.
4. After a part — run the verification gate incrementally (below). Red → fix to green. Mark `[x]` in `## Plan`, append to `## Changes`.

**Sequential is the default.** Parallel only for parts with **non-overlapping** files: spawn implementers with `isolation: "worktree"` (otherwise they clobber each other in the shared tree), then integrate the worktree diffs and review as usual. It's expensive (a worktree per agent) — justified only for genuinely separate parts.

> All parts implemented and accepted → mark `[x] Step 3`, `[~] Step 4` in `## Checkpoint (orchestrate)`.

### Verification gate (headless Unity EditMode tests)

Run **only when compile-affecting files changed** (`.cs`/`.asmdef`/`.asmref`); for a docs/PHP/asset-only diff, **skip it**. The **Unity Editor must be closed** (single-instance lock). Large timeout (batchmode startup + tests take a few minutes; the Bash tool will usually auto-background it).

```bash
cd "$(git rev-parse --show-toplevel)"; ADIR=".claude/artifacts/orchestrate-task"
ROOT="$(git rev-parse --show-toplevel)"; PROJ="$ROOT/tracking-sdk"
CHANGED=$( { git --no-pager diff --name-only HEAD; git ls-files --others --exclude-standard; } | sort -u )
if printf '%s\n' "$CHANGED" | grep -qiE '\.(cs|asmdef|asmref)$'; then
  VER=$(awk '/^m_EditorVersion:/{print $2}' "$PROJ/ProjectSettings/ProjectVersion.txt")
  UNITY="/Applications/Unity/Hub/Editor/$VER/Unity.app/Contents/MacOS/Unity"
  if [ -f "$PROJ/Library/EditorInstance.json" ]; then
    PID=$(grep -o '"process_id" : [0-9]*' "$PROJ/Library/EditorInstance.json" | grep -o '[0-9]*')
    [ -n "$PID" ] && ps -p "$PID" >/dev/null 2>&1 && { echo "VERIFY BLOCKED: Unity Editor open (PID $PID) — close it and re-run."; exit 0; }
  fi
  [ -x "$UNITY" ] || { echo "WARN: Unity not at $UNITY"; exit 0; }
  rm -f "$ADIR/tests.xml" "$ADIR/tests.log"
  "$UNITY" -runTests -batchmode -nographics -projectPath "$PROJ" -testPlatform EditMode -testResults "$ADIR/tests.xml" -logFile "$ADIR/tests.log"
  echo "unity exit=$?"; grep -m1 -o '<test-run[^>]*>' "$ADIR/tests.xml" 2>/dev/null || { grep -iE 'error CS[0-9]+|Compilation failed' "$ADIR/tests.log" | head; tail -15 "$ADIR/tests.log"; }
else
  echo "SKIP: no .cs/.asmdef/.asmref changed — tests not needed (docs/php/asset-only)."
fi
```

Green = `failed="0"` with a non-zero `passed` (suite is currently **145 deterministic tests** + 2 `[Category("Live")]` live tests that run in the default headless suite and need network). Any failure or compile error → fix to green before moving on.

---

## Step 4 — Agent 3: tests

Spawn the **tester** (`general-purpose`): context preamble + a summary of the implemented diff. Task: deterministic EditMode tests for the new behaviour through the **DI seams** — construct the `TrackingSystem` with `startWorker: false` and drive delivery explicitly (`FlushAsync()` / the dispatcher's `PumpOnceAsync` / `DrainAsync`), use the fakes (`ITransport`/`IEventStore`/`IClock`/`IDelayer`/`IConnectivity`) + a virtual clock; cover the edge cases from Agent 1 and the SDK failure-mode walk. **No real network/disk/wall-clock in the default suite — live-network tests must be `[Category("Live")]`.** Run the EditMode suite. Return: which tests were added (`file`), run status.

Review the tests with the same quality bar (Step 3) — placeholder / always-green tests are not accepted. Run the suite yourself, don't take the agent's word (report results honestly).

> Tests added and green → mark `[x] Step 4`, `[~] Step 5` in `## Checkpoint (orchestrate)`.

---

## Step 5 — Agent 4: review + auto fix loop (≤ `MAX_REVIEW_ROUNDS`)

A "find → fix → re-check" loop, like `/self-review`, but with subagents.

**Round k (1 to `MAX_REVIEW_ROUNDS`):**

1. Spawn the **reviewer** (`general-purpose`, **read-only by instruction**): context preamble + "check against `git diff` and the working tree; analyse only the changed code and its context". Return format, strict:
   `[SEV] file:line — problem. Fix: concrete suggestion.` where SEV ∈ `BLOCKER|MAJOR|MINOR|NIT`. End with `## Good` (what was done well). **Report only — do not edit, do not run mutating commands.** Save to `$ADIR/review-round{k}.md`.

   The reviewer should look hard at the SDK's real risk areas: **bugs/logic errors; CONCURRENCY** (the bounded queue is hit from any thread and drained by a background worker — lock correctness, no torn state, no unobserved `Task` exceptions, `ConfigureAwait(false)` on library awaits, `TaskCompletionSource` completed on EVERY path: delivered / retries-exhausted / evicted / rejected / purged / cancelled, no deadlock from blocking on async, no main-thread assumption on the worker — `UnityWebRequest` is main-thread-only, `HttpClient` is deliberate); **ASYNC + cancellation/`Dispose`** (token honored, worker stops clean, `HttpClient`/`SemaphoreSlim` disposed); **ERROR ISOLATION** (the public API never throws into game code — validate, swallow+log via the logger seam); **TEST DETERMINISM** (no real network/disk/wall-clock in the default suite — DI seams/fakes + a virtual clock; live tests are `[Category("Live")]`); **SERIALIZATION** (`JsonUtility` can't do `Dictionary<string,object>` → the type-tagged payload encoding; round-trip + corrupt/missing-file resilience); at-least-once + idempotency (stable event id reused on retries); bounded-memory drop policy (`DropOldest` vs `RejectNew`); config defaults on `TrackingConfig`; public API surface vs docs/CHANGELOG; naming; dead code; concise comments.
   - _Paranoia (optional):_ compare a tree fingerprint before/after (`{ git diff HEAD; git status --porcelain; } | shasum`) — if it changed, the reviewer accidentally touched code; investigate.
2. **Triage** (don't apply blindly): accept by priority `BLOCKER → MAJOR → MINOR → NIT`; **reject** if it conflicts with a documented decision (`DESIGN.md`), the task's stated intent (`TASK_PROGRESS.md`), or an established convention — record the reason.
3. Accepted findings → a **fix-agent** (`general-purpose`) in one batch, or fix surgically yourself. Same quality gate on its diff (Step 3).
4. Run the verification gate after fixes. Red → fix to green.
5. **Exit the loop:** zero accepted findings (only NIT/`## Good`) **OR** `MAX_REVIEW_ROUNDS` reached. Ceiling hit with open BLOCKER/MAJOR → don't bury it: state it explicitly in `## Orchestrate-task summary`, and genuinely deferred items → [WARNINGS.md](../../KnowledgeBase/Documentation/WARNINGS.md). Loop closed → `[x] Step 5`, `[~] Step 6` in `## Checkpoint (orchestrate)`.

---

## Step 6 — Agent 5: documentation

Spawn the **docs-agent** (`general-purpose`): context preamble + the list of changed files/symbols. Task (CLAUDE.md "Documentation" / "Sync docs with code"): grep `KnowledgeBase/**/*.md`, `CLAUDE.md`, and the root docs (`README.md`, `DESIGN.md`, `TASK_PROGRESS.md`, the package `README.md`/`CHANGELOG.md`, the package `Documentation~/`) for the touched paths/symbols; update every reference in the same pass (anchor lines, method/field names, and the **test count**, which appears in several files). A new reference doc → `KnowledgeBase/Documentation/` + a link from [`INDEX.md`](../../KnowledgeBase/INDEX.md) (per [DOCUMENTATION_RULES.md](../../KnowledgeBase/BehaviourRules/DOCUMENTATION_RULES.md)); README — only if the public surface changed. Couldn't finish everything → mark `<!-- STALE: reason -->` + report it. Return: what was updated (`file`).

Review the docs diff with the same bar. `TASK_PROGRESS.md` is a **committed project doc** — update it, never delete or recreate it.

> Docs updated → mark `[x] Step 6`, `[~] Step 7` in `## Checkpoint (orchestrate)`.

---

## Step 7 — Final gate and whole-diff review

1. Full verification gate (the block in Step 3, run from a clean point against the whole diff). Skip for a docs/PHP/asset-only diff.
2. Re-read the **whole** `git --no-pager diff` against base once more: integrity across parts (Step 3 reviewed part-by-part — here check the seams, duplicates, dead code, the SDK edge-case walk on the final code).
3. The SDK edge-case walk on the final code: `SendMessage(null)` / empty map / `Result == null`; thread-safety (queue hit from many threads while the worker drains); `TaskCompletionSource` completed on every path; cancellation + `Dispose` (worker stops, `HttpClient`/`SemaphoreSlim` released); offline hold → flush; overflow policy (`DropOldest` vs `RejectNew`); at-least-once + idempotency; corrupt/missing persisted file; `JsonUtility` round-trip. Fix or flag explicitly.
4. All green and coherent → mark `[x] Step 7` in `## Checkpoint (orchestrate)` (all phases closed), go to the report (Step 8).

---

## Step 8 — Final report to the user

A `## Orchestrate-task summary` section:

- **Goal / DoD** — 1-2 lines, achieved or not.
- **Cut** — how many parts, what ran in parallel, the chosen orchestration depth.
- **Per agent** — briefly for each (Architect/Implementer×N/Tester/Reviewer/Docs): what it did; what of its diff you **kept**, what you **rolled back/redid** and why (that's "merge only quality").
- **Review loop** — rounds out of `MAX_REVIEW_ROUNDS`; findings by severity, accepted/rejected (with the reject reason — `DESIGN.md` / intent / convention); what stayed open at the ceiling.
- **Verification** — EditMode test result honestly (e.g. `total=73 passed=71 skipped=2`), or "skipped — docs/php/asset-only".
- **Decisions / boundaries** — what hit a documented decision or convention with "needs a decision", links to WARNINGS.
- **TASK_PROGRESS.md** — `## Plan` all `[x]`, `## Changes` appended.
- Reminder: **not committed and not finalized** — finalization (the `/finalize` gate) and the commit are the user's (on `main`, branch first).

Artifacts in `$ADIR` (gitignored): `architecture.md`, `review-round*.md`, `tests.xml`, logs. Transient (overwritten by the next run) — the durable record lives in `TASK_PROGRESS.md` and `KnowledgeBase/`.

---

## Context preamble (in EVERY agent prompt)

A subagent starts with a clean context — `CLAUDE.md` is injected automatically, but you set the scope and the return shape. Prepend to every prompt:

```
You are a <role> in an orchestrator run for the task: "<Goal in one line>". Branch <BRANCH>.
The project rules (CLAUDE.md) are already in your context — follow them: a documented decision in DESIGN.md / the codebase convention, error isolation (the public API never throws into game code), no placeholders/stubs, and no commits. Before editing .cs, check KnowledgeBase/BehaviourRules/CODING_STANDARDS.md.
Scope: only <part/files>, not the whole repo.
Return strictly: <exactly what to return — as a structure>. Your final text IS the result for the orchestrator.
```

For read-only roles (Architect, Reviewer) add: "Analyse only — do NOT edit files, do NOT run mutating commands."
