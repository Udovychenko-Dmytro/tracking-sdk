---
description: Self-review of the current branch + uncommitted changes by an external Gemini 3.1 Pro (High) via Antigravity CLI (clean context, different model family), then autopilot the review -> fix -> re-review -> verify loop. Verification = the headless Unity EditMode test run.
argument-hint: "[base-ref] (optional; defaults to origin/main if it differs from HEAD, else the repo's initial commit)"
allowed-tools: Bash, Read, Edit, Write, Grep, Glob
---

# /self-review-gemini-3-1-pro-high — review by an external Gemini 3.1 Pro (High) (via Antigravity CLI)

You run a **fully autonomous** self-review loop. The external reviewer is **Gemini 3.1 Pro (High)**, launched via the **Antigravity CLI** (`agy --print`) in a separate process with clean context. You are the developer: you receive the review, fix, ask for a re-check, verify, and finish.

Why agy/Gemini and not a subagent or a Claude reviewer: clean context (no attachment to the code you just wrote) **+ a different model family (Google)** — it catches what a Claude reviewer can miss due to shared blind spots within one generation.

**No commits at any step** — per the **Commits rule**: commit or push only when the user explicitly asks; on `main`, branch first. This command never commits — it reports and leaves the tree for the user.

**On read-only — an important difference from the sibling commands.** `agy` has no hard read-only mode like `claude --permission-mode plan` or `codex -s read-only`. It only has `--sandbox` (terminal restrictions), and in `--print` mode tools run without interactive confirmation. So read-only is enforced by three layers: (a) launching with `--sandbox`; (b) the prompt strictly forbids any edits — read and report to stdout only; (c) **a before/after working-tree fingerprint is compared after each run** — if the reviewer changed anything, the autopilot halts and reports rather than continuing blind.

The argument (`$ARGUMENTS`) is an optional base-ref for the diff. Empty → `origin/main` if it differs from HEAD, else the repo's initial commit (review the whole SDK).

> **Timeout:** a review takes several minutes. When you launch `agy` via Bash, set a large timeout (up to `600000` ms); the Bash tool will usually **auto-background** the long run. `--print-timeout 540s` keeps the model's own limit just under the tool ceiling. The report file stays 0 bytes until `agy` finishes.
>
> **Project shape:** single git repo; git root = repo root. The Unity project is the subfolder `tracking-sdk/`; `README.md` lives at the repo root, the other docs under `KnowledgeBase/Documentation/`. Review spans the **whole repo** (no path scoping). Verification = the **headless Unity EditMode test run** (145 deterministic tests), not a bare compile check.

---

## Step 0 — Setup, agy check, and scope

1. Check that the Antigravity CLI is installed and the model is reachable (otherwise the review is impossible — stop and ask the user):
   ```bash
   command -v agy >/dev/null 2>&1 || echo "FATAL: agy (Antigravity CLI) not on PATH — stop and tell the user"
   agy models 2>/dev/null | grep -qx "Gemini 3.1 Pro (High)" \
     && echo "agy OK: model available" \
     || echo "FATAL: 'Gemini 3.1 Pro (High)' not in \`agy models\` — likely not logged into Antigravity. Stop and ask the user to sign in."
   ```
   Any `FATAL:` — tell the user and **stop**.

2. Set up the artifacts dir (self-ignored) and resolve scope:
   ```bash
   cd "$(git rev-parse --show-toplevel)"
   RDIR=".self-review/$(git rev-parse --abbrev-ref HEAD | tr '/' '-')/gemini-3-1-pro-high"
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
   printf 'BASE=%q\nBRANCH=%q\n' "$BASE" "$BRANCH" > "$RDIR/scope.env"
   echo "branch=$BRANCH base=$BASE"
   git --no-pager diff --stat "$BASE"...HEAD
   git --no-pager status --short
   ```
   (Substitute `$ARGUMENTS` literally from the command argument.)

3. Collect the diff context into `$RDIR/context.md` — branch, base, committed diff **and** uncommitted. `git add -A -N` makes untracked files visible to `git diff` (intent-to-add only — no content staged); `git reset -q` restores the original state afterward:
   ````bash
   RDIR=".self-review/$(git rev-parse --abbrev-ref HEAD | tr '/' '-')/gemini-3-1-pro-high"; source "$RDIR/scope.env"
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
   ````

