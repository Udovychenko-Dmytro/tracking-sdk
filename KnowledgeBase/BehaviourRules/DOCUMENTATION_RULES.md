# Documentation Rules — writing & maintaining KnowledgeBase docs

How to write and keep the `KnowledgeBase/**/*.md` docs trustworthy. Triggered by the "Documentation" and "Sync docs with code" Work Rules in [CLAUDE.md](../../CLAUDE.md). For *where* a doc lives and the auto-load model, see [INDEX.md](../INDEX.md).

## 1. Reference symbols, not line numbers

Line numbers rot on every edit; the "Sync docs with code" rule then burns effort chasing drift. Anchor references to the most stable identifier available:

- **Best:** a symbol — `ClassName.MethodName`, a field, an enum value. Survives edits and most refactors.
- **OK:** `file.cs` + symbol — `EventDispatcher.RunAsync`.
- **Last resort:** `file.cs:line` — only as a *hint* alongside a symbol, never the sole anchor. Expect it to be stale.

```
GOOD:  `RetryPolicy.TryGetDelay` computes the capped backoff with jitter.
WEAK:  RetryPolicy.cs:54 computes the backoff.   <- line will drift
```

## 2. Every doc opens with a standard header

So a reader instantly knows the scope and whether to trust it:

```markdown
# <Doc Title>

> **Purpose:** <1 line — what this doc explains.>
> **Key files:** `Foo.cs`, `Bar.cs` (the symbols this doc tracks).
> **Last reviewed:** <commit short-sha> against `<branch>`.
```

- **Last reviewed** is the trust signal: if the marker is behind current `main`, re-verify before relying on the doc. Use a commit short-sha (no dates); update it whenever you sync the doc to code (finalization step 2).
- **Key files** makes the "Sync docs with code" grep trivial — you know exactly what to re-check.

## 3. One doc per subsystem — extend before you create

The "Project documentation" table in `CLAUDE.md` is the index; keep it from sprawling:

- A new doc earns its place only for a **distinct subsystem** (e.g. a `DISPATCH_PIPELINE` or `PERSISTENCE` doc). If your topic fits an existing doc's scope, **extend that doc** with a new section instead.
- Cross-link rather than duplicate: state a fact once, link to it from the other doc. (Architecture is documented in `KnowledgeBase/Documentation/DESIGN.md` and the root `README.md` — link to those rather than restating. `KnowledgeBase/Documentation/` holds both the authored project docs and code-level subsystem references.)
- When you add or rename a doc, update the **Project documentation** table in [CLAUDE.md](../../CLAUDE.md) **and** the list in [INDEX.md](../INDEX.md) in the same turn — the tables are how the next session discovers it.
- **List alphabetically:** every index table — Documentation *and* BehaviourRules, in both [CLAUDE.md](../../CLAUDE.md) and [INDEX.md](../INDEX.md) — lists entries alphabetically by filename (byte order: `_` sorts after letters, so `FINALIZATION` precedes `FINAL_REPORT`). Insert at the sorted position, never append.

## 4. Don't duplicate the slash commands

The executable procedures for finalization and review already live as slash commands in `.claude/commands/` (`/finalize`, `/self-review`, `/self-review-uncommitted`). The BehaviourRule docs ([FINALIZATION_RULES.md](FINALIZATION_RULES.md), [FINAL_REPORT_RULES.md](FINAL_REPORT_RULES.md)) are the **detailed reference**; the command file is the **source of truth for the steps actually run**. If you change one, reconcile the other in the same turn.
