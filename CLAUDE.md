# CLAUDE.md

> **How this file loads:** injected into **every** session automatically; `KnowledgeBase/**/*.md` docs are read **only on demand**. Keep this file to always-on rules + *triggers/pointers* — every line costs context on every task. Full procedures live in `KnowledgeBase/` (index: [KnowledgeBase/INDEX.md](KnowledgeBase/INDEX.md)) and in the slash commands under [`.claude/commands/`](.claude/commands).

## Project shape

- **Dmytro Udovychenko Tracking SDK** (`com.dmytroudovychenko.tracking`) — a production-grade, in-process event-tracking SDK for Unity. Tiny public API (`ITracker.SendMessage` / `SendMapAsync`) over a non-blocking pipeline: bounded thread-safe queue → background batching dispatcher → pluggable transport, with retries/backoff, durable persistence, lifecycle flush, connectivity-awareness, circuit breaker, dead-letter, metrics, logging hook, and privacy opt-out. The value is the **pipeline behind the API**, and the **DI seams that make it deterministically testable**.
- **Single git repo; git root = repo root** (`tracking-sdk/`). The Unity project is the **subfolder** `tracking-sdk/`; `README.md` and this file live at the **root**. The other authored docs (`BUSINESS_LOGIC.md`, `DESIGN.md`, `TASK_PROGRESS.md`) live under **`KnowledgeBase/Documentation/`**. The live test receiver (`track.php`) and package docs ship in `…/Documentation~/`. **No submodules.**
- SDK source: `tracking-sdk/Packages/com.dmytroudovychenko.tracking/Runtime`; tests: `…/Tests/Editor`; demo: `…/Assets/TrackingDemo`. Namespace: `DmytroUdovychenko.Tracking`.
- Remote: GitHub (`https://github.com/Udovychenko-Dmytro/tracking-sdk`). Default branch: `main`.

## Work Rules

