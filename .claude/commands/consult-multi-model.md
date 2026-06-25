---
description: Consult three external models (GPT 5.5 / Gemini 3.1 Pro High / Claude Opus 4.8) on a stated design question/blocker to unblock the task — gather answers in parallel, synthesize the best/combined solution, record it in TASK_PROGRESS.md, and continue the work.
argument-hint: "[problem/blocker description] (optional; otherwise inferred from the current context and TASK_PROGRESS.md)"
allowed-tools: Bash, Read, Edit, Write, Grep, Glob
---

# /consult-multi-model — three-model advice (GPT 5.5 ‖ Gemini 3.1 Pro ‖ Claude Opus 4.8)

You are stuck, or you are taking a decision that determines how to proceed on the task. This command **formulates the questions, consults three external models on them in parallel, gathers the answers, synthesizes the best (or combined) solution, records it in `TASK_PROGRESS.md`, and continues the implementation along that decision.**

This is **not** a diff review (use the `/self-review*` family for that). Here you are the lead engineer: you state the problem and the concrete questions, three consultants with clean context answer independently, you fold their answers into one decision and move on.

Why three different vendors/families: each has its own blind spots. A consensus of three = high confidence; divergence = a signal that the fork is real and must be reasoned through deliberately.

**Mode — fully autonomous:** the questions are formulated and the consult is launched **without pausing for confirmation**. The formulated questions are printed to the user for transparency, but the command does not wait on them.

**No commits at any step** — per the **Commits rule**: commit or push only when the user explicitly asks; on `main`, branch first. This command never commits — it reports and leaves the tree for the user. **All three consultants are strictly read-only:** GPT/Codex — hard sandbox (`-s read-only`); Claude — plan mode (`--permission-mode plan`); Gemini/`agy` has no hard read-only mode (only `--sandbox`), so it is wrapped in `--sandbox` + an edit ban in the prompt + a **before/after working-tree fingerprint** (touched the tree → stop and report).

The command argument (`$ARGUMENTS`) is an optional problem/blocker description. Empty → the problem is inferred from the current dialog and `TASK_PROGRESS.md`.

> **Timeout:** the three models run in parallel, wall-clock ≈ the slowest one (a few minutes). When launching the consult block via the Bash tool, set the **maximum timeout `600000` ms** (the Bash tool will usually auto-background the run). `--print-timeout 540s` keeps the Gemini limit just under the tool ceiling. Parallel execution is chosen deliberately to fit under that ceiling (running 3× sequentially would blow past it).

---

## Step 0 — Setup, CLI check, and determining the available consultants

1. Check which consultants are available. Unlike the review family, **degradation is acceptable** here: consulting 2 of 3 models is still useful. Hard stop only if **zero** are available.

   ```bash
   cd "$(git rev-parse --show-toplevel)"
   ADIR=".claude/artifacts/consult-multi-model"
   mkdir -p "$ADIR" && { [ -f .claude/artifacts/.gitignore ] || printf '*\n' > .claude/artifacts/.gitignore; }

   HAVE_GPT=0; HAVE_GEM=0; HAVE_CLAUDE=0
   command -v codex >/dev/null 2>&1 && HAVE_GPT=1 || echo "WARN: codex (Codex CLI) not found — skipping the GPT consultant"

   CLAUDE_BIN="$(command -v claude 2>/dev/null || true)"
   if [ -z "$CLAUDE_BIN" ]; then
     for cand in "$HOME/.local/bin/claude" "$HOME/.claude/local/claude" \
                 /opt/homebrew/bin/claude /usr/local/bin/claude \
                 "$(npm config get prefix 2>/dev/null)/bin/claude"; do
       [ -n "$cand" ] && [ -f "$cand" ] && { CLAUDE_BIN="$cand"; break; }
     done
   fi
   [ -n "$CLAUDE_BIN" ] && HAVE_CLAUDE=1 || echo "WARN: claude CLI not found — skipping the Claude consultant"

   if command -v agy >/dev/null 2>&1 && agy models 2>/dev/null | grep -qx "Gemini 3.1 Pro (High)"; then
     HAVE_GEM=1
   else
     echo "WARN: agy/'Gemini 3.1 Pro (High)' unavailable (not on PATH or not logged into Antigravity) — skipping the Gemini consultant"
   fi

   BRANCH=$(git rev-parse --abbrev-ref HEAD 2>/dev/null || echo "(no-git)")
   printf 'HAVE_GPT=%s\nHAVE_GEM=%s\nHAVE_CLAUDE=%s\nBRANCH=%q\nCLAUDE_BIN=%q\n' \
     "$HAVE_GPT" "$HAVE_GEM" "$HAVE_CLAUDE" "$BRANCH" "$CLAUDE_BIN" > "$ADIR/scope.env"
   echo "consultants: GPT=$HAVE_GPT Gemini=$HAVE_GEM Claude=$HAVE_CLAUDE  branch=$BRANCH"
   [ $((HAVE_GPT + HAVE_GEM + HAVE_CLAUDE)) -eq 0 ] && echo "FATAL: no consultant available — stop and tell the user"
   ```

   `FATAL:` — tell the user and **stop**. Otherwise record who is available and continue (you will explicitly note the missing ones in the final report).

