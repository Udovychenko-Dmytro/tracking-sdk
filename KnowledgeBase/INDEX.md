# KnowledgeBase

Developer documentation for the **Dmytro Udovychenko Tracking SDK** (`com.dmytroudovychenko.tracking`). For the project overview, architecture, and design rationale see the git-root [`README.md`](../README.md) and [`DESIGN.md`](Documentation/DESIGN.md).

`CLAUDE.md` (git root) is injected into **every** session automatically; files under `KnowledgeBase/` are **not** — they're read only when something points to them. The two subfolders differ in obligation:

## `BehaviourRules/` — mandatory process rules

When a `CLAUDE.md` trigger points to one of these, following its procedure is required, not optional. They govern *how* you work. The executable versions of finalization/review are the slash commands in [`.claude/commands/`](../.claude/commands) (`/finalize`, `/self-review`, `/self-review-uncommitted`) — these docs are the detailed reference and must stay reconciled with them.

| File | What it covers | Fired by |
| ---- | -------------- | -------- |
| [CODING_STANDARDS.md](BehaviourRules/CODING_STANDARDS.md) | C# conventions the SDK mandates: `m_camelCase` fields, `UPPER_SNAKE_CASE` consts, explicit/100-block enums, no `var`, `DmytroUdovychenko.Tracking` namespaces, `sealed`, DI seams, error isolation, `ITrackingLogger`, async/`ConfigureAwait`, Null-Object/Strategy patterns, deterministic tests | CLAUDE.md "Coding standards" Work Rule (read before first C# edit) |
| [DOCUMENTATION_RULES.md](BehaviourRules/DOCUMENTATION_RULES.md) | How to write/maintain KnowledgeBase docs: symbol refs, per-doc header, granularity, don't-duplicate-commands | CLAUDE.md "Documentation" / "Sync docs" rules |
| [FINALIZATION_RULES.md](BehaviourRules/FINALIZATION_RULES.md) | The finalization checklist + output format (detailed reference for the `/finalize` command) | CLAUDE.md §"Task finalization" gate |
| [FINAL_REPORT_RULES.md](BehaviourRules/FINAL_REPORT_RULES.md) | Paste-ready PR/MR description + how-to-verify + commit messages, produced at finalization step 7 | FINALIZATION_RULES.md step 7 |
| [TASK_PROGRESS_RULES.md](BehaviourRules/TASK_PROGRESS_RULES.md) | How to maintain the **committed** `TASK_PROGRESS.md` progress log under `Documentation/` (project doc — never deleted) | CLAUDE.md "TASK_PROGRESS.md" Work Rule |

> ⚠️ `TASK_PROGRESS.md` (under `KnowledgeBase/Documentation/`) is a **committed project doc**, not an ephemeral gitignored changelog. Never delete or recreate it — read [TASK_PROGRESS_RULES.md](BehaviourRules/TASK_PROGRESS_RULES.md).

## `Documentation/` — authored docs + on-demand subsystem reference

Holds the project's **authored docs** (overview rationale, progress log + the original plan-of-record, high-level business-logic overview + flowcharts) **and** on-demand **code-level subsystem references**. Consulted as needed; kept alphabetically sorted by filename. The git-root `README.md` stays the entry point (link, don't duplicate).

| File | Covers |
| ---- | ------ |
| [AI_COMMANDS.md](Documentation/AI_COMMANDS.md) | Quick reference for all Claude Code slash commands (review, autonomous, utility) — what each does, arguments, common traits. **Experimental / testing mode** |
| [BUSINESS_LOGIC.md](Documentation/BUSINESS_LOGIC.md) | High-level conceptual overview of SDK behavior (init, connectivity, data sent, config, guarantees) + Mermaid flowcharts (§13) — no implementation detail |
| [BUSINESS_LOGIC_INTENT.md](Documentation/BUSINESS_LOGIC_INTENT.md) | **Developer-authored** intent log — desired SDK behavior in plain language; Claude implements each entry (code + tests + docs). Source that drives `BUSINESS_LOGIC.md` |
| [DESIGN.md](Documentation/DESIGN.md) | Rationale for the non-obvious design decisions (async batch-delivery, drop policy, at-least-once + idempotency, `HttpClient` vs `UnityWebRequest`, DI seams) |
| [STATIC_FACADE.md](Documentation/STATIC_FACADE.md) | The static `Tracker` facade over the `TrackingSystem` instance core: Init overloads + the static-state edge-case policy (call-before-Init, double-Init, lifecycle, domain-reload reset) |
| [TASK_PROGRESS.md](Documentation/TASK_PROGRESS.md) | **Committed** living progress log (phases, test counts, status) + the original plan-of-record (appendix) + AI-workflow record — a project doc, never deleted |
| [WARNINGS.md](Documentation/WARNINGS.md) | Persistent list of out-of-scope bugs and issues found during sessions — always kept current, never deleted |

_(Add new subsystem docs at their sorted position and register them in both this table and the CLAUDE.md "Project documentation" table.)_
