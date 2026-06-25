---
description: Resolve merge/rebase conflicts in the working tree by intent — identify the operation (merge vs rebase, ours/theirs), understand BOTH sides and what each intended per conflict, combine correctly (default: preserve both sides' intent, not "take one"), remove markers, verify (no markers, headless EditMode tests for .cs changes, no dangling refs). Does NOT git add/commit — stops at "resolved in tree"; the user finishes.
argument-hint: "[specific files | empty = all conflicted]"
allowed-tools: Bash, Read, Edit, Write, Grep, Glob
---

# /resolve-merge-conflicts — intent-aware merge/rebase conflict resolution

You resolve the current merge/rebase's conflicts **by intent**, not mechanically. For each conflict you understand what each side changed and why, and combine correctly — by default **preserving both sides' intent** (not "take ours" / "take theirs"). You remove the markers, verify the result, and **stop before `git add`/`commit`** — the user finishes (Commits rule).

**Boundaries (absolute):**

- **`git add` / `git commit` / `git merge --continue` / `git rebase --continue` — I do NOT run them** (Commits rule: commit or push only when the user explicitly asks; on `main`, branch first; `--continue` effectively commits). The resolution stays in the working tree as `UU` until the user stages it.
- **`git merge --abort` / `git rebase --abort` — I do not run them myself** (that discards someone's work). I offer it as an option when resolving is unsafe or there's nothing to resolve.
- **Protected territory** — a conflict that changes a documented deliberate decision in **`DESIGN.md`** (async batch-delivery semantics, drop policy, at-least-once + idempotency, `HttpClient` vs `UnityWebRequest`, the DI seams), the task's stated intent in **`TASK_PROGRESS.md`** (a committed project doc that may itself conflict), or an established codebase convention: I do **not** "resolve to taste". I reconstruct both sides, but if the choice alters protected behavior I surface it to the user — I don't guess.
- **Genuine either/or** (two mutually exclusive features/product decisions whose intent can't be reconstructed from history) — surface to the user, don't guess.

The command argument (`$ARGUMENTS`) is an optional list of files to resolve. Empty → all conflicted files (`git diff --diff-filter=U`).

---

## Step 0 — Determine state: merge or rebase, what against what

`ours`/`theirs` semantics depend on the operation — determine it **before** editing, or "take ours" overwrites the wrong side.

```bash
cd "$(git rev-parse --show-toplevel)"
if [ -f .git/MERGE_HEAD ]; then
  echo "OP=merge"
  echo "  ours  (HEAD,        <<<<<<<) = $(git --no-pager log --oneline -1 HEAD)"
  echo "  theirs(MERGE_HEAD,  >>>>>>>) = $(git --no-pager log --oneline -1 "$(cat .git/MERGE_HEAD)")"
elif [ -d .git/rebase-merge ] || [ -d .git/rebase-apply ]; then
  echo "OP=rebase  ‼ ours/theirs are INVERTED vs merge (see below)"
  git --no-pager status | sed -n '1,4p'
elif [ -f .git/CHERRY_PICK_HEAD ]; then
  echo "OP=cherry-pick"
  echo "  ours  (HEAD,             <<<<<<<) = $(git --no-pager log --oneline -1 HEAD)"
  echo "  theirs(CHERRY_PICK_HEAD, >>>>>>>) = $(git --no-pager log --oneline -1 "$(cat .git/CHERRY_PICK_HEAD)")"
elif [ -f .git/REVERT_HEAD ]; then
  echo "OP=revert  ‼ theirs = the REVERSED commit (a reverse-diff is applied)"
  echo "  reverting: $(git --no-pager log --oneline -1 "$(cat .git/REVERT_HEAD)")"
else
  echo "OP=none"
fi
echo "--- conflicted files (diff-filter=U) ---"
git --no-pager diff --name-only --diff-filter=U
```

- **merge** (`.git/MERGE_HEAD`): `ours` (`<<<<<<<`) = your branch (HEAD); `theirs` (`>>>>>>>`) = incoming (MERGE_HEAD).
- **rebase** (`.git/rebase-merge`/`rebase-apply`): **inverted** — `ours` = the branch you're replaying **onto** (upstream/target, e.g. `main`), `theirs` = **your** commit being replayed. Don't mix these up, or "keep ours" throws away your work.
- **cherry-pick** (`.git/CHERRY_PICK_HEAD`) / **revert** (`.git/REVERT_HEAD`): `ours` = HEAD, `theirs` = the applied commit (for revert, its reverse-diff). Side semantics are like merge, but completion differs (see Step 4).
- **OP=none and no `U` files** → no conflicts: report and **stop** (nothing to resolve).
- `$ARGUMENTS` non-empty → restrict the set to those files; otherwise take the whole list.

---

## Step 1 — Per file: understand both sides BEFORE editing

Don't "pick a side" blindly. For each conflicted file:

1. Find the hunks: `grep -nE '^(<<<<<<<|=======|>>>>>>>)' <file>` and read them with context (`Read`).
2. When unclear, inspect the three versions via git stage-refs (stronger than reading markers):
   - `git --no-pager show :1:<file>` — **base** (common ancestor),
   - `git --no-pager show :2:<file>` — **ours**, `git --no-pager show :3:<file>` — **theirs**.
     What each side changed **relative to base** is its intent.
3. Classify each hunk and pick a strategy:
   - **Both added different things in the same place** (CHANGELOG entries, lists, `using` imports, enum cases) → almost always **union both**, not pick one. For SDK enums note the **explicit-index + 100-block group scheme** (CODING_STANDARDS) — when both sides added cases, renumber so indices stay unique and grouped, don't just concatenate.
   - **Both changed the same line/logic differently** → synthesize a result reflecting **both** intents; if mutually exclusive → see Boundaries.
   - **One deleted, the other edited** (delete/modify) → non-trivial: usually surface to the user, except obvious cases.
   - **Binary file** → not text-mergeable: version choice is `git checkout --ours/--theirs <file>` (not add/commit; allowed under the Commits rule), but which one is usually the user's call — don't guess.
4. Project triage: a hunk touching a `DESIGN.md` deliberate decision / `TASK_PROGRESS.md` intent / an established convention / a genuine either-or → **don't resolve silently**, flag for the user (Step 4).

**Stack-specific merge notes (this repo):**

- **C# source (`.cs`)** — after the union/synthesis, the file must compile and pass the tests (Step 3). No half-merged fragments or placeholders; match the surrounding style (CODING_STANDARDS: `m_camelCase` fields, `UPPER_SNAKE_CASE` constants, `sealed` classes, Allman braces, no `var`, `ConfigureAwait(false)`, `ITrackingLogger` not `Debug.*`). Watch concurrency-sensitive merges (queue/dispatcher/`TaskCompletionSource` paths) — a careless union can leave a `Task` never completed or a lock half-applied.
- **A new `.cs` needs its `.meta`** — if one side added a script and its `.meta` and they conflict or one is missing, keep **both** the script and the matching `.meta` (Unity breaks without it). Don't invent a new GUID.
- **Unity YAML (`.meta` / `.asset` / `.unity` / `.prefab`)** — these are GUID/fileID-keyed; a textual union usually corrupts them. Reconstruct by intent, preferring whole-side `--ours/--theirs` for a given object unless you understand the YAML. **Caveat (CLAUDE.md):** empty-string fields like `  m_Name: ` serialize with a trailing space that `Edit` strips, polluting the diff — after a block edit verify with `grep -cE '^\s*m_(Name|EditorClassIdentifier):$' <file>` and restore the space if the count is > 0.
- **`TASK_PROGRESS.md`** — a committed project doc that often conflicts (both sides bump phase status / **test counts**). Union the prose; for the test total reconcile to the **actual** current count (run Step 3 or `grep` the suite), don't blindly take either side's number. Never drop a side's progress entry.
- **`CHANGELOG.md` / `README.md` / other docs** — union both sides' entries into the correct section; collapse near-duplicate lines into one (don't leave two almost-identical bullets).

---

## Step 2 — Combine and remove markers

Edit each file (`Edit`) to the correct merged content, removing **all three** markers (`<<<<<<<`, `=======`, `>>>>>>>`).

- **Default — preserve both sides' intent.** Don't drop a side silently; if you must, record why for the report (Step 4).
- "Take a whole side" is justified when one version objectively subsumes the other (`git checkout --ours/--theirs <file>` for a whole file) — but confirm the other side carried nothing unique.
- Code: after combining it must compile and pass tests — verified in Step 3. No half-merged fragments or placeholders; match the surrounding code's style.
- Don't leave "consolation" duplicates (both branches added a near-identical block → one merged entry, as in a CHANGELOG: both points in one line, shared tail once).

---

## Step 3 — Verify the resolution

```bash
cd "$(git rev-parse --show-toplevel)"
# 1) No markers left ANYWHERE
git --no-pager grep -nE '^(<<<<<<<|=======|>>>>>>>)( |$)' || echo "✓ no conflict markers"
# 2) What git still considers unmerged (files stay U until git add — that's expected)
git --no-pager diff --name-only --diff-filter=U
```

1. **No markers** in any file (command above).
2. **Both sides survived:** for each resolved file, `grep` for a characteristic line from the ours-version AND from the theirs-version — both should appear (unless one was intentionally dropped).
3. **Dangling references:** merge/rebase may have deleted/renamed files — find them **independent of the operation** (don't rely on `.git/MERGE_HEAD`, absent in rebase): `git --no-pager diff --diff-filter=DR --name-status HEAD` — paths gone/renamed vs current HEAD. For each, `git --no-pager grep -nI "<name>"`; fix live references in the same pass (don't touch CHANGELOG history).
4. **Build & Verify (headless Unity EditMode tests):** run **only if** the resolved files include `.cs`/`.asmdef`/`.asmref` (skip for docs/PHP/`.meta`/asset-only diffs). **Editor must be closed** (single-instance lock). Use a large timeout — batchmode is a few minutes and the Bash tool will usually auto-background it.

   ```bash
   cd "$(git rev-parse --show-toplevel)"
   ADIR=".claude/artifacts/resolve-merge-conflicts"; mkdir -p "$ADIR"
   [ -f .claude/artifacts/.gitignore ] || printf '*\n' > .claude/artifacts/.gitignore
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

   Green = `failed="0"` with a non-zero `passed` in the `<test-run>` line (suite is 145 deterministic tests + 2 `[Category("Live")]` live tests that run in the default headless suite and need network). Failure → fix to green; report honestly, never silently fix (Fix and report code problems).
5. **Semantic coherence:** the result must be logically whole, not merely marker-free (the seam between the two sides must not contradict itself).

---

## Step 4 — Stop at "resolved" + report (NOT git add/commit)

Conflicts combined, no markers, tests (if needed) green:

- **Stop.** `git add` / `git commit` / `git merge --continue` / `git rebase --continue` **not run** (Commits rule). Files stay `UU` in the tree until the user stages them.

Print a `## Resolve-merge summary` section:

- **Operation** — merge or rebase; what against what (ours/theirs, one line each).
- **Per file** — for each: how resolved (**unioned** / took ours / took theirs / **synthesized**) and **why**; what was preserved from each side; if a side was dropped, say so explicitly.
- **For the user** — conflicts you did not resolve yourself (protected `DESIGN.md`/`TASK_PROGRESS.md` decision / established convention / genuine either-or / binary) — with the question for how to decide.
- **Verification** — no markers ✓; both sides survived ✓; dangling references (found/fixed); EditMode test status (or "docs/asset-only — skipped").
- **You finish (Commits rule)** — exact completion commands:
  - merge: `git add <files> && git commit --no-edit`
  - rebase: `git add <files> && git rebase --continue`
  - cherry-pick: `git add <files> && git cherry-pick --continue` · revert: `git add <files> && git revert --continue`
  - or abort: `git merge --abort` / `git rebase --abort` (if resolving is not warranted).
- Reminder: the resolution is **in the working tree, not committed**. Say "commit" and I'll stage the resolved files and run completion myself.
