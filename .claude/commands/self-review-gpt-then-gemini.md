---
description: Two external reviews in one pass — GPT first (Codex CLI), then Gemini 3.1 Pro (High) (Antigravity CLI) as a cross-vendor second pass, with fixes and verification after each. Verification = the headless Unity EditMode test run.
argument-hint: "[base-ref] (optional; defaults to origin/main, else the repo's initial commit — i.e. review the whole SDK)"
allowed-tools: Bash, Read, Edit, Write, Grep, Glob
---

# /self-review-gpt-then-gemini — two-reviewer pass (GPT → Gemini 3.1 Pro High)

You run a **fully autonomous** review loop with **two external reviewers in one pass**:

1. **GPT** (via **Codex CLI**, `codex exec -s read-only`) — first pass.
2. You triage and fix its findings → run verification.
3. **Gemini 3.1 Pro (High)** (via **Antigravity CLI**, `agy --print`) — a second, cross-vendor pass: it gets GPT's report plus a summary of your fixes, **re-checks GPT's fixes** *and* hunts for what GPT missed.
4. You triage and fix its findings → run verification → final summary.

Why cascade two vendors: different model families have different blind spots. Gemini on the second pass verifies GPT's work and catches what was missed, rather than re-reporting what's already closed.

**Both reviewers run strictly read-only.** GPT/Codex runs in a hard read-only sandbox (`-s read-only`) — it physically cannot edit code. `agy` has no hard read-only mode (only `--sandbox` = terminal restrictions), so the Gemini pass is wrapped in three layers: `--sandbox` + a prompt that forbids edits + a **before/after working-tree fingerprint check** (if the reviewer touched the tree → stop and report).

This command does **not commit** — it leaves the working tree resolved and reports; the commit is yours. On `main`, branch first if you intend to commit later.

The argument (`$ARGUMENTS`) is an optional base-ref for the diff. Empty → `origin/main` (if it differs from HEAD), else the repo's **initial commit** (review the entire SDK).

> **Timeout:** every external-CLI pass takes several minutes. When you launch `codex`/`agy` via Bash, set a large timeout (up to `600000` ms). The Bash tool will usually **auto-background** the long run — when it does, wait for the background-completion notification; do not poll. The report file stays empty until the CLI finishes. `--print-timeout 540s` keeps Gemini's own model limit just under the tool ceiling.
>
> **Project shape:** single git repo; git root = repo root. The Unity project is the subfolder `tracking-sdk/`; `README.md` lives at the repo root, the other docs under `KnowledgeBase/Documentation/`. Review spans the **whole repo** (no path scoping). Verification = the **headless Unity EditMode test run** (145 deterministic tests), not a bare compile check.

---

## Step 0 — Setup, both-CLI gate, and scope

1. Confirm both reviewers are available — a missing one makes the pass impossible (stop and tell the user):
   ```bash
   command -v codex >/dev/null 2>&1 || { echo "FATAL: codex (Codex CLI) not found in PATH — stop and tell the user"; exit 1; }; echo "codex OK"
   command -v agy   >/dev/null 2>&1 || { echo "FATAL: agy (Antigravity CLI) not found in PATH — stop and tell the user"; exit 1; }; echo "agy OK"
   agy models 2>/dev/null | grep -qx "Gemini 3.1 Pro (High)" \
     || { echo "FATAL: 'Gemini 3.1 Pro (High)' not in \`agy models\` — likely not logged into Antigravity. Stop and ask the user to log in."; exit 1; }; echo "agy model OK"
   ```
   Any `FATAL:` — tell the user and **stop**.

