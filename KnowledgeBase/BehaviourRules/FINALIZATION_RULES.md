# Task Finalization

Detailed reference for the pre-release finalization pass. The **executable source of truth is the `/finalize` slash command** ([`.claude/commands/finalize.md`](../../.claude/commands/finalize.md)) — run that; this doc explains the steps and the project-specific rationale. The **trigger + gate** also live in [CLAUDE.md](../../CLAUDE.md) §"Task finalization". Keep this doc and the command file reconciled when either changes.

Related: [TASK_PROGRESS_RULES.md](TASK_PROGRESS_RULES.md) (the committed progress log) and [FINAL_REPORT_RULES.md](FINAL_REPORT_RULES.md) (the PR/MR + verify block produced at step 7). For a deeper, fresh-eyes pass run the `/self-review` command (separate Opus 4.8 process) instead of, or in addition to, this in-context self-check.

## When to run

When the task is functionally done and the user has confirmed it works (via the gate below), don't stop at "the change landed" — run the pass and report a checklist. If everything is clean, say so explicitly so the user knows the pass happened.

## The gate (before running the checklist)

- Use `AskUserQuestion`: "Work is in — does everything look right? Ready to finalize for release?" Options: "Yes, finalize" / "No, not yet". Proceed only on confirmation.
- **If "No" / non-committal:** close the widget and **stop** — write nothing, don't summarize, don't ask a follow-up. Wait for the user.

## Checklist — quick reference (full prose lives in [`/finalize`](../../.claude/commands/finalize.md))

> To avoid drift, this is a condensed index of the steps; the command file carries the detailed prose. Mechanical fixes inline; judgment calls get reported, not acted on.

1. **Stray diagnostics** — remove `[ClaudeTest]` / `TODO(claude)` / commented-out experiments / session `Debug.Log`. **Keep** the deliberate logging: the demo's Canvas/uGUI output, `LiveRetryTests`' `[live-retry]` line, and anything routed through `ITrackingLogger`. Unsure → list it for the user.
2. **Docs ↔ code sync** — fix every stale symbol / path / **test count** across the root docs (`README`/`DESIGN`/`TASK_PROGRESS`), the package `README`/`CHANGELOG`, the package `Documentation~/`, and `KnowledgeBase/**/*.md`. Port durable conventions into a doc (or [../Documentation/WARNINGS.md](../Documentation/WARNINGS.md) for out-of-scope issues). How: [DOCUMENTATION_RULES.md](DOCUMENTATION_RULES.md).
3. **Git completeness** — `git status` at the git root (single repo, **no submodules**); no stray untracked (`.self-review/`, `*.tgz`, `Library/`, `Temp/`), no `.cs` missing its `.meta`; list what to commit; branch off `main` first if substantial. **Don't commit.**
4. **Simplification** — cheap cleanups scoped to **your own** diff (`.Where(...).Count() > 0` → `.Any(...)`, dead members left after iteration, expression-bodied where clearer). Don't refactor pre-existing code unless asked.
5. **Trim comments** — ≤2 lines per `//` and per `/// <summary>` content; port overflow to a doc; never destroy unrecorded info.
6. **Edge-case re-check** — re-walk the SDK edge-case categories from [CLAUDE.md](../../CLAUDE.md) "Edge cases" against the **final** code (the canonical list lives there — not restated here to avoid drift). Fix or flag.
7. **Final report** — the three paste-ready blocks; the templates are the source of truth in [FINAL_REPORT_RULES.md](FINAL_REPORT_RULES.md).
8. **TASK_PROGRESS.md** — ensure it reflects the final state (phases, test counts, status); **never delete** — committed project doc ([TASK_PROGRESS_RULES.md](TASK_PROGRESS_RULES.md)).

## Verification = headless EditMode test run

The project's "compile check" is the **headless Unity EditMode test run**, not a bare compile method. Run it when compile-affecting files changed (`.cs` / `.asmdef` / `.asmref`); skip for docs/PHP/asset-only diffs. See [CLAUDE.md](../../CLAUDE.md) "Build & Verify" for the exact command and the Editor-must-be-closed constraint. Green = `failed="0"` with a non-zero `passed`.

## Output format

A short, scannable `## Finalization` section — one sub-bullet per item, each `✓ clean` or `⚠ found X — action: Y`. Then ask the user whether to proceed with anything that needs a decision. **Do not commit or stage (`git add`), even after the checklist is clean** — the user stages and runs `git commit` (and branches first if appropriate). See [CLAUDE.md](../../CLAUDE.md) "Commits & staging".
