# AI Commands (Claude Code slash commands)

> **Status:** experimental / testing mode. The commands below are functional but under active iteration. They represent an **ideological approach to AI-assisted software automation** — leveraging multi-model orchestration, cross-vendor review cascades, and autonomous task pipelines to raise code quality and reduce manual overhead. Expect interfaces and defaults to evolve.

Commands live in `.claude/commands/` and are invoked with `/<name>` inside a Claude Code session.

---

## Quick reference

| Command | What it does | Argument |
| ------- | ------------ | -------- |
| `/check-tests` | Runs the headless Unity EditMode test suite, reports pass/fail, analyzes failures (test name, cause, likely `file:line`). **Read-only** — never edits or commits. | none |
| `/finalize` | Pre-release finalization pass: asks for confirmation, then runs an 8-step checklist (stray diagnostics, docs-code sync, git completeness, simplify diff, trim comments, edge-case re-check, final report, `TASK_PROGRESS.md` update). **Does not commit.** | none |
| `/release-package` | Builds a UPM-installable `.tgz` after a full pre-flight gate (version consistency across 3 sources, CHANGELOG entry, green tests). **Never bumps the version; never commits.** | `[expected version]` (optional) |
| `/resolve-merge-conflicts` | Intent-aware conflict resolution: reads both sides of each conflict, combines by intent (not "take ours/theirs"). Removes markers, verifies. **Does not `git add` or commit.** | `[files]` (optional; default: all conflicted) |

## Review commands

The review family launches **external** AI processes (separate context, read-only) to review the current diff, then triages and fixes findings in an autonomous loop. Verification = the headless EditMode test run.

| Command | Reviewer | Scope |
| ------- | -------- | ----- |
| `/self-review` | Claude Opus 4.8 (fresh process, `--effort max`) | whole project (`base..HEAD` + uncommitted) |
| `/self-review-uncommitted` | Claude Opus 4.8 | uncommitted only (staged + unstaged + untracked vs `HEAD`) |
| `/self-review-branch` | Claude Opus 4.8 | branch-only changes (commits since fork from `main` + uncommitted) |
| `/self-review-opus-4-6` | Claude Opus 4.6 (previous generation, cross-generational second opinion) | whole project |
| `/self-review-gpt-5-5` | GPT-5.5 via Codex CLI (read-only sandbox) | whole project |
| `/self-review-gemini-3-1-pro-high` | Gemini 3.1 Pro (High) via Antigravity CLI | whole project |
| `/self-review-gpt-then-gemini` | GPT-5.5 first, then Gemini 3.1 Pro (High) as a cross-vendor second pass | whole project |

All review commands are **read-only for the reviewer** and **never commit**. They fix findings in the working tree and report.

## Autonomous task commands

These commands drive a task to functional completion without stopping for questions. Design forks are resolved by consulting an external multi-model panel (GPT + Gemini + Claude) instead of asking the user.

| Command | What it does | Argument |
| ------- | ------------ | -------- |
| `/autonomous-task` | Drives a single task to completion autonomously; resolves significant forks via a multi-model panel. **Does not commit or finalize.** | `<task description + definition of done>` (required) |
| `/autonomous-max` | The heaviest mode: role-based subagent pipeline (architect, implementer, tester, reviewer, docs) + self-resolved forks + an external cross-vendor critic cascade (Claude, GPT, Gemini). **Does not commit or finalize.** | `<what to build + definition of done>` (required) |
| `/orchestrate-task` | Orchestrator mode: the lead Claude frames the task, splits it into parts, and drives role-agents (Architect → Implementer → Tester → Reviewer → Docs), reviewing each diff. **Does not commit or finalize.** | `<task description>` (required) |
| `/implement-review-gpt-then-gemini` | Implements a task, then runs a two-stage external critic loop (GPT → Gemini) on the fresh implementation. Generator + critic in one pass. **Does not commit.** | `<task description>` (required) |

## Advisory command

| Command | What it does | Argument |
| ------- | ------------ | -------- |
| `/consult-multi-model` | Consults three external models (GPT-5.5, Gemini 3.1 Pro High, Claude Opus 4.8) on a design question/blocker. Gathers answers in parallel, synthesizes a combined solution, records it in `TASK_PROGRESS.md`. | `[problem description]` (optional; inferred from context if empty) |

---

## Common traits

- **No command ever commits or pushes** — all leave the working tree for the user to review and stage.
- **Verification gate** — any command that modifies code runs the headless Unity EditMode test suite to confirm the build is green before reporting done.
- **Unity Editor must be closed** — the project is single-instance-locked; batchmode fails if the Editor holds the lock.
- **Timeouts** — external model calls and test runs take several minutes; commands use large timeouts and background execution.

## Experimental nature

These commands are in **testing mode**. They work and are actively used during development of this SDK, but their interfaces, model selections, and orchestration patterns are subject to change.

The underlying idea: a **multi-model, cross-vendor review and generation pipeline** where different AI families (Claude, GPT, Gemini) serve as independent reviewers, each catching what the others miss. This approach extends the traditional code-review process with automated, adversarial, multi-perspective analysis — aiming for higher confidence in correctness without additional human reviewer overhead.
