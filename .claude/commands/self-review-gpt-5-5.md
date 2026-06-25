---
description: DEEP GPT review of the current branch (whole project diff base..HEAD + uncommitted) by an external GPT agent (GPT-5.5 via the Codex CLI, clean context, read-only), then autopilot the review -> fix -> re-review -> verify loop. Verification = the headless Unity EditMode test run.
argument-hint: "[base-ref] (optional; defaults to the repo's initial commit — i.e. review the whole SDK)"
allowed-tools: Bash, Read, Edit, Write, Grep, Glob
---

# /self-review-gpt-5-5 — DEEP GPT review by an external agent (Codex CLI)

You run a **fully autonomous** GPT review loop. The external reviewer is **GPT (GPT-5.5)** in a separate process with clean context, launched via the **Codex CLI** (`codex exec -s read-only`). You are the developer: you receive the review, fix, ask for a re-check, verify, and finish.

Why an external GPT and not a subagent: clean context (no attachment to the code you just wrote) + a different model family — a genuine fresh pair of eyes from a different lineage than yours.

**Depth = DEEP.** Reviews the whole project diff (`$BASE...HEAD`, committed) **plus** the uncommitted working tree. Use it before submitting / opening a PR. For a quick check of only what's uncommitted, use `/self-review-uncommitted`.

**The reviewer runs strictly read-only** (`codex exec -s read-only`) — it physically cannot edit; it only reads and writes a report via `-o`. This command does not commit — it leaves the working tree resolved and reports; the commit is yours (commit or push only when the user explicitly asks; on `main`, branch first).

The argument (`$ARGUMENTS`) is an optional base-ref. Empty → the repo's **initial commit** (review the entire SDK).

> **Timeout:** a review takes several minutes. When you launch `codex exec` via Bash, set a large timeout (up to `600000` ms). The Bash tool will usually **auto-background** the long run — when it does, just wait for the background-completion `<task-notification>`; do not poll. The report file stays 0 bytes until `codex exec` finishes.
>
> **Project shape:** single git repo; git root = repo root. The Unity project is the subfolder `tracking-sdk/`; `README.md` lives at the repo root, the other docs under `KnowledgeBase/Documentation/`. Review spans the **whole repo** (no path scoping). Verification = the **headless Unity EditMode test run** (145 deterministic tests), not a bare compile check.

---

## Step 0 — Setup and scope

```bash
cd "$(git rev-parse --show-toplevel)"
RDIR=".self-review/$(git rev-parse --abbrev-ref HEAD | tr '/' '-')/gpt-5-5"
mkdir -p "$RDIR"
[ -f .self-review/.gitignore ] || printf '*\n' > .self-review/.gitignore

# Base: argument, else origin/main (if it differs from HEAD), else the repo's initial commit.
BASE="$ARGUMENTS"
if [ -z "$BASE" ]; then
  if git rev-parse --verify origin/main >/dev/null 2>&1 && ! git merge-base --is-ancestor HEAD origin/main; then
    BASE=origin/main
  else
    BASE="$(git rev-list --max-parents=0 HEAD | tail -1)"   # initial commit -> whole SDK
  fi
fi
BRANCH=$(git rev-parse --abbrev-ref HEAD)

# The reviewer is the Codex CLI (codex 0.135). Bail early if it is not installed.
command -v codex >/dev/null 2>&1 || { echo "ERROR: 'codex' CLI not found on PATH. Install the Codex CLI to run this review."; exit 1; }

printf 'BASE=%q\nBRANCH=%q\n' "$BASE" "$BRANCH" > "$RDIR/scope.env"
echo "scope=DEEP branch=$BRANCH base=$BASE codex=$(command -v codex)"
git --no-pager diff --stat "$BASE"...HEAD
git --no-pager status --short
```
(Substitute `$ARGUMENTS` literally from the command argument.)

Collect the diff context. `git add -A -N` makes any **untracked** files visible to `git diff` (intent-to-add only — no content is staged); `git reset -q` restores the original state afterward:

