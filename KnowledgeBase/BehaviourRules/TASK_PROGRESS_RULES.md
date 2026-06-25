# TASK_PROGRESS.md — maintenance guide

How to maintain the project progress log. The **trigger** lives in [CLAUDE.md](../../CLAUDE.md) Work Rules ("TASK_PROGRESS.md" bullet); this file holds the detailed format + lifecycle.

> ⚠️ **This project is different from the generic template this rule was adapted from.** Here `TASK_PROGRESS.md` is a **committed project doc that is kept in the repo, never deleted** — one of the AI-workflow `.md` files maintained alongside the SDK. It is **NOT** gitignored, **NOT** ephemeral, and is **NEVER deleted**. Do not import the template behaviour of "create on first edit, gitignore, delete at finalization." Compare [`.claude/commands/finalize.md`](../../.claude/commands/finalize.md) step 8, which is the source of truth: *"Do NOT delete it — in this project it is a committed project doc, not gitignored. Just ensure it reflects the final state."*

## Location

`TASK_PROGRESS.md` lives under **`KnowledgeBase/Documentation/`** (`<repo>/KnowledgeBase/Documentation/TASK_PROGRESS.md`), alongside `DESIGN.md` and `BUSINESS_LOGIC.md` (the git-root `README.md` stays at the root). The Unity project is the subfolder `tracking-sdk/`; the progress log is **not** inside it. Plan of record: the original pre-implementation plan is preserved as an appendix inside `TASK_PROGRESS.md` itself.

## Purpose

A living, committed progress log for the SDK build. It records the phased roadmap, what each phase delivered, and the running test count, and it is the primary source material for the PR/MR description (see [FINAL_REPORT_RULES.md](FINAL_REPORT_RULES.md)).

## Lifecycle

1. **It already exists — never recreate or wipe it.** At the start of a task, **read** `TASK_PROGRESS.md` for context (current phase, what's done, open items). Do not delete it, do not start a fresh one, do not ask whether to "continue or start fresh" — it is permanent project state.
2. **Plan before coding** — for a non-trivial change, capture the plan as numbered steps (in the relevant `## Plan` / phase section, or in your turn). Keep steps concrete and small enough to mark done individually.
3. **Update as you go** — when a task or phase advances, update the matching section: mark steps done, record what changed and why, and **update the test count** (it appears in several files — keep them consistent; see [FINALIZATION_RULES.md](FINALIZATION_RULES.md) step 2).
4. **Keep it honest** — if work is reverted or superseded, update or strike the entry so the log always matches the actual repo state. Update **Last updated** at the top.
5. **Present at finalization** — print its current contents and confirm it reflects the final state (phases, test counts, status). It is committed **with** the rest of the change — it is part of the project, not a throwaway.

## Format

The existing file already establishes the structure (snapshot table by phase + per-phase detail). When extending it, keep it scannable and consistent with what's there. A typical phase/task entry:

```markdown
**Last updated:** <date> — <one-line state, e.g. "Phase N done. NN tests green.">

## <Phase / Task title>  (✅ Done | 🟡 In progress | ⬜ Not started)
**Goal:** <1-2 sentences — what and why.>

### Plan
1. [x] <step done>
2. [ ] <step pending>

### Changes
- **`<file path>`** — <what changed> (<why>).

### Verification
- EditMode tests: `total=NN passed=NN skipped=N` (headless run). <or "skipped — docs/php-only">.

### Pending / out of scope
- <deferred follow-ups; mirror durable items to [../Documentation/WARNINGS.md](../Documentation/WARNINGS.md)>.
```

Rules:
- **Changes** — one entry per logical change, referencing the **`file path`**. Note **why**, not just what — the why drives the MR/PR summary.
- **Verification** — record the headless EditMode test result here (per the [CLAUDE.md](../../CLAUDE.md) "Build & Verify" Work Rule) so finalization can confirm it ran.
- **Pending / out of scope** — mirror durable, cross-session items to [../Documentation/WARNINGS.md](../Documentation/WARNINGS.md) too.
- **No submodules** — this is a single repo (git root = repo root, Unity project in `tracking-sdk/`). There is no submodule commit ordering to track.