4. Check there is anything to review:
   ```bash
   RDIR=".self-review/$(git rev-parse --abbrev-ref HEAD | tr '/' '-')/gemini-3-1-pro-high"; source "$RDIR/scope.env"
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

The reviewer is told to read the project rules itself (agy does not auto-load `CLAUDE.md` / `CODING_STANDARDS.md`) — but *you* decide accept/reject.

---

## Step 1 — Gemini reviewer does the review (round 1)

Launch Gemini 3.1 Pro (High) via `agy --print`: the report is written to a file via stdout, and the working tree is fingerprinted before/after. **Large timeout.**

```bash
RDIR=".self-review/$(git rev-parse --abbrev-ref HEAD | tr '/' '-')/gemini-3-1-pro-high"; MODEL="Gemini 3.1 Pro (High)"; source "$RDIR/scope.env"
fp() { { git --no-pager diff HEAD; git status --porcelain; } | shasum | cut -d' ' -f1; }
TREE_BEFORE=$(fp)
PROMPT="You are a senior C#/Unity SDK reviewer with clean context from a different model family (Gemini). You are doing a DEEP review of git branch $BRANCH against base $BASE plus uncommitted changes. The full diff + the task log are in $RDIR/context.md (read it).
$(cat <<'BODY'
FIRST read the project rules: `CLAUDE.md` and `KnowledgeBase/BehaviourRules/CODING_STANDARDS.md` — base your review on them (they are NOT auto-loaded). Then read the '## TASK_PROGRESS.md' section of context.md (goal, plan, what was done), DESIGN.md for the deliberate design decisions, and README.md for the architecture. Judge the diff against that stated intent — flag anything that deviates from the plan or leaves a step unfinished.

SCOPE DISCIPLINE (strict): review ONLY the lines in the diff (context.md) and their immediate context — not the whole repo. Confirm a symbol actually appears in the diff before claiming this work changed it. Pre-existing issues in untouched code go under "## Out-of-scope", never in Findings.

This is a production-grade in-process event-tracking SDK for Unity: tiny public API (ITracker.SendMessage / SendMapAsync) -> bounded thread-safe queue -> background batching dispatcher -> pluggable transport, with retries/backoff, durable persistence, lifecycle flush, connectivity-awareness, circuit breaker, dead-letter, metrics, logging hook, privacy opt-out. Look hard at:
  - bugs and logic errors;
  - CONCURRENCY: the bounded queue is hit from any thread and drained by a background worker. Check lock correctness, no torn state, no unobserved Task exceptions, ConfigureAwait(false) on library awaits, TaskCompletionSource completion on EVERY path (delivered / retries-exhausted / evicted / rejected / purged / cancelled), no deadlock from blocking on async, no main-thread assumption on the worker (UnityWebRequest is main-thread-only -> HttpClient is deliberate);
  - ASYNC + cancellation/Dispose (CancellationToken honored, worker stops clean, HttpClient/SemaphoreSlim disposed);
  - ERROR ISOLATION: the public API must never throw into game code (validate, swallow+log via the logger seam);
  - TEST DETERMINISM: no real network/disk/wall-clock in the default suite — DI seams/fakes (ITransport, IEventStore, IClock, IDelayer, IConnectivity) and a virtual clock; live tests must be in the Live category. Flag any test that sleeps, hits the network in the default suite, or is order-dependent/flaky;
  - SERIALIZATION: JsonUtility cannot handle Dictionary<string,object> -> the type-tagged payload encoding; check round-trip + corrupt/missing-file resilience;
  - at-least-once + idempotency (stable event id reused on retries); bounded-memory drop policy (DropOldest vs RejectNew); config defaults on TrackingConfig;
  - public API surface vs the docs/CHANGELOG; naming; dead code; concise comments.

Report format (strict):
- "## What & why" — 2-4 sentences grounded in TASK_PROGRESS.md.
- "## Findings" — list; each: `[SEV] file:line — problem. Fix: concrete suggestion.` SEV in {BLOCKER, MAJOR, MINOR, NIT}. IN-SCOPE only. `file:line` mandatory.
- "## Out-of-scope (pre-existing)" — optional; issues in code this work did not change.
- "## Good" — what was done well.

Constraints: READ-ONLY. Do NOT edit or create files, do NOT run mutating commands — read only (git status/diff, file reads). Your final text in stdout IS the report and nothing else.
BODY
)"
agy --model "$MODEL" --sandbox --print-timeout 540s --print "$PROMPT" \
  > "$RDIR/round1-review.md" 2> "$RDIR/round1.log" < /dev/null
echo "reviewer exit=$? — report: $RDIR/round1-review.md"
TREE_AFTER=$(fp)
if [ "$TREE_BEFORE" != "$TREE_AFTER" ]; then
  echo "‼ WARNING: the reviewer modified the working tree (agy does not guarantee read-only). Do NOT continue the autopilot — report to the user. Delta:"
  git --no-pager status --short
else
  echo "✓ working tree untouched by the reviewer"