```bash
RDIR=".self-review/$(git rev-parse --abbrev-ref HEAD | tr '/' '-')/gpt-5-5"; source "$RDIR/scope.env"
git add -A -N >/dev/null 2>&1
{
  echo "# Self-review context (DEEP: $BASE...HEAD + working tree + untracked)"
  echo "- branch: $BRANCH"; echo "- base:   $BASE"; echo
  echo "## TASK_PROGRESS.md (intent / plan / what was done)"
  if [ -f TASK_PROGRESS.md ]; then echo '```markdown'; cat TASK_PROGRESS.md; echo '```'; else echo "_(no TASK_PROGRESS.md — infer intent from git log + diff)_"; fi
  echo; echo "## git log ($BASE..HEAD)"; echo '```'; git --no-pager log --oneline --no-decorate "$BASE"..HEAD; echo '```'
  echo; echo "## git status --short"; echo '```'; git --no-pager status --short; echo '```'
  echo; echo "## Committed diff ($BASE...HEAD)"; echo '```diff'; git --no-pager diff "$BASE"...HEAD; echo '```'
  echo; echo "## Working tree + untracked (vs HEAD)"; echo '```diff'; git --no-pager diff HEAD; echo '```'
} > "$RDIR/context.md"
git reset -q >/dev/null 2>&1
echo "context bytes: $(wc -c < "$RDIR/context.md")"
```

Check there is anything to review:
```bash
RDIR=".self-review/$(git rev-parse --abbrev-ref HEAD | tr '/' '-')/gpt-5-5"; source "$RDIR/scope.env"
if git --no-pager diff "$BASE"...HEAD --quiet && git --no-pager diff HEAD --quiet && [ -z "$(git ls-files --others --exclude-standard)" ]; then
  echo "EMPTY: nothing to review against $BASE with a clean tree."
fi
```
If `EMPTY:` — tell the user and **stop**.

---

## Step 0.5 — Read the task context first (you, before triaging)

Ground yourself before launching the reviewer — this is what lets you triage well and prevents scope drift:

1. **Read `TASK_PROGRESS.md`** — the phased build log: goal, plan, what each phase did, test counts. Your triage judges the diff against this stated intent.
2. **Read `DESIGN.md`** — the rationale for the non-obvious decisions (async batch-delivery semantics, drop policy, at-least-once + idempotency, `HttpClient` vs `UnityWebRequest`, the DI seams). A reviewer suggestion that contradicts a documented, deliberate decision is a reject.
3. **Skim `README.md`** (architecture + implicit-requirements scorecard) and the package `CHANGELOG.md`.

The reviewer is told to do the same — but *you* decide accept/reject.

---

## Step 1 — GPT reviewer does the review (round 1)

Launch GPT via the Codex CLI, read-only, with the report written to a file. **Large timeout** (the review takes several minutes):

