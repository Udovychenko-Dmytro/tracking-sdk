---
description: BRANCH-scoped self-review (only what this branch changed — commits since it forked from main, plus uncommitted) by a fresh Claude (Opus 4.8, clean context, --effort max), then autopilot the review -> fix -> re-review -> verify loop. Verification = the headless Unity EditMode test run.
argument-hint: "[base-ref] (optional; defaults to origin/main — i.e. review only this branch's changes)"
allowed-tools: Bash, Read, Edit, Write, Grep, Glob
---

# /self-review-branch — BRANCH-scoped self-review by a fresh Claude (Opus 4.8)

You run a **fully autonomous** self-review loop. The external reviewer is a **fresh Claude (Opus 4.8)** in a separate process with clean context (`claude -p --model claude-opus-4-8 --effort max`). You are the developer: you receive the review, fix, ask for a re-check, verify, and finish.

Why a separate process and not a subagent: clean context (no attachment to the code you just wrote) + a pinned model version — a genuine fresh pair of eyes from the same generation as you.

**Scope = BRANCH.** Reviews only what *this branch* changed — the commits since it forked from `main` (`$BASE...HEAD`, where `$BASE` defaults to `origin/main`; the three-dot diff is taken from the merge-base, so the review stays the branch's own commits even if `main` has advanced) **plus** the uncommitted working tree. Use it before opening a PR. For the **whole SDK** use `/self-review`; for **only uncommitted** changes use `/self-review-uncommitted`.

**The reviewer runs strictly read-only** (`--permission-mode plan`) — it can only read and print a report to stdout (redirected to a file). **Every reviewer run uses `--effort max`.** This command does not commit — it leaves the working tree resolved and reports; the commit is yours.

The argument (`$ARGUMENTS`) is an optional base-ref. Empty → `origin/main` (else local `main`, else the repo's initial commit if no `main` exists). On `main` itself the branch diff is empty, so the review covers just the uncommitted tree.

> **Timeout:** a review takes several minutes. When you launch `claude -p` via Bash, set a large timeout (up to `600000` ms). The Bash tool will usually **auto-background** the long run — when it does, just wait for the background-completion `<task-notification>`; do not poll. The report file stays 0 bytes until `claude -p` finishes.
>
> **Project shape:** single git repo; git root = repo root. The Unity project is the subfolder `tracking-sdk/`; `README.md` lives at the repo root, the other docs under `KnowledgeBase/Documentation/`. Review spans the **whole repo** (no path scoping). Verification = the **headless Unity EditMode test run** (the full EditMode suite — 145 deterministic + 2 live tests), not a bare compile check.

---

## Step 0 — Setup and scope

```bash
cd "$(git rev-parse --show-toplevel)"
RDIR=".self-review/$(git rev-parse --abbrev-ref HEAD | tr '/' '-')/branch"
mkdir -p "$RDIR"
[ -f .self-review/.gitignore ] || printf '*\n' > .self-review/.gitignore

# Base: argument, else origin/main (the branch's fork point), else local main, else the repo's initial commit.
BASE="$ARGUMENTS"
if [ -z "$BASE" ]; then
  if git rev-parse --verify origin/main >/dev/null 2>&1; then
    BASE=origin/main
  elif git rev-parse --verify main >/dev/null 2>&1; then
    BASE=main
  else
    BASE="$(git rev-list --max-parents=0 HEAD | tail -1)"   # no main ref -> whole history
  fi
fi
BRANCH=$(git rev-parse --abbrev-ref HEAD)

# Resolve the standalone `claude` CLI (macOS); do not rely on bare `claude` being on the spawned PATH.
CLAUDE_BIN="$(command -v claude 2>/dev/null || true)"
if [ -z "$CLAUDE_BIN" ]; then
  for cand in "$HOME/.local/bin/claude" "$HOME/.claude/local/claude" \
              /opt/homebrew/bin/claude /usr/local/bin/claude \
              "$(npm config get prefix 2>/dev/null)/bin/claude"; do
    [ -n "$cand" ] && [ -f "$cand" ] && { CLAUDE_BIN="$cand"; break; }
  done
fi
[ -z "$CLAUDE_BIN" ] && { echo "ERROR: 'claude' CLI not found (PATH + known locations). Install: npm i -g @anthropic-ai/claude-code"; exit 1; }

printf 'BASE=%q\nBRANCH=%q\nCLAUDE_BIN=%q\n' "$BASE" "$BRANCH" "$CLAUDE_BIN" > "$RDIR/scope.env"
echo "scope=BRANCH branch=$BRANCH base=$BASE claude=$CLAUDE_BIN"
git --no-pager diff --stat "$BASE"...HEAD
git --no-pager status --short
```
(Substitute `$ARGUMENTS` literally from the command argument.)

Collect the diff context. `git add -A -N` makes any **untracked** files visible to `git diff` (intent-to-add only — no content is staged); `git reset -q` restores the original state afterward:

```bash
RDIR=".self-review/$(git rev-parse --abbrev-ref HEAD | tr '/' '-')/branch"; source "$RDIR/scope.env"
git add -A -N >/dev/null 2>&1
{
  echo "# Self-review context (BRANCH: $BASE...HEAD + working tree + untracked)"
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
RDIR=".self-review/$(git rev-parse --abbrev-ref HEAD | tr '/' '-')/branch"; source "$RDIR/scope.env"
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

## Step 1 — Claude reviewer does the review (round 1)

Launch a fresh Claude (Opus 4.8), read-only, `--effort max`; report to a file. **Large timeout.**

```bash
RDIR=".self-review/$(git rev-parse --abbrev-ref HEAD | tr '/' '-')/branch"; MODEL="claude-opus-4-8"; source "$RDIR/scope.env"
PROMPT="You are a senior C#/Unity SDK reviewer with clean context. You are doing a BRANCH review of git branch $BRANCH against base $BASE (this branch's own commits) plus uncommitted changes. The full diff + the task log are in $RDIR/context.md (read it).
$(cat <<'BODY'
FIRST understand the task: read the '## TASK_PROGRESS.md' section of context.md (goal, plan, what was done). Read DESIGN.md for the deliberate design decisions and README.md for the architecture. Judge the diff against that stated intent — flag anything that deviates from the plan or leaves a step unfinished.

SCOPE DISCIPLINE (strict): review ONLY the lines in the diff (context.md) and their immediate context. Confirm a symbol actually appears in the diff before claiming this work changed it. Pre-existing issues in untouched code go under "## Out-of-scope", never in Findings.

This is a production-grade in-process event-tracking SDK for Unity: public API -> bounded thread-safe queue -> background batching dispatcher -> pluggable transport, with retries/backoff, durable persistence, lifecycle flush, connectivity-awareness, circuit breaker, dead-letter, metrics, logging hook, privacy opt-out. Look hard at:
  - bugs and logic errors;
  - CONCURRENCY: the queue is hit from any thread and drained by a background worker. Check lock correctness, no torn state, no unobserved Task exceptions, ConfigureAwait(false) on library awaits, TaskCompletionSource completion on every path (delivered / retries-exhausted / evicted / purged / cancelled), no deadlock from blocking on async, no main-thread assumptions on the worker (e.g. UnityWebRequest is main-thread-only -> HttpClient is used deliberately);
  - ASYNC correctness and cancellation/Dispose (CancellationToken honored, worker shutdown clean, HttpClient/SemaphoreSlim disposed);
  - ERROR ISOLATION: the public API must never throw into game code (validate, swallow+log via the logger seam);
  - TEST DETERMINISM: tests must not depend on real network/disk/wall-clock — they use DI seams/fakes (ITransport, IEventStore, IClock, IDelayer, IConnectivity) and a virtual clock; live-network tests must be in the Live category. Flag any test that sleeps, hits the network in the default suite, or is order-dependent/flaky;
  - SERIALIZATION: JsonUtility cannot handle Dictionary<string,object> -> the type-tagged payload encoding; check round-trip + corrupt/missing-file resilience;
  - at-least-once + idempotency (stable event id reused on retries), bounded-memory drop policy, config defaults;
  - public API surface vs the docs/CHANGELOG; naming; dead code; comments (should be concise).

Report format (strict):
- "## What & why" — 2-4 sentences grounded in TASK_PROGRESS.md.
- "## Findings" — list; each: `[SEV] file:line — problem. Fix: concrete suggestion.` SEV in {BLOCKER, MAJOR, MINOR, NIT}. IN-SCOPE only. `file:line` mandatory.
- "## Out-of-scope (pre-existing)" — optional; issues in code this work did not change.
- "## Good" — what was done well.

Constraints: read-only (plan mode). Do NOT edit files, do NOT run mutating commands, do NOT call ExitPlanMode. Your final text IS the report and nothing else.
BODY
)"
"$CLAUDE_BIN" -p --model "$MODEL" --effort max --permission-mode plan "$PROMPT" \
  > "$RDIR/round1-review.md" 2> "$RDIR/round1.log" < /dev/null
echo "reviewer exit=$? — report: $RDIR/round1-review.md"
tail -5 "$RDIR/round1.log"
```

When it finishes, **read** `$RDIR/round1-review.md`. Empty or exit != 0 → check `round1.log`, fix the invocation, retry.

---

## Step 2 — Triage and fixes

1. **Do not apply findings blindly.** Decide accept/reject per finding:
   - Reject if it conflicts with a documented deliberate decision (`DESIGN.md`), the task's stated intent (`TASK_PROGRESS.md`), or the established architecture/conventions in the codebase. Record the reason.
   - "## Out-of-scope" items are **not fixed here** — verify each is real; if so, note it for the user (don't silently fix unrelated code).
   - Accept the rest by priority BLOCKER → MAJOR → MINOR → NIT.
2. Apply edits matching the surrounding code's style. No placeholders/stubs.
3. Keep a short decision log in `$RDIR/fixes.md`: accepted / rejected (+reason) / changed (file:line).

---

## Step 3 — Verify (headless Unity EditMode tests)

Run the suite **only when compile-affecting files changed** (`.cs` / `.asmdef` / `.asmref`); for a docs/PHP/asset-only diff, skip it. **The Unity Editor must be closed** (single-instance project lock). Large timeout (batchmode startup + tests can take a few minutes).

```bash
RDIR=".self-review/$(git rev-parse --abbrev-ref HEAD | tr '/' '-')/branch"; source "$RDIR/scope.env"
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

## Step 4 — Claude reviewer re-checks (round 2)

Give the fresh Claude its previous report + your change summary; ask it to assess the current state. `--effort max`. **Large timeout.**

```bash
RDIR=".self-review/$(git rev-parse --abbrev-ref HEAD | tr '/' '-')/branch"; MODEL="claude-opus-4-8"; source "$RDIR/scope.env"
PROMPT="You reviewed this branch earlier. Your previous report: $RDIR/round1-review.md. The developer's change summary: $RDIR/fixes.md. Task log + full diff: $RDIR/context.md. Branch $BRANCH vs base $BASE.
$(cat <<'BODY'
Read your previous report, the change summary, and the TASK_PROGRESS.md section of context.md, then re-check the CURRENT state via `git status` / `git diff`. Assess strictly.

SCOPE: only code in the diff (committed $BASE...HEAD + uncommitted working tree) is in scope. Verify with git before claiming this branch changed a file.

Format (strict):
- "## Fixed well" — findings correctly closed (file:line).
- "## Fixed poorly / not closed" — wrong, incomplete, or a regression.
- "## New findings" — anything that appeared after the fixes (in-scope only).

Constraints: read-only (plan mode). Do not touch code, do not run mutating commands, do not call ExitPlanMode. Final text is the report only.
BODY
)"
"$CLAUDE_BIN" -p --model "$MODEL" --effort max --permission-mode plan "$PROMPT" \
  > "$RDIR/round2-recheck.md" 2> "$RDIR/round2.log" < /dev/null
echo "reviewer exit=$? — report: $RDIR/round2-recheck.md"
tail -5 "$RDIR/round2.log"
```

**Read** `$RDIR/round2-recheck.md`.

---

## Step 5 — Final fixes

1. Close "Fixed poorly / not closed" and valid "New findings" (same accept/reject triage — documented decisions / task intent win).
2. Repeat Step 3 verification.
3. Genuinely deferred items (out of scope / large) → state them to the user; don't stay silent.

---

## Step 6 — Final report to the user

Print a `## Self-review summary (Opus 4.8, BRANCH)` section:
- Branch / base / diff size.
- Round 1: findings by severity; accepted / rejected (+short reason); any out-of-scope items.
- What changed (file:line for the key edits).
- Round 2: the reviewer's verdict + what was closed.
- Verification: EditMode test result (e.g. `total=145 passed=145 skipped=0`) — honest; or "skipped — docs/php-only".
- Residual / deferred items.
- Reminder: **nothing committed** — the commit is yours.

Artifacts stay in `$RDIR/` (gitignored): `context.md`, `round1-review.md`, `fixes.md`, `round2-recheck.md`, `tests.xml`, logs.
