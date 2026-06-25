---
description: Implement a task per CLAUDE.md, then run a cross-vendor critic loop (GPT via Codex -> Gemini 3.1 Pro via Antigravity) on the fresh implementation — generator + critic in one pass; fixes and the EditMode-test gate after each reviewer. Does not commit.
argument-hint: "<task description> (what to implement; required)"
allowed-tools: Bash, Read, Edit, Write, Grep, Glob
---

# /implement-review-gpt-then-gemini — generator + critic in one pass

You run a **fully autonomous** "implementer + critic" pipeline:

1. **Implementer — you** (this session). You implement the task under CLAUDE.md discipline (plan -> bottom-up code), and drive the verification gate green (the headless Unity EditMode test run).
2. **Critic — a cross-vendor cascade GPT (Codex) -> Gemini 3.1 Pro (Antigravity)**, run **on your fresh implementation**: GPT reviews -> you fix -> Gemini re-checks GPT's fixes second and hunts for what was missed -> you fix.

Why this shape: different model families have different blind spots, and a *fresh critic* on just-written code catches what the author has gone blind to. The implementer stays **you** (not a delegated agent) — so all code is written under the project's hard rules (see `CLAUDE.md` + `KnowledgeBase/BehaviourRules/CODING_STANDARDS.md`), with no discipline drift.

**No commits at any step** (CLAUDE.md Commits rule — commit or push only when the user explicitly asks; on `main`, branch first). The whole implementation stays uncommitted in the working tree — the critic reviews exactly that.

The command argument (`$ARGUMENTS`) is **required**: what to implement. It may be a free-form description, a `W-NNN` reference (from `KnowledgeBase/Documentation/WARNINGS.md`), or the task's stated intent in `TASK_PROGRESS.md`.

> **Timeout & compaction:** the review passes take several minutes each — every external-CLI run (`codex` / `agy`) needs a **large Bash timeout (up to `600000` ms)**, and the Bash tool will usually **auto-background** the run; when it does, wait for the background-completion notification, don't poll. Context may compact across that wait. Your anchor is `TASK_PROGRESS.md` (survives compaction): alongside its `## Plan`, keep a coarse phase tracker `## Checkpoint (implement-review)` (created in Step 1) — which of Steps 0–5 you are on. **Update it on EVERY transition.** After a compaction, read it first and resume from the right phase, do not re-implement finished work.

---

## Step 0 — Preflight: critic available, task clear, tree ready

Order matters: **first confirm the critic will run**, then spend time implementing.