2. Set up the artifacts dir (self-ignored) and resolve the base-ref:
   ```bash
   cd "$(git rev-parse --show-toplevel)"
   RDIR=".self-review/$(git rev-parse --abbrev-ref HEAD | tr '/' '-')/gpt-then-gemini"
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

3. Collect the diff context into `$RDIR/context.md` — branch, base, committed diff **and** the uncommitted working tree. `git add -A -N` makes any **untracked** files visible to `git diff` (intent-to-add only — no content is staged); `git reset -q` restores the original state afterward:
   ```bash
   RDIR=".self-review/$(git rev-parse --abbrev-ref HEAD | tr '/' '-')/gpt-then-gemini"; source "$RDIR/scope.env"
   git add -A -N >/dev/null 2>&1
   {
     echo "# Self-review context (GPT → Gemini: $BASE...HEAD + working tree + untracked)"
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

4. Check there is anything to review:
   ```bash
   RDIR=".self-review/$(git rev-parse --abbrev-ref HEAD | tr '/' '-')/gpt-then-gemini"; source "$RDIR/scope.env"
   if git --no-pager diff "$BASE"...HEAD --quiet && git --no-pager diff HEAD --quiet && [ -z "$(git ls-files --others --exclude-standard)" ]; then
     echo "EMPTY: nothing to review against $BASE with a clean tree."
   fi
   ```
   If `EMPTY:` — tell the user and **stop**.

---

## Step 0.5 — Read the task context first (you, before triaging)

Ground yourself before launching the reviewers — this is what lets you triage well and prevents scope drift:

1. **Read `TASK_PROGRESS.md`** — the phased build log: goal, plan, what each phase did, test counts. Your triage judges the diff against this stated intent.
2. **Read `DESIGN.md`** — the rationale for the non-obvious decisions (async batch-delivery semantics, drop policy, at-least-once + idempotency, `HttpClient` vs `UnityWebRequest`, the DI seams). A reviewer suggestion that contradicts a documented, deliberate decision is a reject.
3. **Skim `README.md`** (architecture + implicit-requirements scorecard) and the package `CHANGELOG.md`.

Both reviewers are told to do the same — but *you* decide accept/reject.

---

## Step 1 — GPT (Codex) does the review (first pass)

Launch GPT hard read-only; the report is written to a file via `-o`. **Large timeout.**

```bash
RDIR=".self-review/$(git rev-parse --abbrev-ref HEAD | tr '/' '-')/gpt-then-gemini"; source "$RDIR/scope.env"
GPT_PROMPT="You are a senior C#/Unity SDK reviewer. You are doing a code review of git branch $BRANCH against base-ref $BASE plus uncommitted changes. The full diff + the task log are in $RDIR/context.md (read it).
$(cat <<'BODY'
FIRST read the project rules: CLAUDE.md and KnowledgeBase/BehaviourRules/CODING_STANDARDS.md (not auto-loaded) — the review must be grounded in them. Then read the '## TASK_PROGRESS.md' section of context.md (goal, plan, what was done), DESIGN.md for the deliberate design decisions, and README.md for the architecture. Judge the diff against that stated intent — flag anything that deviates from the plan or leaves a step unfinished.

SCOPE DISCIPLINE (strict): review ONLY the lines in the diff (context.md) and their immediate context. Confirm a symbol actually appears in the diff before claiming this work changed it. Pre-existing issues in untouched code go under "## Out-of-scope", never in Findings.

This is a production-grade in-process event-tracking SDK for Unity: tiny public API (ITracker.SendMessage / SendMapAsync) -> bounded thread-safe queue -> background batching dispatcher -> pluggable transport, with retries/backoff, durable persistence, lifecycle flush, connectivity-awareness, circuit breaker, dead-letter, metrics, logging hook, privacy opt-out. Look hard at:
  - bugs and logic errors;
  - CONCURRENCY: the queue is hit from any thread and drained by a background worker. Check lock correctness, no torn state, no unobserved Task exceptions, ConfigureAwait(false) on library awaits, TaskCompletionSource completion on EVERY path (delivered / retries-exhausted / evicted / rejected / purged / cancelled), no deadlock from blocking on async, no main-thread assumption on the worker (UnityWebRequest is main-thread-only -> HttpClient is deliberate);
  - ASYNC + cancellation/Dispose (token honored, worker stops clean, HttpClient/SemaphoreSlim disposed);
  - ERROR ISOLATION: the public API must never throw into game code (validate, swallow+log via the logger seam);
  - TEST DETERMINISM: no real network/disk/wall-clock in the default suite — DI seams/fakes (ITransport, IEventStore, IClock, IDelayer, IConnectivity) + a virtual clock; live tests must be in the Live category. Flag any test that sleeps, hits the network in the default suite, or is order-dependent/flaky;
  - SERIALIZATION: JsonUtility cannot handle Dictionary<string,object> -> the type-tagged payload encoding; check round-trip + corrupt/missing-file resilience;
  - at-least-once + idempotency (stable event id reused on retries); bounded-memory drop policy (DropOldest vs RejectNew); config defaults on TrackingConfig;
  - public API surface vs the docs/CHANGELOG; naming; dead code; comments (should be concise).
Standards reference (KnowledgeBase/BehaviourRules/CODING_STANDARDS.md): m_camelCase private fields, DmytroUdovychenko.Tracking namespaces, UPPER_SNAKE_CASE constants, explicit enum indices, sealed concrete classes, Allman braces, no var, DI via optional ctor params, error isolation, log via ITrackingLogger never Debug.* in runtime code, ConfigureAwait(false), TryGet+out.

Report format (strict):
- "## What & why" — 2-4 sentences grounded in TASK_PROGRESS.md.
- "## Findings" — list; each: `[SEV] file:line — problem. Fix: concrete suggestion.` SEV in {BLOCKER, MAJOR, MINOR, NIT}. IN-SCOPE only. `file:line` mandatory.
- "## Out-of-scope (pre-existing)" — optional; issues in code this work did not change.
- "## Good" — what was done well.

Constraints: read-only. Do NOT edit files, only the report.
BODY
)"
# codex exec reads stdin even with a prompt arg -> in the background it hangs waiting for EOF; so '< /dev/null' is MANDATORY (codex 0.135). Do not remove.
# No pipe: in zsh ${PIPESTATUS[0]} is empty and codex's exit would be lost. Write to the log, take $? directly.
codex exec -s read-only -C "$(pwd)" -o "$RDIR/gpt-review.md" "$GPT_PROMPT" < /dev/null > "$RDIR/gpt.log" 2>&1; GPT_EXIT=$?
tail -30 "$RDIR/gpt.log"; echo "codex exit=$GPT_EXIT — report: $RDIR/gpt-review.md"
```

When it finishes, **read** `$RDIR/gpt-review.md`. Empty or error → check the tail of the codex log, fix the invocation, retry.

---

## Step 2 — Triage and fixes (GPT)

1. **Do not apply findings blindly.** Decide accept/reject per finding:
   - Reject if it conflicts with a documented deliberate decision (`DESIGN.md`), the task's stated intent (`TASK_PROGRESS.md`), or an established codebase convention. Record the reason.
   - "## Out-of-scope" items are **not fixed here** — verify each is real; if so, note it for the user and add it to `KnowledgeBase/Documentation/WARNINGS.md` (don't silently fix unrelated code).
   - Accept the rest by priority BLOCKER → MAJOR → MINOR → NIT.
2. Apply edits matching the surrounding code's style (`KnowledgeBase/BehaviourRules/CODING_STANDARDS.md`), with the SDK edge-case walk in mind (`SendMessage(null)` / empty map / `Result == null`; thread-safety; TCS completed on every path; cancellation + `Dispose`; offline hold → flush; overflow policy; at-least-once + idempotency; corrupt/missing persisted file; `JsonUtility` round-trip). No placeholders/stubs.
3. Keep a short decision log in `$RDIR/gpt-fixes.md`: accepted / rejected (+reason) / changed (file:line). Gemini reads this on the second pass.

---

## Step 3 — Verify GPT's fixes (headless Unity EditMode tests)

Run the suite **only when compile-affecting files changed** (`.cs` / `.asmdef` / `.asmref`); for a docs/PHP/asset-only diff, skip it. **The Unity Editor must be closed** (single-instance project lock). Large timeout (batchmode startup + tests can take a few minutes).

```bash
RDIR=".self-review/$(git rev-parse --abbrev-ref HEAD | tr '/' '-')/gpt-then-gemini"; source "$RDIR/scope.env"
ROOT="$(git rev-parse --show-toplevel)"; PROJ="$ROOT/tracking-sdk"
CHANGED=$( { git --no-pager diff --name-only "$BASE"...HEAD; git --no-pager diff --name-only HEAD; git ls-files --others --exclude-standard; } | sort -u )
if printf '%s\n' "$CHANGED" | grep -qiE '\.(cs|asmdef|asmref)$'; then
  VER=$(awk '/^m_EditorVersion:/{print $2}' "$PROJ/ProjectSettings/ProjectVersion.txt")
  UNITY="/Applications/Unity/Hub/Editor/$VER/Unity.app/Contents/MacOS/Unity"
  if [ -f "$PROJ/Library/EditorInstance.json" ]; then
    PID=$(grep -o '"process_id" : [0-9]*' "$PROJ/Library/EditorInstance.json" | grep -o '[0-9]*')
    [ -n "$PID" ] && ps -p "$PID" >/dev/null 2>&1 && { echo "VERIFY BLOCKED: Unity Editor open (PID $PID) — close it and re-run this step."; exit 0; }
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

Green = `failed="0"` with a non-zero `passed` (suite is 145 deterministic tests + 2 `[Category("Live")]` live tests that run in the default headless suite and need network). Any failure or compile error → fix to green before moving on. Report failures honestly.

---

## Step 4 — Gemini 3.1 Pro (Antigravity) does the second pass

**First rebuild `context.md`** so it reflects the state AFTER GPT's fixes (Step 2), or Gemini reviews a stale diff. The rebuild block below is the same builder as Step 0.3. The main block then gives Gemini the updated `context.md` + GPT's report + your fix summary: it re-checks GPT's fixes and hunts for new issues. The tree is fingerprinted before/after. **Large timeout.**

```bash
RDIR=".self-review/$(git rev-parse --abbrev-ref HEAD | tr '/' '-')/gpt-then-gemini"; source "$RDIR/scope.env"
git add -A -N >/dev/null 2>&1
{
  echo "# Self-review context (post-GPT-fixes: $BASE...HEAD + working tree + untracked)"
  echo "- branch: $BRANCH"; echo "- base:   $BASE"; echo
  echo "## git status --short"; echo '```'; git --no-pager status --short; echo '```'
  echo; echo "## Committed diff ($BASE...HEAD)"; echo '```diff'; git --no-pager diff "$BASE"...HEAD; echo '```'
  echo; echo "## Working tree + untracked (vs HEAD)"; echo '```diff'; git --no-pager diff HEAD; echo '```'
} > "$RDIR/context.md"
git reset -q >/dev/null 2>&1
echo "context.md rebuilt post-fix ($(wc -c < "$RDIR/context.md") bytes)"
```

```bash
RDIR=".self-review/$(git rev-parse --abbrev-ref HEAD | tr '/' '-')/gpt-then-gemini"; MODEL="Gemini 3.1 Pro (High)"; source "$RDIR/scope.env"
fp() { { git --no-pager diff HEAD; git status --porcelain; } | shasum | cut -d' ' -f1; }
TREE_BEFORE=$(fp)
GEM_PROMPT="You are a senior C#/Unity SDK reviewer from a different model family (Gemini), doing the SECOND pass on git branch $BRANCH against base $BASE plus uncommitted changes. The full diff is in $RDIR/context.md. A GPT reviewer looked first: its report is $RDIR/gpt-review.md, and a summary of the fixes the developer already applied is $RDIR/gpt-fixes.md.
$(cat <<'BODY'
FIRST read the project rules: CLAUDE.md and KnowledgeBase/BehaviourRules/CODING_STANDARDS.md (NOT auto-loaded), plus DESIGN.md for the deliberate decisions. Then read GPT's report and the fix summary, and verify the CURRENT state via `git status` / `git diff`.

SCOPE DISCIPLINE (strict): only code in the diff (committed $BASE...HEAD + uncommitted working tree) is in scope. Confirm a symbol appears in the diff before claiming this branch changed it. Pre-existing issues in untouched code go under "## Out-of-scope".

This is a production-grade in-process event-tracking SDK for Unity (ITracker.SendMessage / SendMapAsync -> bounded thread-safe queue -> background batching dispatcher -> pluggable transport; retries/backoff, durable persistence, lifecycle flush, connectivity-awareness, circuit breaker, dead-letter, metrics, logging hook, privacy opt-out). Tasks:
1. Re-check the developer's fixes against GPT's report: which findings are correctly closed, which are incomplete/wrong/introduced a regression (with file:line).
2. Find NEW issues GPT missed; do not re-report items already closed. Look hard at: bugs/logic errors; CONCURRENCY (lock correctness, no torn state, no unobserved Task exceptions, ConfigureAwait(false) on library awaits, TaskCompletionSource completed on EVERY path: delivered / retries-exhausted / evicted / rejected / purged / cancelled, no deadlock from blocking on async, no main-thread assumption on the worker); ASYNC + cancellation/Dispose (token honored, worker stops clean, HttpClient/SemaphoreSlim disposed); ERROR ISOLATION (public API never throws into game code — validate, swallow+log via the logger seam); TEST DETERMINISM (no real network/disk/wall-clock in the default suite — DI seams/fakes ITransport/IEventStore/IClock/IDelayer/IConnectivity + a virtual clock; live tests are in the Live category); SERIALIZATION (JsonUtility can't do Dictionary<string,object> -> the type-tagged payload encoding; round-trip + corrupt/missing-file resilience); at-least-once + idempotency (stable event id reused on retries); bounded-memory drop policy (DropOldest vs RejectNew); config defaults on TrackingConfig; public API surface vs docs/CHANGELOG; naming; dead code; concise comments.
Standards reference: m_camelCase private fields, DmytroUdovychenko.Tracking namespaces, UPPER_SNAKE_CASE constants, explicit enum indices, sealed classes, Allman braces, no var, DI via optional ctor params, error isolation, log via ITrackingLogger never Debug.* in runtime code, ConfigureAwait(false), TryGet+out.

Report format (strict):
- "## GPT-fix verification" — per significant fix: ✓ correct / ⚠ incomplete / ✗ regression, with file:line.
- "## New findings" — list; each: `[SEV] file:line — problem. Fix: concrete suggestion.` SEV in {BLOCKER, MAJOR, MINOR, NIT}. IN-SCOPE only. `file:line` mandatory.
- "## Out-of-scope (pre-existing)" — optional.
- "## Good" — what was done well.

Constraints: READ-ONLY. Do NOT edit or create files, do NOT run mutating commands — only read (git status/diff, reading files). Your final stdout text IS the report and nothing else.
BODY
)"
agy --model "$MODEL" --sandbox --print-timeout 540s --print "$GEM_PROMPT" \
  > "$RDIR/gemini-review.md" 2> "$RDIR/gemini-review.log" < /dev/null
echo "gemini exit=$? — report: $RDIR/gemini-review.md"
TREE_AFTER=$(fp)
if [ "$TREE_BEFORE" != "$TREE_AFTER" ]; then
  echo "‼ WARNING: the reviewer modified the working tree (agy does not guarantee read-only). Do NOT continue the autopilot — report to the user. Delta:"
  git --no-pager status --short
else
  echo "✓ working tree untouched by the reviewer"
fi
tail -5 "$RDIR/gemini-review.log"
```

When it finishes, **read** `$RDIR/gemini-review.md`. Empty or exit != 0 → check `gemini-review.log` (common causes: `not logged into Antigravity` or timeout), fix, retry. If the `WARNING` block fired — stop and report.

---

## Step 5 — Triage and fixes (Gemini)

1. First close the items from "## GPT-fix verification" with status ⚠/✗ (incomplete fixes and regressions — top priority).
2. Then the valid "## New findings" — same accept/reject triage (documented decisions in `DESIGN.md` / task intent in `TASK_PROGRESS.md` / established conventions win), order BLOCKER → MAJOR → MINOR → NIT.
3. Keep a log in `$RDIR/gemini-fixes.md`: accepted / rejected (+reason) / changed (file:line).

---

## Step 6 — Verify Gemini's fixes

Repeat the Step 3 verification block (headless Unity EditMode tests; same skip rule for docs/PHP/asset-only diffs). Any failure → fix to green. Genuinely deferred items (out of scope, needs a design decision, large effort) → record in `KnowledgeBase/Documentation/WARNINGS.md`; don't stay silent.

---

## Step 7 — Final report to the user

Print a `## Self-review summary (GPT → Gemini 3.1 Pro High)` section:

- Branch / base / diff size.
- **GPT pass:** findings by severity; accepted / rejected (+short reason); what changed (file:line for the key edits).
- **Gemini pass:** verdict on GPT's fixes (what it confirmed / what it returned as incomplete-or-regression) + new findings by severity; accepted / rejected; what changed (file:line).
- Verification: EditMode test result after each pass (e.g. `total=73 passed=71 skipped=2`) — honest; or "skipped — docs/php-only".
- Residual / deferred items → WARNINGS references.
- Reminder: **nothing committed** — the commit is yours.

Artifacts stay in `$RDIR/` (gitignored): `context.md`, `gpt-review.md`, `gpt-fixes.md`, `gemini-review.md`, `gemini-fixes.md`, logs.
