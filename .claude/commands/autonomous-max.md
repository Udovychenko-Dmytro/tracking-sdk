---
description: Maximal-autonomy task driver — one trigger builds the feature with role subagents (architect -> implementer -> tester -> internal review loop -> docs), resolves forks itself via an external multi-model consult instead of asking, then adds an external cross-vendor critic cascade (fresh Claude -> codex/GPT -> Gemini) over the full diff. The heaviest mode. Does NOT commit or finalize.
argument-hint: "<what to build + definition-of-done (DoD); optional spec/issue link>"
allowed-tools: Agent, Bash, Read, Edit, Write, Grep, Glob
---

# /autonomous-max — maximal-autonomy task driver (roles + self-resolved forks + external critic)

The heaviest mode: one trigger takes a task from spec to **functionally-done + verified** with no questions asked. It runs the full pipeline in one pass — role subagents build it, ambiguities are resolved **by an external multi-model consult, not by asking you**, and at the end an **external cross-vendor critic cascade** reviews the whole diff. This is the heavier sibling of a one-shot implement command: more roles, an internal review loop, **and** a second, different-vendor pair of eyes.

This command is **self-contained** — the target repo has no sibling workflow commands to delegate to, so the role pipeline and the consult/critic mechanics are inlined here (and kept faithful to the gold-standard `self-review.md`).

**It never asks you mid-run.** Once started, every debatable technical/design fork — API shape, data contract, payload encoding, algorithm choice, code structure, edge-case handling, naming, a "stuck" state — is resolved by the **external multi-model consult** (Step 0 / Step 1), not by `AskUserQuestion`. The only things that stop the run and return to you are listed under "Boundaries" below.

**Boundaries (absolute — autonomy does NOT lift these):**

- **Commits / push — never.** Per the **Commits rule**: commit or push only when you explicitly ask; on `main`, branch first. This command never commits — it drives to "done", leaves the resolved tree, and reports. **It does not commit and does not finalize.**
- **Finalization is not part of this command.** Stop at "functionally done + verified" and report; the finalization pass (`/finalize`, with its "ready to finalize?" gate) is yours to run.
- **Locked-decision gate is yours.** If a fork collides with a documented deliberate decision in **`DESIGN.md`** (async batch-delivery semantics, drop policy, at-least-once + idempotency, `HttpClient` vs `UnityWebRequest`, the DI seams), the task's stated intent in **`TASK_PROGRESS.md`**, or an established codebase convention, the consult does **not** override it: record a recommendation + "needs your decision" in `TASK_PROGRESS.md` and a one-liner in [`WARNINGS.md`](../../KnowledgeBase/Documentation/WARNINGS.md), do the unblocked work, and surface it in the final report.

**Forks → external consult, not to you (at every stage, especially BEFORE the critic):**

Any debatable technical/design fork at **any** stage — preflight, planning, implementation (i.e. **before** the external critic) and when triaging its findings — is resolved by the external multi-model consult (Step 1 mechanics), **not** by asking you. That covers API shape, data contract, payload encoding, approach/algorithm choice, code structure, edge-case handling, naming, and the "stuck" state. Synthesize the answers → write a `## Decisions (consult)` block in `TASK_PROGRESS.md` → proceed on the decision.