- **Commits & staging** — commit or push **only when the user explicitly asks**. On `main`, **branch first**. **Never run `git add` / stage on your own** — not even after a clean verify or finalize; leave the working tree exactly as edited (unstaged) so the user reviews and stages it themselves. Staging is a write to git state: do it only on explicit request. The `/finalize` and `/self-review` passes **never commit and never stage** — they report and leave the tree for the user.
- **Ambiguity** — if a task is unclear on architecture, API shape, data contract, or naming, ask a clarifying question before implementing. Don't guess on decisions that are costly to reverse. Small cosmetic/phrasing choices resolve yourself.
- **Build & Verify** — the "compile check" for this project is the **headless Unity EditMode test run** (see below). Run it after any compile-affecting change (`.cs` / `.asmdef` / `.asmref`) before reporting a task done; skip it for docs/PHP/asset-only diffs. Never assume the code compiles.
- **Analyze before editing** — before modifying a file, scan the code you're about to touch (and its immediate context) for existing issues: bugs, standards violations, null-safety gaps, dead code, magic values, concurrency hazards. If you find any, tell the user before making changes; wait for direction on whether to fix in the same pass or separately.
- **Edge cases** — for every change, enumerate the failure modes the new path can hit and confirm each is handled (or surface it). For this SDK, walk: null/empty inputs (`SendMessage(null)`, empty map, `Result == null`); **thread-safety** (queue hit from many threads while the worker drains); **`TaskCompletionSource` completed on every path** (delivered / retries-exhausted / evicted / rejected / purged / cancelled — a never-completed `Task` is a hang); cancellation + `Dispose` (worker stops cleanly, `HttpClient`/`SemaphoreSlim` released); offline hold → flush; overflow policy (`DropOldest` vs `RejectNew`); at-least-once + idempotency; corrupt/missing persisted file; `JsonUtility` payload round-trip. **Report edge cases even when out of scope** (one-line FYI + add to [WARNINGS.md](KnowledgeBase/Documentation/WARNINGS.md)).
- **Fix and report code problems** — if verification fails, or you notice a bug/standards violation, fix it AND explicitly tell the user: what was wrong, where (`file:line` or `ClassName.Method`), what you changed. Never silently fix.
- **WARNINGS.md** — maintain [KnowledgeBase/Documentation/WARNINGS.md](KnowledgeBase/Documentation/WARNINGS.md) as a persistent list of out-of-scope bugs and edge-case gaps. Add anything you spot but aren't fixing now; read it at the start of each task.
- **TASK_PROGRESS.md is a committed project log** — [`TASK_PROGRESS.md`](KnowledgeBase/Documentation/TASK_PROGRESS.md) (under `KnowledgeBase/Documentation/`) is the project's living progress log, kept in the repo. **Read it for context; never delete, gitignore, or recreate it.** Update it (and the test count) as work advances. Lifecycle + format: [KnowledgeBase/BehaviourRules/TASK_PROGRESS_RULES.md](KnowledgeBase/BehaviourRules/TASK_PROGRESS_RULES.md).
- **Coding standards** — read [KnowledgeBase/BehaviourRules/CODING_STANDARDS.md](KnowledgeBase/BehaviourRules/CODING_STANDARDS.md) before your first C# edit each task. Hot rules: `m_camelCase` private fields (**not** `_`); `DmytroUdovychenko.Tracking` namespaces; `UPPER_SNAKE_CASE` constants (**not** PascalCase); **descriptive names** — spell words out, no cryptic abbreviations (`event` not `evt`, `message` not `msg`, `value` not `val`); explicit enum indices + 100-block group scheme; `sealed` concrete classes; Allman braces, 4-space indent, braces on all control-flow bodies; **no `var`** (explicit types always); DI via optional constructor params with production-default fallbacks; **error isolation** (public API never throws into caller code); log through `ITrackingLogger`, never `Debug.*` in runtime SDK code; `ConfigureAwait(false)` on library awaits; `TryGet`+`out` over returning `null`; tunables on `TrackingConfig`, no magic values.
- **Documentation** — after introducing a non-obvious rule/convention that future sessions need, update the relevant doc. Comments: complex logic only, **≤2 lines** (`//` and `/// <summary>` content alike); 3+ lines → port to a `KnowledgeBase/**/*.md` doc + leave a 1-line pointer.
- **Sync docs with code** — when you change code, grep `KnowledgeBase/**/*.md` (incl. the docs now under `KnowledgeBase/Documentation/`: `BUSINESS_LOGIC.md`, `DESIGN.md`, `TASK_PROGRESS.md`), this file, the root `README.md`, the package `README.md`/`CHANGELOG.md`, and the package `Documentation~/` for the symbol/path you touched and fix every reference in the same turn — including the **test count**, which appears in several files. Prefer stable symbol refs (`ClassName.Method`) over `file.cs:line`. Conventions: [KnowledgeBase/BehaviourRules/DOCUMENTATION_RULES.md](KnowledgeBase/BehaviourRules/DOCUMENTATION_RULES.md).
- **Versioning & CHANGELOG** — the SDK follows [SemVer](https://semver.org/) (new public API → minor; breaking change → major; fix-only → patch). **When releasing a new version**, bump it in **all three places together** (a test pins them): `version` in `Packages/com.dmytroudovychenko.tracking/package.json`, `TrackingSdk.VERSION`, and the `SmokeTests` assertion (`Is.EqualTo("x.y.z")`). Then update the package **`CHANGELOG.md`** (Keep a Changelog): rename the `## [Unreleased]` section to `## [x.y.z] - YYYY-MM-DD` and open a fresh empty `[Unreleased]` above it. Land **every** user-facing change under `[Unreleased]` (`Added`/`Changed`/`Fixed`/`Removed`) as you make it — don't defer to release time. Releasing/tagging is a user action; never bump the version unprompted. To build the distributable UPM tarball once a version is released, use **[`/release-package`](.claude/commands/release-package.md)** (gates version/CHANGELOG/tests, then `npm pack` → `dist/*.tgz`; never bumps the version).
- **Editing Unity YAML (`.asset`, `.unity`, `.prefab`)** — empty-string fields like `  m_Name: ` serialize with a trailing space; the Edit tool strips it, polluting diffs. After a block edit, verify with `grep -cE '^\s*m_(Name|EditorClassIdentifier):$' <file>` and restore the space if needed. (This SDK is mostly plain C#; YAML edits are rare.)

## Task finalization

> Executable procedure: the **[`/finalize`](.claude/commands/finalize.md)** slash command. Detailed reference: [KnowledgeBase/BehaviourRules/FINALIZATION_RULES.md](KnowledgeBase/BehaviourRules/FINALIZATION_RULES.md). For a fresh-eyes (separate Opus 4.8 process) pass: **[`/self-review`](.claude/commands/self-review.md)** / `/self-review-uncommitted`.

**Gate first** — before running the checklist, use `AskUserQuestion`: "Work is in — does everything look right? Ready to finalize for release?" Options "Yes, finalize" / "No, not yet". Proceed only on confirmation. **If "No" / non-committal: close the widget and stop — write nothing, don't summarize, don't ask a follow-up.** Wait for the user.

When confirmed, run the pass and report a scannable `## Finalization` section (`✓ clean` / `⚠ found X — action: Y` per item). Steps, in order: (1) remove stray diagnostics (keep the demo output, `[live-retry]` line, and `ITrackingLogger` routing); (2) sync docs ↔ code (incl. test count); (3) git completeness (single repo, no submodules); (4) simplify your own diff; (5) trim comments to ≤2 lines; (6) edge-case re-check on final code; (7) Final Report ([FINAL_REPORT_RULES.md](KnowledgeBase/BehaviourRules/FINAL_REPORT_RULES.md)); (8) ensure `TASK_PROGRESS.md` is current — **do not delete it** (committed project doc). **Do not commit or stage, even when clean** — see "Commits & staging".

## Project documentation

| File | Covers |
| ---- | ------ |
| [README.md](README.md) | Project overview, architecture, implicit-requirements scorecard, install |
| [BUSINESS_LOGIC.md](KnowledgeBase/Documentation/BUSINESS_LOGIC.md) | High-level conceptual overview of SDK behavior (init, connectivity, data sent, config, guarantees) + Mermaid flowcharts (§13) — no implementation detail |
| [BUSINESS_LOGIC_INTENT.md](KnowledgeBase/Documentation/BUSINESS_LOGIC_INTENT.md) | **Developer-authored** intent log: desired behavior in plain language → Claude implements (code + tests + docs). Source that drives `BUSINESS_LOGIC.md` |
| [DESIGN.md](KnowledgeBase/Documentation/DESIGN.md) | Rationale for non-obvious decisions (async batch-delivery semantics, drop policy, at-least-once + idempotency, `HttpClient` vs `UnityWebRequest`, DI seams) |
| [TASK_PROGRESS.md](KnowledgeBase/Documentation/TASK_PROGRESS.md) | **Committed** living progress log (phases, test counts, status) + the original plan-of-record (appendix) + AI-workflow record — a project doc |
| Package `Documentation~/` | API reference, configuration, architecture, and `track.php` live test receiver — ships with the UPM tarball |
| [KnowledgeBase/INDEX.md](KnowledgeBase/INDEX.md) | KnowledgeBase index: BehaviourRules + Documentation tables |

### BehaviourRules (mandatory — read before starting work)

| File | Covers |
| ---- | ------ |
| [CODING_STANDARDS.md](KnowledgeBase/BehaviourRules/CODING_STANDARDS.md) | C# conventions the SDK follows (naming, DI, error isolation, async, patterns, tests) — read before first C# edit |
| [DOCUMENTATION_RULES.md](KnowledgeBase/BehaviourRules/DOCUMENTATION_RULES.md) | How to write/maintain KnowledgeBase docs |
| [FINALIZATION_RULES.md](KnowledgeBase/BehaviourRules/FINALIZATION_RULES.md) | Finalization checklist (reference for `/finalize`) |
| [FINAL_REPORT_RULES.md](KnowledgeBase/BehaviourRules/FINAL_REPORT_RULES.md) | PR/MR + verify + commit-message blocks |
| [TASK_PROGRESS_RULES.md](KnowledgeBase/BehaviourRules/TASK_PROGRESS_RULES.md) | Maintaining the committed `TASK_PROGRESS.md` project doc |

### Documentation (on-demand reference)

| File | Covers |
| ---- | ------ |
| [AI_COMMANDS.md](KnowledgeBase/Documentation/AI_COMMANDS.md) | Quick reference for all Claude Code slash commands (review, autonomous, utility) — experimental |
| [WARNINGS.md](KnowledgeBase/Documentation/WARNINGS.md) | Persistent out-of-scope bug/issue list |

## Build & Verify

No CLI build script. Verification = the **headless Unity EditMode test run**. Unity version is read dynamically from `ProjectSettings/ProjectVersion.txt` (currently `2022.3.62f3`). **The Unity Editor must be closed** (single-instance project lock).

```bash
ROOT="$(git rev-parse --show-toplevel)"; PROJ="$ROOT/tracking-sdk"
VER=$(awk '/^m_EditorVersion:/{print $2}' "$PROJ/ProjectSettings/ProjectVersion.txt")
UNITY="/Applications/Unity/Hub/Editor/$VER/Unity.app/Contents/MacOS/Unity"
# Bail if the Editor is open (else batchmode fails on the project lock):
if [ -f "$PROJ/Library/EditorInstance.json" ]; then
  PID=$(grep -o '"process_id" : [0-9]*' "$PROJ/Library/EditorInstance.json" | grep -o '[0-9]*')
  [ -n "$PID" ] && ps -p "$PID" >/dev/null 2>&1 && { echo "Unity Editor open (PID $PID) — close it first"; exit 1; }
fi
rm -f "$ROOT/Temp/editmode.xml" "$ROOT/Temp/editmode.log"
"$UNITY" -runTests -batchmode -nographics -projectPath "$PROJ" -testPlatform EditMode \
  -testResults "$ROOT/Temp/editmode.xml" -logFile "$ROOT/Temp/editmode.log"
echo "exit=$?"
grep -m1 -o '<test-run[^>]*>' "$ROOT/Temp/editmode.xml" 2>/dev/null \
  || { echo "no results — compile errors? tail:"; grep -iE 'error CS[0-9]+|Compilation failed' "$ROOT/Temp/editmode.log" | head; }
```

Green = `failed="0"` with a non-zero `passed` in the `<test-run>` line (suite is currently 147 tests: 145 deterministic + 2 live tests that POST to the deployed receiver, so the default run needs network). Latency: batchmode startup + tests is a few minutes — use a large timeout and let the run background. Inside the Editor you can instead use **Window → General → Test Runner → EditMode → Run All**.

The **[`/check-tests`](.claude/commands/check-tests.md)** slash command runs this exact suite and analyzes any failures (test → cause → likely `file:line`); **[`/release-package`](.claude/commands/release-package.md)** gates on it before building the UPM `.tgz`.