2. **Define the problem.** Source, by priority:
   - `$ARGUMENTS` non-empty → that is the problem statement (substitute it literally from the command argument).
   - Empty → infer the current blocker from the **last turn of the dialog** and `TASK_PROGRESS.md` (if present): where you got stuck, which fork is unresolved.
   - No clear problem in either → there is nothing to consult about: tell the user and **stop** (ask them to describe the blocker in one sentence).

---

## Step 1 — Formulate the problem and questions → `context.md`

Turn the problem into a **statement + a numbered list of concrete fork-questions** (the ones you need answered RIGHT NOW to proceed). Good questions are closed / choices between options, not "what's best in general". Assemble everything the consultant needs into `$ADIR/context.md`:

````bash
ADIR=".claude/artifacts/consult-multi-model"; source "$ADIR/scope.env"
{
  echo "# Consult context"
  echo "- branch: $BRANCH"
  echo
  echo "## Problem"
  echo "<2-5 sentences: what the task is, where you got stuck, what you already tried and rejected>"
  echo
  echo "## Questions (each needs an answer)"
  echo "1. <concrete fork-question; with options A/B/C where possible>"
  echo "2. <…>"
  echo
  echo "## Relevant code / files"
  echo "- \`Packages/com.dmytroudovychenko.tracking/Runtime/<file>.cs:ClassName.Method\` — why it relates"
  echo
  echo "## Project constraints (factor into the answer)"
  echo "- Deliberate decisions live in DESIGN.md (async batch-delivery semantics, drop policy, at-least-once + idempotency, HttpClient vs UnityWebRequest, the DI seams); intent in TASK_PROGRESS.md; coding standards in KnowledgeBase/BehaviourRules/CODING_STANDARDS.md."
  echo
  echo "## Current working-tree state"
  echo '```'; git --no-pager status --short 2>/dev/null; echo '```'
  echo '```diff'; git --no-pager diff 2>/dev/null; echo '```'
} > "$ADIR/context.md"
echo "context assembled: $ADIR/context.md"
````

**Write the real content directly into the block above BEFORE running it** — replace each `<…>` and the example `Packages/com.dmytroudovychenko.tracking/Runtime/<file>.cs:ClassName.Method` with the actual problem, questions, and paths, so `context.md` is born ready rather than a fill-in-later template (otherwise, on a failure/compaction between writing and filling, the consultants get garbage context). After writing, confirm no placeholders remain:

```bash
grep -nE '<…>|<\.\.\.>|<file>\.cs:ClassName\.Method' "$ADIR/context.md" && echo '‼ placeholders remain — context.md is NOT ready, do not launch the consult (Step 2)' || echo '✓ context.md ready'
```

Then **print to the user** the formulated problem and the list of questions (transparency) and **proceed straight to Step 2 — without pausing** (the command is autonomous).

---

## Step 2 — Parallel consult of the three models

One bash block launches the available consultants **in parallel** (background + `wait`); each gets the same prompt pointing at `context.md`. **Set the tool timeout to `600000` ms** (the Bash tool will usually auto-background the run). The tree fingerprint is checked before/after (codex/claude physically cannot write; agy is guarded).

