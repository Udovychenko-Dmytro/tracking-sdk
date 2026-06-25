---
description: SHALLOW self-review of only the UNCOMMITTED working tree (staged + unstaged + untracked, vs HEAD) by a fresh Claude (Opus 4.8, clean context, --effort max), then autopilot review -> fix -> re-review -> verify.
argument-hint: "(no argument — reviews what is currently uncommitted)"
allowed-tools: Bash, Read, Edit, Write, Grep, Glob
---

# /self-review-uncommitted — SHALLOW self-review by a fresh Claude (Opus 4.8)

Same autonomous loop as `/self-review`, but scoped to **only the uncommitted changes** (staged + unstaged + untracked, vs `HEAD`) — a quick pass before committing. For the whole-SDK review use `/self-review`.

The reviewer is a fresh **Claude (Opus 4.8)**, read-only (`--permission-mode plan`), `--effort max`. This command does not commit. See `/self-review` for the timeout / auto-background / project-shape notes — they apply identically. Verification = the headless Unity EditMode test run.

---

## Step 0 — Setup, scope, and the "anything uncommitted?" gate

```bash
cd "$(git rev-parse --show-toplevel)"
RDIR=".self-review/$(git rev-parse --abbrev-ref HEAD | tr '/' '-')/uncommitted"
mkdir -p "$RDIR"
[ -f .self-review/.gitignore ] || printf '*\n' > .self-review/.gitignore
BRANCH=$(git rev-parse --abbrev-ref HEAD)

CLAUDE_BIN="$(command -v claude 2>/dev/null || true)"
if [ -z "$CLAUDE_BIN" ]; then
  for cand in "$HOME/.local/bin/claude" "$HOME/.claude/local/claude" \
              /opt/homebrew/bin/claude /usr/local/bin/claude \
              "$(npm config get prefix 2>/dev/null)/bin/claude"; do
    [ -n "$cand" ] && [ -f "$cand" ] && { CLAUDE_BIN="$cand"; break; }
  done
fi
[ -z "$CLAUDE_BIN" ] && { echo "ERROR: 'claude' CLI not found. Install: npm i -g @anthropic-ai/claude-code"; exit 1; }
printf 'BRANCH=%q\nCLAUDE_BIN=%q\n' "$BRANCH" "$CLAUDE_BIN" > "$RDIR/scope.env"

if git --no-pager diff HEAD --quiet && [ -z "$(git ls-files --others --exclude-standard)" ]; then
  echo "EMPTY: working tree is clean — nothing uncommitted to review. (Use /self-review for the whole SDK.)"
fi
echo "scope=UNCOMMITTED branch=$BRANCH claude=$CLAUDE_BIN"
git --no-pager status --short
```
If `EMPTY:` — tell the user and **stop**.

Collect context (the `git add -A -N` trick surfaces untracked files in the diff; `git reset -q` restores):
```bash
RDIR=".self-review/$(git rev-parse --abbrev-ref HEAD | tr '/' '-')/uncommitted"; source "$RDIR/scope.env"
git add -A -N >/dev/null 2>&1
{
  echo "# Self-review context (UNCOMMITTED: working tree vs HEAD)"
  echo "- branch: $BRANCH"; echo
  echo "## TASK_PROGRESS.md (intent)"
  if [ -f TASK_PROGRESS.md ]; then echo '```markdown'; cat TASK_PROGRESS.md; echo '```'; else echo "_(none)_"; fi
  echo; echo "## git status --short"; echo '```'; git --no-pager status --short; echo '```'
  echo; echo "## Uncommitted diff (vs HEAD, incl. new files)"; echo '```diff'; git --no-pager diff HEAD; echo '```'
} > "$RDIR/context.md"
git reset -q >/dev/null 2>&1
echo "context bytes: $(wc -c < "$RDIR/context.md")"
```

---

## Step 0.5 — Read the task context

Read `TASK_PROGRESS.md` (intent) and `DESIGN.md` (deliberate decisions) so your triage rejects suggestions that contradict a documented choice. Skim `README.md` for the architecture.

---

## Step 1 — Reviewer round 1