fi
tail -5 "$RDIR/round1.log"
```

When it finishes, **read** `$RDIR/round1-review.md`. Empty or exit != 0 → check `round1.log` (common cause: `not logged into Antigravity` or a timeout), fix the invocation, retry. If the `WARNING` branch fired — stop and report to the user that the reviewer touched the tree.

---

## Step 2 — Triage and fixes

1. **Do not apply findings blindly.** Decide accept/reject per finding:
   - Reject if it conflicts with a documented deliberate decision (`DESIGN.md`), the task's stated intent (`TASK_PROGRESS.md`), or an established codebase convention. Record the reason.
   - "## Out-of-scope" items are **not fixed here** — verify each is real; if so, note it for the user (don't silently fix unrelated code).
   - Accept the rest by priority BLOCKER → MAJOR → MINOR → NIT.
2. Apply edits per the coding standards (`KnowledgeBase/BehaviourRules/CODING_STANDARDS.md`: `m_camelCase` private fields, `DmytroUdovychenko.Tracking` namespaces, `UPPER_SNAKE_CASE` constants, explicit enum indices, `sealed` classes, Allman braces, no `var`, DI via optional ctor params, error isolation, log via `ITrackingLogger` never `Debug.*` in runtime code, `ConfigureAwait(false)`, `TryGet`+`out`) and the **Edge cases** walk (`SendMessage(null)` / empty map / `Result == null`; thread-safety; TCS completed on every path; cancellation + `Dispose`; offline hold → flush; overflow policy; at-least-once + idempotency; corrupt/missing persisted file; `JsonUtility` round-trip). No placeholders/stubs; match the surrounding code's style.
3. Keep a short decision log in `$RDIR/fixes.md`: accepted / rejected (+reason) / changed (file:line).

---

## Step 3 — Verify (headless Unity EditMode tests)

Run the suite **only when compile-affecting files changed** (`.cs` / `.asmdef` / `.asmref`); for a docs/PHP/asset-only diff, skip it. **The Unity Editor must be closed** (single-instance project lock). Large timeout (batchmode startup + tests can take a few minutes). Report failures honestly — never silently fix.

```bash
RDIR=".self-review/$(git rev-parse --abbrev-ref HEAD | tr '/' '-')/gemini-3-1-pro-high"; source "$RDIR/scope.env"
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

`failed="0"` with a non-zero `passed` → green. The suite is currently **145 deterministic tests + 2 `[Category("Live")]` live tests** that run in the default headless suite and need network. Any failure or compile error → fix to green before moving on.

---

## Step 4 — Gemini reviewer re-checks (round 2)

Give Gemini its previous report + your change summary; ask it to assess the current state. **Large timeout.**

```bash
RDIR=".self-review/$(git rev-parse --abbrev-ref HEAD | tr '/' '-')/gemini-3-1-pro-high"; MODEL="Gemini 3.1 Pro (High)"; source "$RDIR/scope.env"
fp() { { git --no-pager diff HEAD; git status --porcelain; } | shasum | cut -d' ' -f1; }
TREE_BEFORE=$(fp)
PROMPT="You reviewed this branch earlier. Your previous report: $RDIR/round1-review.md. The developer's change summary: $RDIR/fixes.md. Task log + full diff: $RDIR/context.md. Branch $BRANCH vs base $BASE.
$(cat <<'BODY'
Read your previous report, the change summary, and the TASK_PROGRESS.md section of context.md, then re-check the CURRENT state via `git status` / `git diff`. Assess strictly.

SCOPE: only code in the diff (committed $BASE...HEAD + uncommitted working tree) is in scope. Verify with git before claiming this branch changed a file.

Format (strict):
- "## Fixed well" — findings correctly closed (file:line).
- "## Fixed poorly / not closed" — wrong, incomplete, or a regression.
- "## New findings" — anything that appeared after the fixes (in-scope only).

Constraints: READ-ONLY. Do not edit or create files, do not run mutating commands. Final text in stdout is the report only.
BODY
)"
agy --model "$MODEL" --sandbox --print-timeout 540s --print "$PROMPT" \
  > "$RDIR/round2-recheck.md" 2> "$RDIR/round2.log" < /dev/null
echo "reviewer exit=$? — report: $RDIR/round2-recheck.md"
TREE_AFTER=$(fp)
[ "$TREE_BEFORE" = "$TREE_AFTER" ] && echo "✓ working tree untouched by the reviewer" || { echo "‼ WARNING: the reviewer modified the working tree — report to the user:"; git --no-pager status --short; }
tail -5 "$RDIR/round2.log"
```

**Read** `$RDIR/round2-recheck.md`. If the `WARNING` branch fired — stop and report.

---

## Step 5 — Final fixes

1. Close "Fixed poorly / not closed" and valid "New findings" (same accept/reject triage — documented decisions in `DESIGN.md` / the task intent in `TASK_PROGRESS.md` win).
2. Repeat Step 3 verification.
3. Genuinely deferred items (out of scope / needs an ADR / large effort) → record in `KnowledgeBase/Documentation/WARNINGS.md`; don't stay silent.

---

## Step 6 — Final report to the user

Print a `## Self-review summary (Gemini 3.1 Pro High)` section:

- Branch / base / diff size.
- Round 1: findings by severity; accepted / rejected (+short reason); any out-of-scope items.
- What changed (file:line for the key edits).
- Round 2: the reviewer's verdict + what was closed.
- Verification: EditMode test result (e.g. `total=73 passed=71 skipped=2`) — honest; or "skipped — docs/php-only".
- Residual / deferred items → WARNINGS.md references.
- Reminder: **nothing committed** — the commit is yours.

Artifacts stay in `$RDIR/` (gitignored): `context.md`, `round1-review.md`, `fixes.md`, `round2-recheck.md`, `tests.xml`, logs.