```bash
ADIR=".claude/artifacts/consult-multi-model"; source "$ADIR/scope.env"
fp() { { git --no-pager diff HEAD 2>/dev/null; git status --porcelain 2>/dev/null; } | shasum | cut -d' ' -f1; }
TREE_BEFORE=$(fp)

PROMPT="The task context and questions are in $ADIR/context.md (branch $BRANCH).
$(cat <<'BODY'
You are a senior consulting engineer with clean context. The dev team has hit a fork in the task and wants your answer so they can proceed. You are being consulted in parallel with two other models from different families — answer independently and on the merits; do not assume their answers.

First read the project rules: `CLAUDE.md` and `KnowledgeBase/BehaviourRules/CODING_STANDARDS.md` — the solution MUST fit a documented deliberate decision in `DESIGN.md` (async batch-delivery semantics, drop policy, at-least-once + idempotency, HttpClient vs UnityWebRequest, the DI seams), the task's stated intent in `TASK_PROGRESS.md`, or an established codebase convention. Then read the `context.md` referenced above and the relevant files it lists (file reads, `git status` / `git diff`).

This is a production-grade in-process event-tracking SDK for Unity. Tiny public API (`ITracker.SendMessage` / `SendMapAsync`) over a non-blocking pipeline: bounded thread-safe queue → background batching dispatcher → pluggable transport, with retries/backoff, durable persistence, lifecycle flush, connectivity-awareness, circuit breaker, dead-letter, metrics, a logging hook, and privacy opt-out. Namespace `DmytroUdovychenko.Tracking`; runtime under `Packages/com.dmytroudovychenko.tracking/Runtime`.

Task: give a concrete, implementable-IN-THIS-stack solution and answer EVERY numbered question.

Response format — strict:
- "## Recommendation" — 2-5 sentences: which approach to take (TL;DR), directly applicable.
- "## Answers per question" — per question from context.md, by number: `N. <recommendation>. Rationale: <why>. Tradeoffs: <pros/cons, risks>.`
- "## Risks and gotchas" — what breaks / edge cases / CONCURRENCY (the bounded queue is hit from any thread and drained by a background worker: lock correctness, no torn state, no unobserved Task exceptions, ConfigureAwait(false) on library awaits, TaskCompletionSource completed on EVERY path — delivered / retries-exhausted / evicted / rejected / purged / cancelled, no deadlock from blocking on async, no main-thread assumption on the worker — UnityWebRequest is main-thread-only, HttpClient is deliberate) / async + cancellation/Dispose (token honored, worker stops clean, HttpClient/SemaphoreSlim disposed) / error isolation (the public API never throws into game code — validate, swallow+log via the logger seam) / test determinism (no real network/disk/wall-clock in the default suite — DI seams/fakes ITransport/IEventStore/IClock/IDelayer/IConnectivity + a virtual clock; live tests are in the Live category) / serialization (JsonUtility can't do Dictionary<string,object> → the type-tagged payload encoding; round-trip + corrupt/missing-file resilience) / at-least-once + idempotency (stable event id reused on retries) / bounded-memory drop policy (DropOldest vs RejectNew) — if you go this route.
- "## Rejected alternatives" — what you considered and rejected, briefly why.

If a correct answer requires changing a documented DESIGN.md decision, the task's stated intent, or an established convention — say so directly and propose a solution within the current constraints (or note that it needs an explicit decision from the user).

Constraints: READ-ONLY mode. Do NOT edit or create files, do NOT run mutating commands. Your final answer IS the text in the format above and nothing else. Be concrete: operate over the project's files/symbols/API, not abstractions.
BODY
)"

# GPT 5.5 — Codex CLI, hard read-only, report to -o. '< /dev/null' is mandatory (codex hangs on EOF, 0.135). Do not remove.
if [ "${HAVE_GPT:-0}" = 1 ]; then
  codex exec -s read-only -C "$(pwd)" -o "$ADIR/gpt.md" "$PROMPT" < /dev/null > "$ADIR/gpt.log" 2>&1 &
  PID_GPT=$!
fi
# Gemini 3.1 Pro (High) — Antigravity CLI, report to stdout.
if [ "${HAVE_GEM:-0}" = 1 ]; then
  agy --model "Gemini 3.1 Pro (High)" --sandbox --print-timeout 540s --print "$PROMPT" \
    > "$ADIR/gemini.md" 2> "$ADIR/gemini.log" < /dev/null &
  PID_GEM=$!
fi
# Claude Opus 4.8 — fresh instance, plan mode (read-only), report to stdout.
if [ "${HAVE_CLAUDE:-0}" = 1 ]; then
  "$CLAUDE_BIN" -p --model "claude-opus-4-8" --effort max --permission-mode plan "$PROMPT" \
    > "$ADIR/claude.md" 2> "$ADIR/claude.log" < /dev/null &
  PID_CLAUDE=$!
fi

[ -n "${PID_GPT:-}" ]    && { wait "$PID_GPT";    echo "GPT exit=$?    → $ADIR/gpt.md"; }
[ -n "${PID_GEM:-}" ]    && { wait "$PID_GEM";    echo "Gemini exit=$? → $ADIR/gemini.md"; }
[ -n "${PID_CLAUDE:-}" ] && { wait "$PID_CLAUDE"; echo "Claude exit=$? → $ADIR/claude.md"; }

TREE_AFTER=$(fp)
if [ "$TREE_BEFORE" != "$TREE_AFTER" ]; then
  echo "‼ WARNING: the working tree changed during the consult (most likely agy). Do NOT continue synthesis/implementation blindly — report to the user. Delta:"
  git --no-pager status --short
else
  echo "✓ working tree untouched by the consultants"
fi
for f in gpt gemini claude; do
  [ -s "$ADIR/$f.md" ] && echo "$f: answer received ($(wc -l < "$ADIR/$f.md") lines)" || echo "$f: EMPTY/skipped — see $ADIR/$f.log"
done
```