```bash
RDIR=".self-review/$(git rev-parse --abbrev-ref HEAD | tr '/' '-')/gpt-5-5"; source "$RDIR/scope.env"
PROMPT="You are a senior C#/Unity SDK reviewer with clean context. You are doing a DEEP review of git branch $BRANCH against base $BASE plus uncommitted changes. The full diff + the task log are in $RDIR/context.md (read it).
$(cat <<'BODY'
FIRST understand the task: read the '## TASK_PROGRESS.md' section of context.md (goal, plan, what was done). Read DESIGN.md for the deliberate design decisions and README.md for the architecture. Also read CLAUDE.md and KnowledgeBase/BehaviourRules/CODING_STANDARDS.md — the project's named rules and C# conventions (m_camelCase private fields, DmytroUdovychenko.Tracking namespaces, UPPER_SNAKE_CASE constants, explicit enum indices, sealed classes, Allman braces, no var, DI via optional ctor params, error isolation, log via ITrackingLogger never Debug.* in runtime code, ConfigureAwait(false), TryGet+out). Judge the diff against that stated intent — flag anything that deviates from the plan or leaves a step unfinished.

SCOPE DISCIPLINE (strict): review ONLY the lines in the diff (context.md) and their immediate context. Confirm a symbol actually appears in the diff before claiming this work changed it. Pre-existing issues in untouched code go under "## Out-of-scope", never in Findings.

This is a production-grade in-process event-tracking SDK for Unity: public API -> bounded thread-safe queue -> background batching dispatcher -> pluggable transport, with retries/backoff, durable persistence, lifecycle flush, connectivity-awareness, circuit breaker, dead-letter, metrics, logging hook, privacy opt-out. The public API is tiny: ITracker.SendMessage / SendMapAsync. Look hard at:
  - bugs and logic errors;
  - CONCURRENCY: the bounded queue is hit from any thread and drained by a background worker. Check lock correctness, no torn state, no unobserved Task exceptions, ConfigureAwait(false) on library awaits, TaskCompletionSource completed on EVERY path (delivered / retries-exhausted / evicted / rejected / purged / cancelled), no deadlock from blocking on async, no main-thread assumption on the worker (UnityWebRequest is main-thread-only -> HttpClient is deliberate);
  - ASYNC + cancellation/Dispose: CancellationToken honored, worker stops clean, HttpClient/SemaphoreSlim disposed;
  - ERROR ISOLATION: the public API must never throw into game code (validate, swallow+log via the logger seam);
  - TEST DETERMINISM: tests must not depend on real network/disk/wall-clock in the default suite — they use DI seams/fakes (ITransport, IEventStore, IClock, IDelayer, IConnectivity) and a virtual clock; live tests must be in the Live category. Flag any test that sleeps, hits the network in the default suite, or is order-dependent/flaky;
  - SERIALIZATION: JsonUtility cannot handle Dictionary<string,object> -> the type-tagged payload encoding; check round-trip + corrupt/missing-file resilience;
  - at-least-once + idempotency (stable event id reused on retries); bounded-memory drop policy (DropOldest vs RejectNew); config defaults on TrackingConfig;
  - public API surface vs the docs/CHANGELOG; naming; dead code; comments (should be concise, <=2 lines).

Edge-case walk for the public API: SendMessage(null) / empty map / Result == null; thread-safety; TCS completed on every path; cancellation + Dispose; offline hold -> flush; overflow policy; at-least-once + idempotency; corrupt/missing persisted file; JsonUtility round-trip.

Report format (strict):
- "## What & why" — 2-4 sentences grounded in TASK_PROGRESS.md.
- "## Findings" — list; each: `[SEV] file:line — problem. Fix: concrete suggestion.` SEV in {BLOCKER, MAJOR, MINOR, NIT}. IN-SCOPE only. `file:line` mandatory.
- "## Out-of-scope (pre-existing)" — optional; issues in code this work did not change.
- "## Good" — what was done well.

Constraints: read-only sandbox. Do NOT edit files, do NOT run mutating commands. Your report (written via -o) IS the deliverable and nothing else.
BODY
)"
# codex reads stdin even with a prompt arg -> hangs on EOF; '< /dev/null' is MANDATORY (codex 0.135). Do not remove. $? instead of PIPESTATUS (empty in zsh).
codex exec -s read-only -C "$(pwd)" -o "$RDIR/round1-review.md" "$PROMPT" < /dev/null > "$RDIR/round1.log" 2>&1; EXIT=$?
echo "codex exit=$EXIT — report: $RDIR/round1-review.md"
tail -30 "$RDIR/round1.log"
```

When it finishes, **read** `$RDIR/round1-review.md`. Empty or exit != 0 → check `round1.log`, fix the invocation, retry.

---

## Step 2 — Triage and fixes

