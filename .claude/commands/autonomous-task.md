---
description: Run a task in autonomous mode — given a goal and the desired final result, drive it to functional completion, resolving significant forks by auto-polling a multi-model panel (not by asking the user), with continuous verification. Stops at "done"; finalization (`/finalize`) is the user's to run.
argument-hint: '<what to do + desired final result / definition of done>'
allowed-tools: Bash, Read, Edit, Write, Grep, Glob
---

# /autonomous-task — drive a task to completion autonomously

You drive the task **fully autonomously** to functional completion. The user gave you what to do and the desired final result — you get there yourself without blocking on questions: resolve **significant** forks by auto-polling a small multi-model panel of fresh external CLIs; resolve **small** ones yourself with a one-line note. Verify continuously.

This command **orchestrates**; the actual rules live in the root `CLAUDE.md` (Work Rules) and `KnowledgeBase/BehaviourRules/`. It is the **entry** into autonomous mode — from here on, by default, a fork goes to the panel, not to the user.

**Boundaries (absolute — autonomy does NOT lift them):**

- **Commits / push — never** without the user explicitly asking in this turn. On `main`, branch first. This command drives to "done" but **does not commit and does not finalize** — it reports and leaves the tree for the user.
- **Finalization is not part of this command.** Stop at functionally-done + verified, report; the user runs `/finalize` ([FINALIZATION_RULES.md](../../KnowledgeBase/BehaviourRules/FINALIZATION_RULES.md), with its own "ready to finalize?" gate).
- **Behind the design gate** — when a fork would change a documented deliberate decision in **`DESIGN.md`** (async batch-delivery semantics, drop policy, at-least-once + idempotency, `HttpClient` vs `UnityWebRequest`, the DI seams), the task's stated intent in **`TASK_PROGRESS.md`**, or an established codebase convention, the panel may advise but you do **not** change the locked decision autonomously: record the recommendation + "needs a decision" and proceed on the unblocked work.

The argument (`$ARGUMENTS`) is the task description + desired final result. Empty → that is not a fork, it is a missing spec: ask in one line for the task and the definition of done, then **stop** (no panel — there is nothing to consult about).

> **Timeout:** the run is long, with several panel polls (each poll spawns external CLIs that take minutes — the Bash tool will usually auto-background them; set a large timeout, up to `600000` ms). Drive the task in this same turn until done; on a long loop lean on `TASK_PROGRESS.md` (it survives compaction) — `## Plan` with `[x]` and `## Decisions (panel)` let a re-run resume from where it left off.

---

## Step 0 — Activate autonomous mode, fix the goal

1. This command = the explicit trigger for autonomous mode. From here on, by default: a fork → panel, not the user.
2. Parse `$ARGUMENTS` into a **Goal** (what we do) and a **Final result / DoD** (the completion criterion — how you will know the task is closed). Empty → see above (ask for the spec and stop).
3. If `$ARGUMENTS` references a section of `TASK_PROGRESS.md` (incl. its plan appendix) or a doc, read it and the relevant part of `DESIGN.md` / `README.md`. No reference → don't block: the stated final result is the spec.

---

## Step 1 — Start: resolve leftover + Plan + mode marker

1. **Resolve leftover — the first step before any code edit:** if the `TASK_PROGRESS.md` carries unfinished work from another task, resolve it (continue / restart) per [TASK_PROGRESS_RULES.md](../../KnowledgeBase/BehaviourRules/TASK_PROGRESS_RULES.md). In parallel, glance at [WARNINGS.md](../../KnowledgeBase/Documentation/WARNINGS.md) for a closable item you can knock out in passing.
   - A partial leftover (uncommitted work from another task) is about work already done — the panel can't adjudicate it: ask one setup question (continue / restart) and proceed. This is setup, not a technical fork.
2. Add a marker as the first line under the `TASK_PROGRESS.md` header (format — [TASK_PROGRESS_RULES.md](../../KnowledgeBase/BehaviourRules/TASK_PROGRESS_RULES.md)):
   ```markdown
   **Mode:** autonomous — forks → multi-model panel (Claude / Codex / Gemini), not the user. End: stop at "done"; finalization (/finalize) is the user's.
   ```
3. Record `## Goal / Context` (Goal + Final result/DoD from Step 0) and `## Plan` — concrete steps bottom-up (event model → queue/dispatcher → transport → demo/docs), each independently verifiable.

---

## Step 2 — Autonomous run (loop until done)