**Read** the resulting `.claude/artifacts/consult-multi-model/gpt.md`, `…/gemini.md`, `…/claude.md`. For an empty/errored answer from an available model, inspect its `.log` (common causes: Gemini — `not logged into Antigravity` / timeout; codex — rate limits), fix, and **re-run the consult for that model only**. If the "WARNING" block fired — stop and report.

---

## Step 3 — Synthesis: pick the best or combine

1. Read all received answers. Build a per-question comparison:
   - **Consensus** (they agree) → high confidence, take it.
   - **Divergence** → reason through why; pick the option that best fits the project's constraints.
2. **Project triage (absolute priority):** reject any item that conflicts with a documented `DESIGN.md` decision, the task's stated intent in `TASK_PROGRESS.md`, or an established codebase convention — even on unanimous model agreement. Record the reason for rejection.
3. Decide per question: **best single answer OR a hybrid** (best parts from different models). State the final decision concretely — in terms of files/symbols/steps, ready to implement.
4. If the chosen direction would change a documented `DESIGN.md` decision or the task's stated intent — that is **not implemented silently**: record the decision, but in Step 5 flag that it needs an **explicit decision from the user**, and do not change the protected behavior without it.

---

## Step 4 — Record the decision in `TASK_PROGRESS.md`

The decision and the raw answers must survive compaction and become the anchor for further work. Format and lifecycle: [TASK_PROGRESS_RULES.md](../../KnowledgeBase/BehaviourRules/TASK_PROGRESS_RULES.md).

1. No `TASK_PROGRESS.md` — this project ships one as a committed project doc; do not recreate it. If it is somehow absent, create a minimal one (header + `## Goal / Context` + `## Plan`) per the rules. Present — append (never overwrite).
2. Add a block under a `## Decisions (consult)` section (create the section if absent). **Write the decision text and rationale here directly** — not just as a link, since `.claude/artifacts/consult-multi-model/` is overwritten by the next run:

   ```markdown
   ## Decisions (consult)

   ### <YYYY-MM-DD> Consult: <short topic>

   - **Questions:** <one line per question>
   - **Consulted:** GPT 5.5 / Gemini 3.1 Pro (High) / Claude Opus 4.8 _(note the unavailable ones)_
   - **Consensus:** <where they agreed>
   - **Divergence:** <where they diverged and why>
   - **Decision:** <chosen/hybrid> — why (tying to DESIGN.md / TASK_PROGRESS.md intent / convention, if relevant).
   - **User decision needed:** yes/no (if yes — what exactly requires it).
   - **Raw answers:** `.claude/artifacts/consult-multi-model/gpt.md`, `…/gemini.md`, `…/claude.md` _(transient)_.
   ```

   Use the date from the system `currentDate` (do NOT call `date`).