1. **Do not apply findings blindly.** Decide accept/reject per finding:
   - Reject if it conflicts with a documented deliberate decision (`DESIGN.md`), the task's stated intent (`TASK_PROGRESS.md`), or the established architecture/conventions in the codebase (`KnowledgeBase/BehaviourRules/CODING_STANDARDS.md`). Record the reason.
   - "## Out-of-scope" items are **not fixed here** — verify each is real; if so, note it for the user and add it to `KnowledgeBase/Documentation/WARNINGS.md` (don't silently fix unrelated code).
   - Accept the rest by priority BLOCKER → MAJOR → MINOR → NIT.
2. Apply edits matching the surrounding code's style. No placeholders/stubs.
3. Keep a short decision log in `$RDIR/fixes.md`: accepted / rejected (+reason) / changed (file:line).

---

## Step 3 — Verify (headless Unity EditMode tests)

Run the suite **only when compile-affecting files changed** (`.cs` / `.asmdef` / `.asmref`); for a docs/PHP/asset-only diff, skip it. **The Unity Editor must be closed** (single-instance project lock). Large timeout (batchmode startup + tests can take a few minutes).

```bash
RDIR=".self-review/$(git rev-parse --abbrev-ref HEAD | tr '/' '-')/gpt-5-5"; source "$RDIR/scope.env"
ROOT="$(git rev-parse --show-toplevel)"; PROJ="$ROOT/tracking-sdk"
CHANGED=$( { git --no-pager diff --name-only "$BASE"...HEAD; git --no-pager diff --name-only HEAD; git ls-files --others --exclude-standard; } | sort -u )
if printf '%s\n' "$CHANGED" | grep -qiE '\.(cs|asmdef|asmref)$'; then
  VER=$(awk '/^m_EditorVersion:/{print $2}' "$PROJ/ProjectSettings/ProjectVersion.txt")
  UNITY="/Applications/Unity/Hub/Editor/$VER/Unity.app/Contents/MacOS/Unity"
  if [ -f "$PROJ/Library/EditorInstance.json" ]; then
    PID=$(grep -o '"process_id" : [0-9]*' "$PROJ/Library/EditorInstance.json" | grep -o '[0-9]*')
    if [ -n "$PID" ] && ps -p "$PID" >/dev/null 2>&1; then
      echo "VERIFY BLOCKED: Unity Editor is open (PID $PID). Close it and re-run this step."; exit 0
    fi
  fi
  [ -x "$UNITY" ] || { echo "WARN: Unity not at $UNITY — open Hub or fix the path."; exit 0; }
  rm -f "$RDIR/tests.xml" "$RDIR/tests.log"
  "$UNITY" -runTests -batchmode -nographics -projectPath "$PROJ" -testPlatform EditMode \
    -testResults "$RDIR/tests.xml" -logFile "$RDIR/tests.log"
  echo "unity exit=$?"
  grep -m1 -o '<test-run[^>]*>' "$RDIR/tests.xml" 2>/dev/null \
    || { echo "no results — compile errors? tail:"; grep -iE 'error CS[0-9]+|Compilation failed' "$RDIR/tests.log" | head; tail -15 "$RDIR/tests.log"; }
else
  echo "SKIP: no .cs/.asmdef/.asmref in the diff — tests not needed (docs/php/asset-only)."
fi
```

`failed="0"` and a non-zero `passed` → green. Any failure or compile error → fix to green before moving on. Report failures honestly.

---

## Step 4 — GPT reviewer re-checks (round 2)

Give GPT its previous report + your change summary; ask it to assess the current state. **Large timeout.**

```bash
RDIR=".self-review/$(git rev-parse --abbrev-ref HEAD | tr '/' '-')/gpt-5-5"; source "$RDIR/scope.env"
PROMPT="You reviewed this branch earlier. Your previous report: $RDIR/round1-review.md. The developer's change summary: $RDIR/fixes.md. Task log + full diff: $RDIR/context.md. Branch $BRANCH vs base $BASE.
$(cat <<'BODY'
Read your previous report, the change summary, and the TASK_PROGRESS.md section of context.md, then re-check the CURRENT state via `git status` / `git diff`. Assess strictly.

SCOPE: only code in the diff (committed $BASE...HEAD + uncommitted working tree) is in scope. Verify with git before claiming this branch changed a file.

Format (strict):
- "## Fixed well" — findings correctly closed (file:line).
- "## Fixed poorly / not closed" — wrong, incomplete, or a regression.
- "## New findings" — anything that appeared after the fixes (in-scope only).

Constraints: read-only sandbox. Do not touch code, do not run mutating commands. Your report (via -o) is the only deliverable.
BODY
)"
# '< /dev/null' MANDATORY (codex 0.135 reads stdin even with a prompt arg and hangs on EOF). Do not remove.
codex exec -s read-only -C "$(pwd)" -o "$RDIR/round2-recheck.md" "$PROMPT" < /dev/null > "$RDIR/round2.log" 2>&1; EXIT=$?
echo "codex exit=$EXIT — report: $RDIR/round2-recheck.md"
tail -30 "$RDIR/round2.log"
```

**Read** `$RDIR/round2-recheck.md`.

---

## Step 5 — Final fixes

1. Close "Fixed poorly / not closed" and valid "New findings" (same accept/reject triage — documented decisions / task intent win).
2. Repeat Step 3 verification.
3. Genuinely deferred items (out of scope / large) → record them in `KnowledgeBase/Documentation/WARNINGS.md` and state them to the user; don't stay silent.

---

## Step 6 — Final report to the user

Print a `## GPT review summary (GPT-5.5 via Codex, DEEP)` section:
- Branch / base / diff size.
- Round 1: findings by severity; accepted / rejected (+short reason); any out-of-scope items.
- What changed (file:line for the key edits).
- Round 2: the reviewer's verdict + what was closed.
- Verification: EditMode test result (e.g. `total=70 passed=70 skipped=0`) — honest; or "skipped — docs/php-only".
- Residual / deferred items → references to WARNINGS (W-NNN).
- Reminder: **nothing committed** — the commit is yours.

Artifacts stay in `$RDIR/` (gitignored): `context.md`, `round1-review.md`, `fixes.md`, `round2-recheck.md`, `tests.xml`, logs.
