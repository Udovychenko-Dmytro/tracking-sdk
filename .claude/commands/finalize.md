---
description: Pre-release finalization pass — gate, then an 8-step checklist (stray diagnostics, docs<->code sync, git completeness, simplify, trim comments, edge cases, final report, TASK_PROGRESS.md). Reports actions; does not commit.
argument-hint: "(no argument — finalizes the current work)"
allowed-tools: AskUserQuestion, Read, Edit, Write, Grep, Glob, Bash
---

# /finalize — pre-release finalization pass

A self-check of **your own** work this session before release: the diff you wrote, the diagnostics you added, the docs you maintained. This runs **in-context** — it is not a fresh-eyes review (for that, use `/self-review`). It **does not commit** — it reports the checklist; you commit.

This command takes **no argument**.

---

## Step 0 — Gate (ask before running the checklist)

Call `AskUserQuestion`:
- **Question:** "Work is in — does everything look right? Ready to finalize for release?"
- **Options:** "Yes, finalize" / "No, not yet"

Proceed only if the user picks **"Yes, finalize"** (or clearly confirms). **If "No" / non-committal:** close the widget and **stop** — write nothing, don't summarize, don't ask a follow-up. Wait for the user.

---

## Checklist — walk through in order

> Do mechanical fixes inline (remove stray diagnostics, trim comments, simplify). For anything needing a judgment call, report it and ask rather than acting.

1. **Stray diagnostics** — Grep the working tree for `[ClaudeTest]`, `TODO(claude)`, commented-out experiments, and ad-hoc `Debug.Log` added *for debugging this session*. Remove them. **Keep** the intentional logging: the demo's on-screen output, `LiveRetryTests`' `[live-retry]` metrics line, and anything routed through the `ITrackingLogger` seam — those are deliberate. When unsure whether a `Debug.Log` is intentional, list it for the user instead of deleting.

2. **Docs ↔ code sync** — For every symbol / file path / **test count** you touched, grep the docs and fix anything stale: `README.md`, `DESIGN.md`, `TASK_PROGRESS.md`, the package `README.md` + `CHANGELOG.md`, and the package `Documentation~/`. Common drift: a renamed public type, a changed default, an out-of-date test total (the suite count appears in several files), a new config field not mentioned in the scorecard. Add a one-line note for any new non-obvious convention.

3. **Git completeness** — `git status` in the repo. Confirm every edited file shows up; no stray untracked files that should be ignored (e.g. `dist/*.tgz`, `.self-review/`, Unity `Library/`); no new `.cs` left without its `.meta`. List exactly what to commit. **Note:** this project's default branch is `main` — if the user is about to commit substantial new work, suggest a branch first (per the harness's branch-before-commit norm). Do **not** commit here.

4. **Simplification pass** — Re-read the diff scoped to **your own** changes for cheap cleanups: `.Where(...).Count() > 0` → `.Any(...)`, redundant locals, dead fields/methods left after iteration, collapsible conditions, expression-bodied members where clearer. Don't refactor pre-existing code unless asked.

5. **Trim comments** — Re-read comments in files you touched (yours and any you edited around): drop noise, keep them concise. Prefer ≤2 lines per comment and per `/// <summary>`. Never delete information that isn't recorded elsewhere — move it to a doc first if needed.

6. **Edge-case re-check** — Re-walk the failure modes against the **final** code: null/empty inputs (`SendMessage(null)`, empty map), thread-safety (queue hit from multiple threads while the worker drains), `TaskCompletionSource` completed on every path (delivered / retries-exhausted / evicted / rejected / purged / cancelled), cancellation + `Dispose` (worker stops, `HttpClient`/`SemaphoreSlim` released), offline/connectivity hold→flush, overflow policy (DropOldest vs RejectNew), at-least-once + idempotency, corrupt/missing persisted file. Fix or flag explicitly.

7. **Final report** — Produce three copy-paste-ready fenced blocks:
   - **PR description** — from `TASK_PROGRESS.md` (what was built, the implicit-requirements covered, test count).
   - **How to verify** — open in Unity (version from `ProjectSettings/ProjectVersion.txt`), run EditMode tests (or the headless `-runTests` command); note the `[Category("Live")]` live tests and how to run them.
   - **Commit message(s)** — concise, imperative; end with the `Co-Authored-By` trailer if the user commits via the assistant.

8. **TASK_PROGRESS.md** — Print its current contents and confirm it is up to date (phases, test counts, status). **Do NOT delete it** — in this project it is a **committed project doc** (one of the AI-workflow `.md` files kept in the repo), not gitignored. Just ensure it reflects the final state.

---

## Output format

Report as a short, scannable `## Finalization` section — one sub-bullet per checklist item, each marked `✓ clean` or `⚠ found X — action: Y`. Then ask the user whether to proceed with anything that needs a decision.

**Do not commit or stage (`git add`), even after the checklist is clean** — the user stages and runs `git commit` (and branches first if appropriate).