**Back to you (not the consult) — only:** an empty spec (`$ARGUMENTS` empty), resolving a partial leftover from a prior run (continue/restart — that is about *your own* prior work, which the consult can't adjudicate), and a collision with the **Commits rule**, a `DESIGN.md` locked decision, or the locked-decision gate above. Everything else goes to the consult, never to you.

> **⚠ Cost — the most expensive mode.** Per run: a role pipeline (5+ subagents in sequence) + every significant fork → consult (multiple CLIs × minutes) + an internal review loop (≤ `MAX_REVIEW_ROUNDS=3`) + the final external critic (fresh Claude + codex up to 600 s + Gemini up to 540 s). Realistically **30–60+ minutes** and a lot of tokens. The double review (internal Claude **and** an external vendor) is deliberate, for maximum quality. Choose this mode on purpose; for a well-understood feature a plain implement-then-`/self-review` pass is cheaper.

The command argument (`$ARGUMENTS`) is the task description + definition-of-done (DoD); optional spec/issue link. Empty → that's not a fork, it's a missing spec: ask once, in one sentence, for the task + DoD and **stop** (no consult — there's nothing to consult about).

> **Timeout & compaction:** the run is very long (role subagents, consult ×N, the critic cascade) — context will almost certainly **compact mid-run**. The only durable anchor is `TASK_PROGRESS.md` (a committed project doc — read it, never delete it). Beyond the `## Plan` with `[x]` items, the `## Decisions (consult)` block, and `## Changes`, keep a **coarse phase tracker** in a `## Checkpoint (autonomous-max)` section: which of the 4 phases (Step 0/1/2/3) you're in. **Update it on EVERY step transition.** After any context compaction, first re-read `TASK_PROGRESS.md` (checkpoint + `## Plan`) and resume from the right phase — **do not restart the build (Step 1) if it is already marked done.**

> **External-CLI timeouts:** every external-CLI run (consult, codex, agy, the fresh Claude reviewer) takes minutes. Set a large Bash timeout (up to `600000` ms); the Bash tool will usually **auto-background** the long run — wait for the background-completion notification rather than polling. Report files stay 0 bytes until the process exits.

---

## Step 0 — Preflight: external CLIs + artifacts dir

Order matters: pin the autonomous posture and probe the CLIs **before** the expensive run.

1. This command is an explicit trigger for **fully-autonomous** mode: from here on, a significant fork → consult, not a question to you.
2. Parse `$ARGUMENTS` into **Goal** (what to build) and **DoD** (done criterion). Empty → ask once for the spec and stop (see above). A spec/issue link is read by the architect subagent in Step 1.
3. Probe the external CLIs: the consult panel (for forks) and `codex`+`agy` (for the external critic). **Degradation is allowed — there is no hard stop here.** Always operate from the repo root so paths resolve from the git root, not the Unity subfolder.

   ```bash
   cd "$(git rev-parse --show-toplevel)"
   ADIR=".claude/artifacts/autonomous-max"
   mkdir -p "$ADIR"
   [ -f .claude/artifacts/.gitignore ] || printf '*\n' > .claude/artifacts/.gitignore

   # Resolve the standalone `claude` CLI (macOS); do not rely on bare `claude` being on the spawned PATH.
   CLAUDE_BIN="$(command -v claude 2>/dev/null || true)"
   if [ -z "$CLAUDE_BIN" ]; then
     for cand in "$HOME/.local/bin/claude" "$HOME/.claude/local/claude" \
                 /opt/homebrew/bin/claude /usr/local/bin/claude \
                 "$(npm config get prefix 2>/dev/null)/bin/claude"; do
       [ -n "$cand" ] && [ -f "$cand" ] && { CLAUDE_BIN="$cand"; break; }
     done
   fi

   HAVE_GPT=0; HAVE_GEM=0; HAVE_CLAUDE=0
   command -v codex >/dev/null 2>&1 && HAVE_GPT=1
   [ -n "$CLAUDE_BIN" ] && HAVE_CLAUDE=1
   if command -v agy >/dev/null 2>&1 && agy models 2>/dev/null | grep -qx "Gemini 3.1 Pro (High)"; then HAVE_GEM=1; fi

   CRITIC_OK=0; [ "$HAVE_GPT" = 1 ] && [ "$HAVE_GEM" = 1 ] && CRITIC_OK=1   # external critic (Step 2) needs BOTH
   PANEL_N=$((HAVE_GPT + HAVE_GEM + HAVE_CLAUDE))                          # consult works with >=1 model

   BRANCH=$(git rev-parse --abbrev-ref HEAD 2>/dev/null || echo "(no-git)")
   printf 'HAVE_GPT=%s\nHAVE_GEM=%s\nHAVE_CLAUDE=%s\nCRITIC_OK=%s\nPANEL_N=%s\nBRANCH=%q\nCLAUDE_BIN=%q\n' \
     "$HAVE_GPT" "$HAVE_GEM" "$HAVE_CLAUDE" "$CRITIC_OK" "$PANEL_N" "$BRANCH" "$CLAUDE_BIN" > "$ADIR/scope.env"
   echo "panel(consult)=$PANEL_N/3  external-critic(codex+agy)=$CRITIC_OK  branch=$BRANCH"
   [ "$PANEL_N" -eq 0 ]  && echo "WARN: consult panel unavailable — forks fall back to the safe-default rule below."
   [ "$CRITIC_OK" -eq 0 ] && echo "WARN: external critic (Step 2) will be SKIPPED — needs both codex and agy ('Gemini 3.1 Pro (High)')."
   ```

   - `PANEL_N=0` → the consult panel is unavailable: resolve autonomous forks by the **safe-default rule** (cheap/reversible → safest default, log the reason; costly-to-reverse with no safe default → stop that branch and bring it back to you). Not a whole-command stop.
   - `CRITIC_OK=0` → the external critic (Step 2) is skipped; the role pipeline + internal review still yields verified code. Note it loudly in the final report.
   - If you need a **guaranteed** external pass and `CRITIC_OK=0`: stop and ask the user to bring up `codex` / sign into Antigravity (`agy`), then re-run.

4. Resolve the base ref for the eventual full-diff critic (replaces a hardcoded branch name):

   ```bash
   cd "$(git rev-parse --show-toplevel)"
   BASE=""
   if git rev-parse --verify origin/main >/dev/null 2>&1 && ! git merge-base --is-ancestor HEAD origin/main; then
     BASE=origin/main
   else
     BASE="$(git rev-list --max-parents=0 HEAD | tail -1)"   # initial commit -> whole SDK
   fi
   printf 'BASE=%q\n' "$BASE" >> ".claude/artifacts/autonomous-max/scope.env"
   echo "base=$BASE"
   ```

---

## Step 1 — Build with role subagents (autonomous)

Drive the implementation through a role pipeline using the `Agent` tool, resolving every fork via the consult — not by asking you. This covers the build: roles + autonomous decisions + consult.

1. **Leftover check first** (before any code edits). If a prior `autonomous-max` run left a partial `TASK_PROGRESS.md` (a `## Checkpoint (autonomous-max)` with `[x]/[~]` marks), this is the one thing you bring back to *you*: continue or restart? Resolve it, then proceed. Otherwise create/extend `TASK_PROGRESS.md` for this task (`## Plan`, and the checkpoint/decisions sections below).

2. **Write the mode marker** into the `TASK_PROGRESS.md` header (after the leftover resolve, so it doesn't land in a stale leftover file):

   ```markdown
   **Mode:** autonomous-max — role pipeline (architect/implementer/tester/internal-review/docs) run autonomously (forks → external consult) + external critic (fresh Claude → codex/GPT → Gemini) at the end. Stops at "done"; finalization and commit are the user's.
   ```

   And seed the coarse phase tracker:

   ```markdown
   ## Checkpoint (autonomous-max)

   - [x] Step 0 — preflight (PANEL_N=<n>, CRITIC_OK=<0|1> from scope.env)
   - [~] Step 1 — build via role subagents (roles + internal review)
   - [ ] Step 2 — external critic (fresh Claude → codex/GPT → Gemini)
   - [ ] Step 3 — stop at "done" + combined report
   ```

   Legend: `[x]` done · `[~]` in progress · `[ ]` not started. This sits above `## Plan` (granular steps) — the checkpoint says *which phase*, `## Plan` says *which step within it*.

3. **Run the role pipeline** via the `Agent` tool (`subagent_type: "general-purpose"` or `"Plan"` for the architect), each role reading `CLAUDE.md` + [`KnowledgeBase/BehaviourRules/CODING_STANDARDS.md`](../../KnowledgeBase/BehaviourRules/CODING_STANDARDS.md) first:
   - **Architect** — read the spec/issue link and `DESIGN.md`; decompose into a `## Plan` with `[ ]` items; flag forks for the consult, don't resolve them by fiat.
   - **Implementer** — write the SDK code under `tracking-sdk/Packages/com.dmytroudovychenko.tracking/Runtime` (namespace `DmytroUdovychenko.Tracking`); no placeholders/stubs; match the surrounding code's style.
   - **Tester** — add deterministic EditMode tests under `…/Tests/Editor` using the DI seams/fakes (`ITransport`, `IEventStore`, `IClock`, `IDelayer`, `IConnectivity`) and the virtual clock; any real-network test must be `[Category("Live")]`.
   - **Internal reviewer** — review + auto-fix loop (≤ `MAX_REVIEW_ROUNDS=3`): triage findings, apply accepted ones, re-run the gate; a documented deliberate decision wins over a finding.
   - **Docs** — sync docs ↔ code per the **Sync-docs rule**: the symbol/path you touched across `README.md`, `DESIGN.md`, `TASK_PROGRESS.md`, package `README.md`/`CHANGELOG.md`, the package `Documentation~/` — including the **test count**, which appears in several files.

4. **Hot coding rules** (enforce in every role; full list in `CODING_STANDARDS.md`): `m_camelCase` private fields, `DmytroUdovychenko.Tracking` namespaces, `UPPER_SNAKE_CASE` constants, explicit enum indices, `sealed` concrete classes, Allman braces / no `var`, DI via optional ctor params with production-default fallbacks, error isolation (the public API never throws into game code), log through `ITrackingLogger` (never `Debug.*` in runtime SDK code), `ConfigureAwait(false)` on library awaits, `TryGet`+`out` over returning `null`, tunables on `TrackingConfig` (no magic values).

5. **Edge-case walk** (the implementer + tester confirm each is handled or surfaced — the public API is tiny: `ITracker.SendMessage` / `SendMapAsync`): `SendMessage(null)` / empty map / `Result == null`; thread-safety (the bounded queue hit from many threads while the worker drains); `TaskCompletionSource` completed on **every** path (delivered / retries-exhausted / evicted / rejected / purged / cancelled — a never-completed `Task` is a hang); cancellation + `Dispose` (worker stops clean, `HttpClient`/`SemaphoreSlim` released); offline hold → flush; overflow policy (`DropOldest` vs `RejectNew`); at-least-once + idempotency (stable event id reused on retries); corrupt/missing persisted file; `JsonUtility` payload round-trip. Report edge cases even when out of scope (one-line FYI + add to `WARNINGS.md`).

6. **Consult mechanic for any fork.** When a role hits a debatable fork, run the inlined multi-model consult instead of asking you (degrade per `PANEL_N`). Write the question + each model's answer to `$ADIR/consult-<n>.md`, synthesize, and record the decision in the `## Decisions (consult)` block of `TASK_PROGRESS.md`. The same CLI mechanics as the critic (Step 2) apply — read-only, `< /dev/null`, large timeout:

   ```bash
   cd "$(git rev-parse --show-toplevel)"; ADIR=".claude/artifacts/autonomous-max"; source "$ADIR/scope.env"
   Q="<the fork, with enough SDK context to answer cold>"; N=1
   [ "$HAVE_GPT" = 1 ]    && codex exec -s read-only -C "$(pwd)" -o "$ADIR/consult-$N.gpt.md" "$Q" < /dev/null > "$ADIR/consult-$N.gpt.log" 2>&1   # codex 0.135 reads stdin even with a prompt arg and hangs on EOF — `< /dev/null` is MANDATORY.
   [ "$HAVE_GEM" = 1 ]    && agy --model "Gemini 3.1 Pro (High)" --sandbox --print-timeout 540s --print "$Q" > "$ADIR/consult-$N.gem.md" 2> "$ADIR/consult-$N.gem.log" < /dev/null
   [ "$HAVE_CLAUDE" = 1 ] && "$CLAUDE_BIN" -p --model claude-opus-4-8 --effort max --permission-mode plan "$Q" > "$ADIR/consult-$N.claude.md" 2> "$ADIR/consult-$N.claude.log" < /dev/null
   ```

7. **Gate the build** (the **Build & Verify** rule). Run the headless Unity EditMode test suite **only when compile-affecting files changed** (`.cs`/`.asmdef`/`.asmref`); skip it for docs/PHP/asset-only diffs. **The Unity Editor must be closed** (single-instance lock). Large timeout.

   ```bash
   cd "$(git rev-parse --show-toplevel)"
   ADIR=".claude/artifacts/autonomous-max"; RDIR="$ADIR"
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

   Green = `failed="0"` with a non-zero `passed` in the `<test-run>` line (the suite is currently **145 deterministic tests + 2 `[Category("Live")]` live tests** that run in the default headless suite and need network). Any failure or compile error → fix to green before Step 2. Report failures honestly — never silently fix.

8. **Checkpoint:** the whole build is done (roles complete, internal review done, the gate green) → mark `[x] Step 1` and `[~] Step 2` in `## Checkpoint (autonomous-max)`. That mark is what lets a post-compaction re-run skip straight to the critic instead of restarting the build.

---

## Step 2 — External cross-vendor critic (the second pair of eyes)

After Step 1 the working tree holds the full uncommitted implementation that already passed the **internal** review loop. Now add an **external**, different-vendor review — a fresh Claude **and** a cross-vendor cascade catch different things than the internal Claude that just wrote the code.

1. **Skip:** `CRITIC_OK=0` from Step 0 (no `codex` and/or `agy`) → skip this step, note "external critic skipped (no codex/agy)" in the report, go to Step 3. Don't block finished, verified work. (The fresh-Claude pass below still runs if `HAVE_CLAUDE=1`.)
2. **Assemble the diff under review** = the full accumulated implementation (committed `BASE...HEAD` + the uncommitted working tree). `git add -A -N` makes untracked files visible to `git diff`; `git reset -q` restores afterward:

   ```bash
   cd "$(git rev-parse --show-toplevel)"; ADIR=".claude/artifacts/autonomous-max"; source "$ADIR/scope.env"
   git add -A -N >/dev/null 2>&1
   {
     echo "# Critic context (full diff: $BASE...HEAD + working tree + untracked)"
     echo "## TASK_PROGRESS.md (intent / plan / decisions)"; echo '```markdown'; cat TASK_PROGRESS.md 2>/dev/null; echo '```'
     echo; echo "## Committed diff ($BASE...HEAD)"; echo '```diff'; git --no-pager diff "$BASE"...HEAD; echo '```'
     echo; echo "## Working tree + untracked (vs HEAD)"; echo '```diff'; git --no-pager diff HEAD; echo '```'
   } > "$ADIR/context.md"
   git reset -q >/dev/null 2>&1
   echo "context bytes: $(wc -c < "$ADIR/context.md")"
   ```

3. **Guard the read-only reviewers** (none may touch the tree). `codex -s read-only` and the fresh Claude's `--permission-mode plan` are read-only by construction; `agy` has **no** hard read-only mode, so fingerprint the tree before/after and halt + report if it changed:

   ```bash
   fp() { { git --no-pager diff HEAD; git status --porcelain; } | shasum | cut -d' ' -f1; }
   ```

4. **The review concern list** the critic prompt must carry (verbatim — these are the SDK's real risk areas):

   > bugs/logic errors; CONCURRENCY (the bounded queue is hit from any thread and drained by a background worker — lock correctness, no torn state, no unobserved `Task` exceptions, `ConfigureAwait(false)` on library awaits, `TaskCompletionSource` completed on EVERY path: delivered / retries-exhausted / evicted / rejected / purged / cancelled, no deadlock from blocking on async, no main-thread assumption on the worker — `UnityWebRequest` is main-thread-only, `HttpClient` is deliberate); ASYNC + cancellation/`Dispose` (token honored, worker stops clean, `HttpClient`/`SemaphoreSlim` disposed); ERROR ISOLATION (the public API never throws into game code — validate, swallow+log via the logger seam); TEST DETERMINISM (no real network/disk/wall-clock in the default suite — DI seams/fakes `ITransport`/`IEventStore`/`IClock`/`IDelayer`/`IConnectivity` + a virtual clock; live tests are `[Category("Live")]`); SERIALIZATION (`JsonUtility` can't do `Dictionary<string,object>` → the type-tagged payload encoding; round-trip + corrupt/missing-file resilience); at-least-once + idempotency (stable event id reused on retries); bounded-memory drop policy (`DropOldest` vs `RejectNew`); config defaults on `TrackingConfig`; public API surface vs docs/CHANGELOG; naming; dead code; concise comments.

5. **Run the cascade** (each read-only, `< /dev/null`, large timeout). Fresh Claude first (the same gold-standard mechanic as `/self-review`), then codex/GPT, then Gemini re-checking GPT's fixes and hunting for what it missed:

   ```bash
   cd "$(git rev-parse --show-toplevel)"; ADIR=".claude/artifacts/autonomous-max"; source "$ADIR/scope.env"
   PROMPT="You are a senior C#/Unity SDK reviewer with clean context. Full diff + task log: $ADIR/context.md (read it). This is a production-grade in-process event-tracking SDK for Unity. Review IN-SCOPE diff lines only; report '[SEV] file:line — problem. Fix: ...' (SEV in BLOCKER/MAJOR/MINOR/NIT). Concern list: <paste the verbatim list above>. Read-only: do NOT edit files, do NOT run mutating commands, do NOT call ExitPlanMode. Your final text IS the report."

   BEFORE=$(fp)
   [ "$HAVE_CLAUDE" = 1 ] && "$CLAUDE_BIN" -p --model claude-opus-4-8 --effort max --permission-mode plan "$PROMPT" > "$ADIR/claude-review.md" 2> "$ADIR/claude.log" < /dev/null
   [ "$HAVE_GPT" = 1 ]    && codex exec -s read-only -C "$(pwd)" -o "$ADIR/gpt-review.md" "$PROMPT" < /dev/null > "$ADIR/gpt.log" 2>&1   # codex 0.135 reads stdin even with a prompt arg and hangs on EOF — `< /dev/null` is MANDATORY.
   # ...triage + fix GPT's accepted findings, re-run the Step-1 gate, then Gemini re-checks the fixes:
   [ "$HAVE_GEM" = 1 ]    && agy --model "Gemini 3.1 Pro (High)" --sandbox --print-timeout 540s --print "$PROMPT Re-check the fixes just applied and find anything missed." > "$ADIR/gemini-review.md" 2> "$ADIR/gemini.log" < /dev/null
   AFTER=$(fp)
   [ "$BEFORE" != "$AFTER" ] && echo "HALT: a read-only reviewer (likely agy) modified the working tree — inspect before continuing."
   ```

6. **You are the developer in this loop, not a re-implementer.** Triage each finding (accept/reject), priority BLOCKER → MAJOR → MINOR → NIT. Reject anything that conflicts with a documented deliberate decision (`DESIGN.md`), the task's stated intent (`TASK_PROGRESS.md`), or an established convention — record the reason. Apply accepted fixes, re-run the Step-1 gate to green. Don't re-implement the task from scratch.
7. **Checkpoint:** critic done (or skipped per `CRITIC_OK=0`) and the gate green → mark `[x] Step 2` and `[~] Step 3` in `## Checkpoint (autonomous-max)`.

---

## Step 3 — Stop at "done" + combined report (do NOT finalize, do NOT commit)

All plan items closed, DoD met, the gate green:

- **Stop.** Finalization (`/finalize`) and the commit are not run here — they are yours. Mark `[x] Step 3` in `## Checkpoint (autonomous-max)` — all 4 phases closed, task functionally done and verified.
- Anything deferred behind the locked-decision gate or carried as an assumption is already in `TASK_PROGRESS.md` / `WARNINGS.md`.

Print a `## Autonomous-max summary` section:

- **Goal / DoD** — 1–2 lines, met or not.
- **Roles** — what each subagent produced; what you kept / reverted / redid and why.
- **Internal review loop** — rounds out of `MAX_REVIEW_ROUNDS`; findings by severity, accepted/rejected; anything left open at the cap.
- **Forks → consult** — what went to the external consult and the decisions taken (link to `## Decisions (consult)`); what you settled yourself as trivial; the fallback used if the panel was unavailable.
- **External critic** (fresh Claude → codex/GPT → Gemini) — findings by severity, accepted/rejected, what changed (`file:line`); **or** "skipped — no codex/agy".
- **Assumptions / defaults** — product/taste calls awaiting your confirmation.
- **Locked decisions / boundaries** — what was deferred behind a `DESIGN.md`/`TASK_PROGRESS.md` locked decision with "needs your decision", with WARNINGS refs.
- **Verification** — EditMode test result (e.g. `total=73 passed=71 skipped=2`) — honest; or "skipped — docs/php-only".
- **TASK_PROGRESS.md** — `## Plan` all `[x]`, `## Changes` / `## Decisions (consult)` filled in.
- Reminder: **nothing committed and nothing finalized** — the finalization pass (`/finalize`, "ready to finalize?" gate) and the commit are yours; on `main`, branch first.

Artifacts live in `$ADIR` (`.claude/artifacts/autonomous-max`, gitignored, transient — overwritten by the next run): `scope.env`, `context.md`, `consult-*.md`, `claude-review.md`, `gpt-review.md`, `gemini-review.md`, logs. Durable records live in `TASK_PROGRESS.md` and `KnowledgeBase/`.