```bash
RDIR=".self-review/$(git rev-parse --abbrev-ref HEAD | tr '/' '-')/uncommitted"; MODEL="claude-opus-4-8"; source "$RDIR/scope.env"
PROMPT="You are a senior C#/Unity SDK reviewer with clean context, reviewing ONLY the uncommitted changes of branch $BRANCH. The diff + task log are in $RDIR/context.md (read it).
$(cat <<'BODY'
Read the '## TASK_PROGRESS.md' section of context.md, plus DESIGN.md and README.md, then review ONLY the lines in the uncommitted diff and their immediate context.

This is a production-grade in-process event-tracking SDK for Unity (API -> bounded thread-safe queue -> background batching dispatcher -> transport; retries/backoff, persistence, lifecycle flush, connectivity, circuit breaker, dead-letter, metrics, logging hook, privacy). Look for: bugs/logic errors; concurrency correctness (locking, no torn state, no unobserved Task exceptions, ConfigureAwait(false), TaskCompletionSource completed on every path, no deadlock, no main-thread assumption on the worker); async + cancellation/Dispose; error isolation (API never throws into game code); TEST DETERMINISM (no real network/disk/wall-clock in the default suite — DI seams/fakes + virtual clock; live tests in the Live category); serialization round-trip + corrupt-file resilience; at-least-once + idempotency; naming; dead code; concise comments.

Report format (strict):
- "## What & why" — 2-3 sentences.
- "## Findings" — `[SEV] file:line — problem. Fix: suggestion.` SEV in {BLOCKER, MAJOR, MINOR, NIT}. file:line mandatory.
- "## Out-of-scope (pre-existing)" — optional.
- "## Good" — brief.

Constraints: read-only (plan mode). Do NOT edit, do NOT run mutating commands, do NOT call ExitPlanMode. Final text IS the report.
BODY
)"
"$CLAUDE_BIN" -p --model "$MODEL" --effort max --permission-mode plan "$PROMPT" \
  > "$RDIR/round1-review.md" 2> "$RDIR/round1.log" < /dev/null
echo "reviewer exit=$? — report: $RDIR/round1-review.md"; tail -5 "$RDIR/round1.log"
```
**Read** `$RDIR/round1-review.md`.

---

## Step 2 — Triage and fixes

Accept/reject each finding (reject anything contradicting `DESIGN.md` / task intent / established conventions — record why). Out-of-scope items: verify and surface to the user, don't fix unrelated code. Apply accepted fixes matching the surrounding style. Log decisions in `$RDIR/fixes.md`.

---

## Step 3 — Verify (headless Unity EditMode tests)

Only if `.cs`/`.asmdef`/`.asmref` changed; Editor must be closed; large timeout:
```bash
RDIR=".self-review/$(git rev-parse --abbrev-ref HEAD | tr '/' '-')/uncommitted"; source "$RDIR/scope.env"
ROOT="$(git rev-parse --show-toplevel)"; PROJ="$ROOT/tracking-sdk"
CHANGED=$( { git --no-pager diff --name-only HEAD; git ls-files --others --exclude-standard; } | sort -u )
if printf '%s\n' "$CHANGED" | grep -qiE '\.(cs|asmdef|asmref)$'; then
  VER=$(awk '/^m_EditorVersion:/{print $2}' "$PROJ/ProjectSettings/ProjectVersion.txt")
  UNITY="/Applications/Unity/Hub/Editor/$VER/Unity.app/Contents/MacOS/Unity"
  if [ -f "$PROJ/Library/EditorInstance.json" ]; then
    PID=$(grep -o '"process_id" : [0-9]*' "$PROJ/Library/EditorInstance.json" | grep -o '[0-9]*')
    [ -n "$PID" ] && ps -p "$PID" >/dev/null 2>&1 && { echo "VERIFY BLOCKED: Editor open (PID $PID) — close it and re-run."; exit 0; }
  fi
  [ -x "$UNITY" ] || { echo "WARN: Unity not at $UNITY"; exit 0; }
  rm -f "$RDIR/tests.xml" "$RDIR/tests.log"
  "$UNITY" -runTests -batchmode -nographics -projectPath "$PROJ" -testPlatform EditMode -testResults "$RDIR/tests.xml" -logFile "$RDIR/tests.log"
  echo "unity exit=$?"; grep -m1 -o '<test-run[^>]*>' "$RDIR/tests.xml" 2>/dev/null || { grep -iE 'error CS[0-9]+|Compilation failed' "$RDIR/tests.log" | head; tail -15 "$RDIR/tests.log"; }
else
  echo "SKIP: no .cs/.asmdef/.asmref changed."
fi
```
Fix to green before moving on.

---

## Step 4 — Reviewer round 2

```bash
RDIR=".self-review/$(git rev-parse --abbrev-ref HEAD | tr '/' '-')/uncommitted"; MODEL="claude-opus-4-8"; source "$RDIR/scope.env"
PROMPT="You reviewed these uncommitted changes earlier. Previous report: $RDIR/round1-review.md. Change summary: $RDIR/fixes.md. Context: $RDIR/context.md.
$(cat <<'BODY'
Re-check the CURRENT uncommitted state via git status/diff. Format (strict):
- "## Fixed well" (file:line)
- "## Fixed poorly / not closed"
- "## New findings" (in-scope only)
Constraints: read-only (plan mode); no edits, no mutating commands, no ExitPlanMode. Final text is the report only.
BODY
)"
"$CLAUDE_BIN" -p --model "$MODEL" --effort max --permission-mode plan "$PROMPT" \
  > "$RDIR/round2-recheck.md" 2> "$RDIR/round2.log" < /dev/null
echo "reviewer exit=$? — report: $RDIR/round2-recheck.md"; tail -5 "$RDIR/round2.log"
```
**Read** it, then close any valid items (Step 5 of `/self-review`), re-verify.

---

## Step 5 — Report

Print `## Self-review summary (Opus 4.8, UNCOMMITTED)`: findings by severity, accepted/rejected, what changed (file:line), round-2 verdict, EditMode test result, residual items. **Nothing committed — the commit is yours.** Artifacts in `$RDIR/` (gitignored).