Work down `## Plan`, implementing under the `CLAUDE.md` discipline: coding standards ([CODING_STANDARDS.md](../../KnowledgeBase/BehaviourRules/CODING_STANDARDS.md) — `m_camelCase` private fields, `DmytroUdovychenko.Tracking` namespaces, `UPPER_SNAKE_CASE` constants, explicit enum indices, `sealed` classes, Allman braces, no `var`, DI via optional ctor params, error isolation, log via `ITrackingLogger` never `Debug.*` in runtime code, `ConfigureAwait(false)`, `TryGet`+`out`); the **Edge cases** Work Rule (walk below); no placeholders/stubs (match the surrounding code's style). At each step:

### 2a. A fork? Decide who decides

**Poll the panel** (Step 2e) if the fork is **significant** — any of:

- architecture / data contract / API shape / serialization format;
- costly-to-reverse (rollback needs a migration of the persisted format or a wide refactor);
- you would normally stop and ask the user;
- you are "stuck" (two approaches in a row missed, or > 30 min on an opaque error).

**Decide yourself** (with a one-line note in `TASK_PROGRESS.md`, no panel) if the fork is small:

- cosmetic / phrasing / naming with an obvious convention;
- clearly reversible and low-impact;
- already answered in `CLAUDE.md` / `CODING_STANDARDS.md` or by an existing pattern in the code.

After the panel: synthesize (best / hybrid, triaged against the design-gate decisions in `DESIGN.md`) → record a block under `## Decisions (panel)` → proceed on the decision.

### 2b. Respect the boundaries

- The decision would change a `DESIGN.md` decision / the task intent in `TASK_PROGRESS.md` / an established convention → **do not implement it**: write the recommendation + "needs a decision" into `TASK_PROGRESS.md` and [WARNINGS.md](../../KnowledgeBase/Documentation/WARNINGS.md), do the rest.
- A purely product/taste question (what the user would prefer) → take a reasonable default, write it as an assumption-to-confirm, move on.
- A request/finding that contradicts a `CLAUDE.md` golden rule (no commit without ask, error isolation, etc.) → surface to the user, not to the panel.

### 2c. Verify as you go

Don't accumulate unverified work. After each meaningful chunk, run the verification gate (Step 2d). Mark `[x]` in `## Plan`, append to `## Changes`.

**The loop runs until:** every `## Plan` step is done AND the Final result / DoD is reached AND verification is green.

### 2d. Verification gate — headless Unity EditMode test run

Run only when `.cs`/`.asmdef`/`.asmref` changed (skip for docs/PHP/asset-only diffs); the Unity Editor must be **closed** (single-instance lock); large timeout. Results go under `ADIR` (Step 2e):

```bash
ROOT="$(git rev-parse --show-toplevel)"; PROJ="$ROOT/tracking-sdk"
ADIR="$ROOT/.claude/artifacts/autonomous-task"; mkdir -p "$ADIR"
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
Green = `failed="0"` with a non-zero `passed` (the suite is currently **145 deterministic tests + 2 `[Category("Live")]`** live tests that run in the default headless suite and need network). Fix to green before continuing.

**Edge-case walk** (apply at each chunk; the public API is tiny — `ITracker.SendMessage` / `SendMapAsync`): `SendMessage(null)` / empty map / `Result == null`; **concurrency** (the bounded queue is hit from any thread while the background worker drains — lock correctness, no torn state, no unobserved `Task` exceptions, `ConfigureAwait(false)` on library awaits, `TaskCompletionSource` completed on **every** path: delivered / retries-exhausted / evicted / rejected / purged / cancelled, no deadlock from blocking on async, no main-thread assumption on the worker — `UnityWebRequest` is main-thread-only, `HttpClient` is deliberate); async + cancellation/`Dispose` (token honored, worker stops clean, `HttpClient`/`SemaphoreSlim` disposed); **error isolation** (the public API never throws into game code — validate, swallow+log via the logger seam); **test determinism** (no real network/disk/wall-clock in the default suite — DI seams/fakes `ITransport`/`IEventStore`/`IClock`/`IDelayer`/`IConnectivity` + a virtual clock; live tests `[Category("Live")]`); **serialization** (`JsonUtility` can't do `Dictionary<string,object>` → the type-tagged payload encoding; round-trip + corrupt/missing-file resilience); at-least-once + idempotency (stable event id reused on retries); bounded-memory drop policy (`DropOldest` vs `RejectNew`); config defaults on `TrackingConfig`. Confirm each is handled (or surface it to `TASK_PROGRESS.md` / `WARNINGS.md`).

### 2e. The multi-model panel (auto-resolve a significant fork)

Auto-poll fresh external CLIs read-only instead of asking the user. Artifacts under `ADIR`:

```bash
ROOT="$(git rev-parse --show-toplevel)"; cd "$ROOT"
ADIR=".claude/artifacts/autonomous-task"; mkdir -p "$ADIR"
[ -f .claude/artifacts/.gitignore ] || printf '*\n' > .claude/artifacts/.gitignore
```

Write the fork as a self-contained prompt to `$ADIR/fork.md` (the decision, the options you see, the constraints from `DESIGN.md` / `CODING_STANDARDS.md`, and the ask: "recommend one option with a one-paragraph rationale; read-only, do not edit"). Then poll whichever of these are installed — two agreeing answers is enough; one is acceptable if only one is available:

- **Codex / GPT** (read-only sandbox = the reviewer physically cannot edit; codex 0.135 reads stdin even with a prompt arg and hangs on EOF, so `< /dev/null` is MANDATORY):
  ```bash
  PROMPT="$(cat "$ADIR/fork.md")"
  codex exec -s read-only -C "$(pwd)" -o "$ADIR/panel-codex.md" "$PROMPT" < /dev/null > "$ADIR/panel-codex.log" 2>&1; echo "codex exit=$?"
  ```
- **Gemini 3.1 Pro (High)** via Antigravity (`agy` has no hard read-only mode — guard it): availability check first, a prompt that forbids edits, and a before/after working-tree fingerprint that halts and reports if the reviewer touched the tree:
  ```bash
  command -v agy >/dev/null && agy models 2>/dev/null | grep -qx "Gemini 3.1 Pro (High)" || { echo "skip: agy / Gemini model unavailable"; }
  fp() { { git --no-pager diff HEAD; git status --porcelain; } | shasum | cut -d' ' -f1; }
  BEFORE=$(fp)
  agy --model "Gemini 3.1 Pro (High)" --sandbox --print-timeout 540s --print "$(cat "$ADIR/fork.md")" > "$ADIR/panel-gemini.md" 2> "$ADIR/panel-gemini.log" < /dev/null
  [ "$(fp)" = "$BEFORE" ] || { echo "HALT: Gemini modified the working tree — stop and report."; }
  ```
- **Claude (fresh process)** — resolve the binary robustly, run read-only at max effort:
  ```bash
  CLAUDE_BIN="$(command -v claude 2>/dev/null || true)"
  if [ -z "$CLAUDE_BIN" ]; then
    for cand in "$HOME/.local/bin/claude" "$HOME/.claude/local/claude" /opt/homebrew/bin/claude /usr/local/bin/claude "$(npm config get prefix 2>/dev/null)/bin/claude"; do
      [ -n "$cand" ] && [ -f "$cand" ] && { CLAUDE_BIN="$cand"; break; }
    done
  fi
  [ -z "$CLAUDE_BIN" ] && { echo "ERROR: 'claude' CLI not found. Install: npm i -g @anthropic-ai/claude-code"; exit 1; }
  "$CLAUDE_BIN" -p --model claude-opus-4-8 --effort max --permission-mode plan "$(cat "$ADIR/fork.md")" > "$ADIR/panel-claude.md" 2> "$ADIR/panel-claude.log" < /dev/null; echo "claude exit=$?"
  ```

**Read** each panel file, then synthesize per Step 2a (best / hybrid, triaged against the design gate) and record under `## Decisions (panel)`.

### 2f. Fallback — panel unavailable

If none of the three CLIs are installed/reachable:

- cheap/reversible fork → safe default + a recorded reason, continue;
- costly-to-reverse fork with no safe default → **stop** that branch and put the question to the user. This is the one case where a technical fork returns to the user.

---

## Step 3 — Stop at "done" (do NOT finalize, do NOT commit)

All plan steps closed, Final result reached, verification green:

- **Stop.** Do **not** run finalization (`/finalize`) or commit — those are the user's.
- Anything left behind the design gate or as an assumption is already in `TASK_PROGRESS.md` / `WARNINGS.md`.

---

## Step 4 — Final report to the user

Section `## Autonomous-task summary`:

- **Goal / Final result** — 1-2 lines, and whether the DoD was met.
- **Done** — the key changes (`ClassName.Method`, `file:line`), `## Plan` all `[x]`.
- **Forks** — what went to the panel and the decisions taken (briefly, linked to `## Decisions (panel)`); what you decided yourself as small.
- **Assumptions / defaults** — product/taste calls awaiting the user's confirmation.
- **Behind the gate** — what was deferred behind a `DESIGN.md` / convention decision, with `WARNINGS.md` links.
- **Verification** — the EditMode test result (`failed="0"`, passed count) or the SKIP reason — reported honestly.
- **Ready to finalize.** Reminder: **nothing committed, nothing finalized** — finalization (`/finalize`, with its "ready?" gate) and the commit are the user's.