3. Turn the decision into concrete steps and append them to the standard `## Plan` section (`[ ]`) — that is what implementation follows.
4. If the decision is durable knowledge (a new pattern/gotcha/architectural choice) — port the essence into `KnowledgeBase/**/*.md` (per the Documentation rules); `.claude/artifacts/consult-multi-model/` and `TASK_PROGRESS.md` are both more ephemeral than the KnowledgeBase.

---

## Step 5 — Move the task forward (implement the decision)

1. **If Step 3/4 flagged "user decision needed"** (changing a documented `DESIGN.md` decision or the task's stated intent) — do NOT implement the protected part silently: surface the fork to the user and propose the change. The rest (not touching the protected behavior) can be implemented.
2. Otherwise — implement the chosen solution by CLAUDE.md discipline: bottom-up (model → queue/dispatcher → transport → demo/UI), per `KnowledgeBase/BehaviourRules/CODING_STANDARDS.md` (hot rules: `m_camelCase` private fields, `DmytroUdovychenko.Tracking` namespaces, `UPPER_SNAKE_CASE` constants, explicit enum indices, `sealed` classes, Allman braces, no `var`, DI via optional ctor params, error isolation, log via `ITrackingLogger` never `Debug.*` in runtime code, `ConfigureAwait(false)`, `TryGet`+`out`), the **Edge cases** walk below, and no placeholders/stubs — match the surrounding code's style. Append `## Changes` to `TASK_PROGRESS.md` after each batch of edits and tick `[x]` in `## Plan`.
3. **Edge-case walk** (every change): null/empty inputs (`SendMessage(null)`, empty map, `Result == null`); thread-safety (queue hit from many threads while the worker drains); `TaskCompletionSource` completed on every path (delivered / retries-exhausted / evicted / rejected / purged / cancelled); cancellation + `Dispose` (worker stops clean, `HttpClient`/`SemaphoreSlim` released); offline hold → flush; overflow policy (`DropOldest` vs `RejectNew`); at-least-once + idempotency; corrupt/missing persisted file; `JsonUtility` round-trip.
4. **Verify** — run the headless Unity EditMode test run, but **only** if `.cs`/`.asmdef`/`.asmref` changed; skip for docs/PHP/asset-only diffs. The Unity Editor must be closed (single-instance lock). Set a large Bash timeout (up to `600000` ms):

   ```bash
   ADIR=".claude/artifacts/consult-multi-model"
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

   Green = `failed="0"` with non-zero `passed` (suite is currently 145 deterministic tests + 2 `[Category("Live")]` live tests that run in the default headless suite and need network). Failure — fix to green and report honestly (per the **Fix and report code problems** rule, never silently fix).

5. Genuinely deferred work (out-of-scope, needs an explicit decision, large effort) → record in `KnowledgeBase/Documentation/WARNINGS.md`; don't bury it.

---

## Step 6 — Final report to the user

Print a `## Consult summary` section:

- **Problem** — 1-2 lines.
- **Consulted** — who answered (GPT 5.5 / Gemini 3.1 Pro / Claude Opus 4.8) and who was unavailable.
- **Per question** — consensus/divergence in brief + the **chosen decision** (pick or hybrid) and why.
- **Rejected** — proposals dropped due to a `DESIGN.md` decision / task intent / convention (with the reason).
- **User decision** — needed? on what?
- **Implemented** — what was done per the decision (file:line on the key ones); or "stopped at a checkpoint — needs a user decision".
- **Verify** — EditMode test status (honestly; or SKIP if docs/php/asset-only).
- **TASK_PROGRESS.md** — updated (`## Decisions (consult)` section + `## Plan`).
- Reminder: **nothing committed** — the commit is the user's (on `main`, branch first).

Artifacts stay in `.claude/artifacts/consult-multi-model/` (gitignored): `context.md`, `gpt.md`, `gemini.md`, `claude.md`, logs. They are transient — overwritten by the next run; the durable record lives in `TASK_PROGRESS.md` (and `KnowledgeBase/`, if the knowledge is long-lived).