1. **Critic CLIs available** (fail-fast — no point implementing if the review can't run afterward):

   ```bash
   command -v codex >/dev/null 2>&1 || { echo "FATAL: codex (Codex CLI) not on PATH — stop and tell the user"; exit 1; }; echo "codex OK"
   command -v agy   >/dev/null 2>&1 || { echo "FATAL: agy (Antigravity CLI) not on PATH — stop and tell the user"; exit 1; }; echo "agy OK"
   agy models 2>/dev/null | grep -qx "Gemini 3.1 Pro (High)" \
     || { echo "FATAL: 'Gemini 3.1 Pro (High)' missing from \`agy models\` — likely not logged into Antigravity. Stop and ask the user to log in."; exit 1; }
   echo "agy model OK"
   ```

   The block stops at the first missing CLI (non-zero exit + `FATAL:`) — tell the user and **stop**, do not start implementing.

2. **Task defined.** `$ARGUMENTS` empty → stop and ask for a task description (nothing to implement). Otherwise resolve the source:
   - `W-NNN` → read the entry in `KnowledgeBase/Documentation/WARNINGS.md` (trigger / context / effort).
   - A phase or item named in `TASK_PROGRESS.md` → read its plan and intended outcome.
   - Free text → that is the spec.

3. **CLAUDE.md boundary.** If the task conflicts with a documented deliberate decision in `DESIGN.md` (async batch-delivery semantics, drop policy, at-least-once + idempotency, `HttpClient` vs `UnityWebRequest`, the DI seams), with the task's stated intent in `TASK_PROGRESS.md`, or with an established codebase convention, or it is ambiguous on architecture / API shape / data contract / naming — **do not implement silently**: stop and ask the user a clarifying question (per the Ambiguity rule). Autopilot does not override this.

4. **Branch.** Check the current branch: `git rev-parse --abbrev-ref HEAD`. On `main` — propose a working branch `dev/<short-desc>` (`git switch -c dev/<…>`; no commit — the Commits rule allows branching, only `git commit/add` is forbidden). If the user declines the branch, continue on the current one (still uncommitted), but note it in the final report.

5. **Leftover progress.** If `TASK_PROGRESS.md` carries an unrelated in-flight task, resolve it per `KnowledgeBase/BehaviourRules/TASK_PROGRESS_RULES.md` **before** the first code edit (don't silently clobber someone else's context) — `TASK_PROGRESS.md` is a committed project doc, never delete or recreate it.

---

## Step 1 — Plan before code

1. Create/update `TASK_PROGRESS.md` for this task (plan-before-coding, `KnowledgeBase/BehaviourRules/TASK_PROGRESS_RULES.md`).
   Add a `## Checkpoint (implement-review)` section there — a coarse phase tracker (update on every transition; read it first after a compaction):

   ```markdown
   ## Checkpoint (implement-review)

   - [x] Step 0 — preflight (codex/agy OK, branch <branch>)
   - [~] Step 1 — plan before code
   - [ ] Step 2 — implementation (details — ## Plan)
   - [ ] Step 3 — green gate (EditMode tests)
   - [ ] Step 4 — critic loop GPT (Codex) -> Gemini 3.1 Pro
   - [ ] Step 5 — final report
   ```

   Legend: `[x]` done · `[~]` in progress · `[ ]` not started. The coarse "which phase" lives here; "which step inside" lives in `## Plan`.

2. Record the plan: which runtime types/seams, which tests, which risks.
3. Explicit edge-case walk (Edge cases rule). The public API is tiny (`ITracker.SendMessage` / `SendMapAsync`); enumerate the failure modes the new path can hit and confirm each is handled (or surface it):
   - null/empty inputs (`SendMessage(null)`, empty map, `Result == null`);
   - **thread-safety** (the bounded queue is hit from many threads while the background worker drains);
   - **`TaskCompletionSource` completed on EVERY path** (delivered / retries-exhausted / evicted / rejected / purged / cancelled — a never-completed `Task` is a hang);
   - cancellation + `Dispose` (worker stops clean, `HttpClient` / `SemaphoreSlim` released);
   - offline hold → flush; overflow policy (`DropOldest` vs `RejectNew`); at-least-once + idempotency (stable event id reused on retries);
   - corrupt/missing persisted file; `JsonUtility` payload round-trip (type-tagged encoding for `Dictionary<string,object>`).

> Plan recorded → mark `[x] Step 1`, `[~] Step 2` in `## Checkpoint (implement-review)`.

---

## Step 2 — Implement bottom-up

Implement the task strictly per `CLAUDE.md` + `KnowledgeBase/BehaviourRules/CODING_STANDARDS.md`:

- No placeholders/stubs — all code complete and compiling; match the surrounding code's style.
- Coding standards (hot rules): `m_camelCase` private fields, `DmytroUdovychenko.Tracking` namespaces, `UPPER_SNAKE_CASE` constants, explicit enum indices, `sealed` concrete classes, Allman braces, **no `var`** (explicit types), DI via optional constructor params with production-default fallbacks, **error isolation** (the public API never throws into game code), log through `ITrackingLogger` (never `Debug.*` in runtime SDK code), `ConfigureAwait(false)` on library awaits, `TryGet`+`out` over returning `null`, tunables on `TrackingConfig` (no magic values).
- Tests for the concurrency core must stay **deterministic**: no real network/disk/wall-clock in the default suite — use the DI seams/fakes (`ITransport`, `IEventStore`, `IClock`, `IDelayer`, `IConnectivity`) and the virtual clock; live-network tests are `[Category("Live")]`.
- SDK source: `tracking-sdk/Packages/com.dmytroudovychenko.tracking/Runtime`; tests: `…/Tests/Editor`; demo: `…/Assets/TrackingDemo`.

Along the way: if you find a pre-existing problem in code you're touching, report it (Analyze before editing / Fix and report rules) — don't fix it silently outside scope. Genuinely deferrable issues → `KnowledgeBase/Documentation/WARNINGS.md`.

> Implementation done → mark `[x] Step 2`, `[~] Step 3` in `## Checkpoint (implement-review)`.

---

## Step 3 — Green gate before the critic (headless Unity EditMode tests)

Run the verification gate before calling the reviewer (the implementation must be green — the critic catches logic, not a broken build). **Skip for docs/PHP/asset-only diffs; only run when `.cs` / `.asmdef` / `.asmref` changed.** The **Unity Editor must be closed** (single-instance project lock). Large timeout (batchmode startup + tests take a few minutes).

```bash
RDIR=".self-review/$(git rev-parse --abbrev-ref HEAD | tr '/' '-')/implement-gpt-then-gemini"
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
  rm -f "$RDIR/tests.xml" "$RDIR/tests.log"
  "$UNITY" -runTests -batchmode -nographics -projectPath "$PROJ" -testPlatform EditMode -testResults "$RDIR/tests.xml" -logFile "$RDIR/tests.log"
  echo "unity exit=$?"; grep -m1 -o '<test-run[^>]*>' "$RDIR/tests.xml" 2>/dev/null || { grep -iE 'error CS[0-9]+|Compilation failed' "$RDIR/tests.log" | head; tail -15 "$RDIR/tests.log"; }
else
  echo "SKIP: no .cs/.asmdef/.asmref changed — tests not needed (docs/php/asset-only)."
fi
```

Green = `failed="0"` with a non-zero `passed` in the `<test-run>` line (the suite is currently **145 deterministic tests + 2 `[Category("Live")]` live tests** that run in the default headless suite and need network). Any failure or compile error → fix to green. Report failures honestly (Fix and report rule) — never silently fix.

> Gate green (implementation ready for the critic) → mark `[x] Step 3`, `[~] Step 4` in `## Checkpoint (implement-review)`.

---

## Step 4 — Critic loop: GPT (Codex) -> Gemini 3.1 Pro (Antigravity)

Your fresh implementation is now uncommitted in the working tree. Run the cross-vendor critic loop on it. Both reviewers are **strictly read-only**; neither may edit the tree.

### 4.0 — Setup + read-only fingerprint

```bash
cd "$(git rev-parse --show-toplevel)"
RDIR=".self-review/$(git rev-parse --abbrev-ref HEAD | tr '/' '-')/implement-gpt-then-gemini"
mkdir -p "$RDIR"
[ -f .self-review/.gitignore ] || printf '*\n' > .self-review/.gitignore
BRANCH=$(git rev-parse --abbrev-ref HEAD)

# Working-tree fingerprint — agy has no hard read-only mode, so we diff this before/after every agy run.
fp() { { git --no-pager diff HEAD; git status --porcelain; } | shasum | cut -d' ' -f1; }
printf 'BRANCH=%q\n' "$BRANCH" > "$RDIR/scope.env"

# Context = your just-written (uncommitted) implementation. Surface untracked files via intent-to-add, then restore.
git add -A -N >/dev/null 2>&1
{
  echo "# Implement-review context (uncommitted working tree vs HEAD)"
  echo "- branch: $BRANCH"; echo
  echo "## TASK_PROGRESS.md (intent / plan / what was done)"
  if [ -f TASK_PROGRESS.md ]; then echo '```markdown'; cat TASK_PROGRESS.md; echo '```'; else echo "_(none)_"; fi
  echo; echo "## git status --short"; echo '```'; git --no-pager status --short; echo '```'
  echo; echo "## Uncommitted diff (vs HEAD, incl. new files)"; echo '```diff'; git --no-pager diff HEAD; echo '```'
} > "$RDIR/context.md"
git reset -q >/dev/null 2>&1
echo "context bytes: $(wc -c < "$RDIR/context.md")  fingerprint: $(fp)"
```

> **Large/noisy tree — scope the critic's context.** If the diff carries a large mechanical chunk (mass reformat, generated code), a multi-MB context can exceed the reviewer's limit (Gemini's tool ceiling is ~10 min). Filter the diff in `context.md` (`git --no-pager diff HEAD -- '.' ':(exclude)<noisy-path>'`) so the critic gets the meaningful change, not the mechanical noise. `codex` tends to handle large contexts; `agy` is the one that times out.

The shared SDK risk-area list both reviewers are told to focus on:

> bugs/logic errors; **CONCURRENCY** (the bounded queue is hit from any thread and drained by a background worker — lock correctness, no torn state, no unobserved `Task` exceptions, `ConfigureAwait(false)` on library awaits, `TaskCompletionSource` completed on EVERY path: delivered / retries-exhausted / evicted / rejected / purged / cancelled, no deadlock from blocking on async, no main-thread assumption on the worker — `UnityWebRequest` is main-thread-only, `HttpClient` is deliberate); **ASYNC + cancellation/`Dispose`** (token honored, worker stops clean, `HttpClient` / `SemaphoreSlim` disposed); **ERROR ISOLATION** (the public API never throws into game code — validate, swallow+log via the logger seam); **TEST DETERMINISM** (no real network/disk/wall-clock in the default suite — DI seams/fakes `ITransport` / `IEventStore` / `IClock` / `IDelayer` / `IConnectivity` + a virtual clock; live tests are `[Category("Live")]`); **SERIALIZATION** (`JsonUtility` can't do `Dictionary<string,object>` → the type-tagged payload encoding; round-trip + corrupt/missing-file resilience); at-least-once + idempotency (stable event id reused on retries); bounded-memory drop policy (`DropOldest` vs `RejectNew`); config defaults on `TrackingConfig`; public API surface vs docs/CHANGELOG; naming; dead code; concise comments.

### 4.1 — GPT review (Codex, read-only)

Codex's `read-only` sandbox means the reviewer physically cannot edit. **Large timeout** (auto-backgrounds).

```bash
cd "$(git rev-parse --show-toplevel)"; source "$RDIR/scope.env" 2>/dev/null
RDIR=".self-review/$(git rev-parse --abbrev-ref HEAD | tr '/' '-')/implement-gpt-then-gemini"
PROMPT="You are a senior C#/Unity SDK reviewer with clean context, reviewing ONLY the uncommitted changes of branch $BRANCH. The diff + task log are in $RDIR/context.md (read it). FIRST read the '## TASK_PROGRESS.md' section of context.md plus DESIGN.md and README.md; judge the diff against that stated intent. SCOPE (strict): review ONLY the lines in the uncommitted diff and their immediate context; pre-existing issues in untouched code go under '## Out-of-scope', never in Findings. This is a production-grade in-process event-tracking SDK for Unity (public API -> bounded thread-safe queue -> background batching dispatcher -> pluggable transport; retries/backoff, persistence, lifecycle flush, connectivity, circuit breaker, dead-letter, metrics, logging hook, privacy opt-out). Look hard at: bugs/logic errors; CONCURRENCY (queue hit from any thread, drained by a background worker — lock correctness, no torn state, no unobserved Task exceptions, ConfigureAwait(false) on library awaits, TaskCompletionSource completed on EVERY path: delivered/retries-exhausted/evicted/rejected/purged/cancelled, no deadlock from blocking on async, no main-thread assumption on the worker — UnityWebRequest is main-thread-only, HttpClient is deliberate); ASYNC + cancellation/Dispose (token honored, worker stops clean, HttpClient/SemaphoreSlim disposed); ERROR ISOLATION (public API never throws into game code — validate, swallow+log via the logger seam); TEST DETERMINISM (no real network/disk/wall-clock in the default suite — DI seams/fakes ITransport/IEventStore/IClock/IDelayer/IConnectivity + a virtual clock; live tests are in the Live category); SERIALIZATION (JsonUtility can't do Dictionary<string,object> -> type-tagged payload encoding; round-trip + corrupt/missing-file resilience); at-least-once + idempotency; bounded-memory drop policy (DropOldest vs RejectNew); config defaults on TrackingConfig; public API surface vs docs/CHANGELOG; naming; dead code; concise comments. Report format (strict): '## What & why' (2-4 sentences grounded in TASK_PROGRESS.md); '## Findings' — each '[SEV] file:line — problem. Fix: concrete suggestion.' SEV in {BLOCKER, MAJOR, MINOR, NIT}, IN-SCOPE only, file:line mandatory; '## Out-of-scope (pre-existing)' (optional); '## Good'. You are read-only — do NOT edit files or run mutating commands. Your final text IS the report."
# < /dev/null is MANDATORY: codex reads stdin even with a prompt arg and hangs on EOF without it.
codex exec -s read-only -C "$(pwd)" -o "$RDIR/gpt-review.md" "$PROMPT" < /dev/null > "$RDIR/gpt.log" 2>&1; EXIT=$?
echo "codex exit=$EXIT — report: $RDIR/gpt-review.md"; tail -5 "$RDIR/gpt.log"
```

When it finishes, **read** `$RDIR/gpt-review.md`. Empty or non-zero exit → check `gpt.log`, fix the invocation, retry.

### 4.2 — Triage + fix GPT's findings, then re-gate

1. **Do not apply findings blindly.** Accept/reject each: reject anything contradicting a documented deliberate decision (`DESIGN.md`), the task's stated intent (`TASK_PROGRESS.md`), or established conventions — record the reason. `## Out-of-scope` items are **not fixed here**: verify each is real, surface it for the user. Accept the rest by priority BLOCKER → MAJOR → MINOR → NIT.
2. Apply edits matching the surrounding style; no placeholders. Log decisions (accepted / rejected+reason / changed file:line) in `$RDIR/gpt-fixes.md`.
3. **Re-run the Step 3 verification gate** to green.

### 4.3 — Gemini re-check (Antigravity, read-only via fingerprint)

`agy` has **no hard read-only mode**, so it is guarded three ways: the Step-0 availability gate (already passed), an edit-forbidding prompt, and a before/after working-tree fingerprint. Capture the fingerprint **before** the run; verify it **after**. **Large timeout** (`--print-timeout 540s`; auto-backgrounds).

```bash
cd "$(git rev-parse --show-toplevel)"; source "$RDIR/scope.env" 2>/dev/null
RDIR=".self-review/$(git rev-parse --abbrev-ref HEAD | tr '/' '-')/implement-gpt-then-gemini"
fp() { { git --no-pager diff HEAD; git status --porcelain; } | shasum | cut -d' ' -f1; }
FP_BEFORE=$(fp); echo "fp before: $FP_BEFORE"
PROMPT="You are a senior C#/Unity SDK reviewer with clean context. GPT already reviewed this branch and the developer applied fixes; you are the SECOND reviewer. Read $RDIR/context.md (the uncommitted diff + task log of branch $BRANCH), GPT's report $RDIR/gpt-review.md, and the developer's change summary $RDIR/gpt-fixes.md, then assess the CURRENT working tree via git status/diff. FIRST read the '## TASK_PROGRESS.md' section of context.md plus DESIGN.md and README.md. SCOPE (strict): only the lines in the uncommitted diff and their immediate context; pre-existing issues go under '## Out-of-scope'. This is a production-grade in-process event-tracking SDK for Unity. Verify GPT's fixes are correct and complete (flag any incomplete fix or regression), AND hunt for what GPT missed, focusing on: bugs/logic errors; CONCURRENCY (queue hit from any thread, drained by a background worker — lock correctness, no torn state, no unobserved Task exceptions, ConfigureAwait(false) on library awaits, TaskCompletionSource completed on EVERY path: delivered/retries-exhausted/evicted/rejected/purged/cancelled, no deadlock, no main-thread assumption on the worker — UnityWebRequest is main-thread-only, HttpClient is deliberate); ASYNC + cancellation/Dispose (HttpClient/SemaphoreSlim disposed); ERROR ISOLATION (public API never throws into game code); TEST DETERMINISM (no real network/disk/wall-clock in the default suite — DI seams/fakes + virtual clock; live tests in the Live category); SERIALIZATION (JsonUtility type-tagged payload encoding; round-trip + corrupt/missing-file resilience); at-least-once + idempotency; bounded-memory drop policy; config defaults on TrackingConfig; public API surface vs docs/CHANGELOG; naming; dead code; concise comments. Report format (strict): '## GPT fixes verdict' — each fix confirmed / incomplete / regression (file:line); '## New findings' — '[SEV] file:line — problem. Fix: suggestion.' SEV in {BLOCKER, MAJOR, MINOR, NIT}, in-scope only; '## Out-of-scope (pre-existing)' (optional); '## Good'. You are READ-ONLY: do NOT edit any file, do NOT run mutating commands, do NOT write anything to the working tree. Your final text IS the report."
agy --model "Gemini 3.1 Pro (High)" --sandbox --print-timeout 540s --print "$PROMPT" > "$RDIR/gemini-review.md" 2> "$RDIR/gemini.log" < /dev/null
echo "agy exit=$? — report: $RDIR/gemini-review.md"; tail -5 "$RDIR/gemini.log"
FP_AFTER=$(fp); echo "fp after: $FP_AFTER"
if [ "$FP_BEFORE" != "$FP_AFTER" ]; then
  echo "HALT: agy modified the working tree (fingerprint changed) despite read-only instructions. Inspect 'git status' / 'git diff', revert any reviewer edits, and report this to the user before continuing."
fi
```

**Read** `$RDIR/gemini-review.md`. If the fingerprint changed → **halt the autopilot**, inspect the tree, revert any reviewer edits, and report it (the reviewer must not write code).

### 4.4 — Triage + fix Gemini's findings, then re-gate

Same triage as 4.2 (documented decisions / task intent win): close every confirmed `incomplete`/`regression` from the GPT-fixes verdict, accept valid `## New findings` by severity, verify+surface `## Out-of-scope`. Log decisions in `$RDIR/gemini-fixes.md`. **Re-run the Step 3 verification gate** to green.

> Critic loop done (both passes + fixes + green gate) → mark `[x] Step 4`, `[~] Step 5` in `## Checkpoint (implement-review)`.

---

## Step 5 — Final report to the user

Print an `## Implement + critique summary (GPT [Codex] -> Gemini 3.1 Pro)` section:

- **Task:** what you implemented (source: W-NNN / TASK_PROGRESS.md item / free text) + branch / base / diff size.
- **Implementation:** what was built bottom-up (key `file:line` — runtime, dispatcher, transport, tests); which tests were added.
- **GPT pass:** findings by severity; accepted / rejected (short reason for rejects); what changed (`file:line`).
- **Gemini pass:** verdict on GPT's fixes (confirmed / returned as incomplete / regression) + new findings by severity; accepted / rejected; what changed. Note whether the read-only fingerprint stayed intact.
- **Verification:** the EditMode test result after implementation and after each review pass (e.g. `total=73 passed=71 skipped=2`) — honest; or "skipped — docs/php/asset-only". Report failures honestly.
- **Deferred:** links to `KnowledgeBase/Documentation/WARNINGS.md` (W-NNN) if anything was genuinely deferred.
- **Reminder:** **nothing committed** — the commit is the user's (on `main`, branch first). Finalization (`/finalize`) is the user's call, not the last step of this pipeline.

Update `TASK_PROGRESS.md` to reflect the final state (it is a committed project doc — do not delete it). Artifacts stay in `$RDIR/` (gitignored): `context.md`, `gpt-review.md`, `gpt-fixes.md`, `gemini-review.md`, `gemini-fixes.md`, `tests.xml`, logs.
