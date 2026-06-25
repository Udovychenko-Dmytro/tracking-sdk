---
description: Run the headless Unity EditMode test suite, then analyze the results and explain any failures (test, cause, likely file:line). Read-only — never edits, commits, or stages.
argument-hint: "(no argument — runs the full EditMode suite)"
allowed-tools: Bash, Read, Grep, Glob
---

# /check-tests — run and analyze the EditMode test suite

Runs the project's verification suite — the **headless Unity EditMode test run** — and reports the outcome. On failure it analyzes `Temp/editmode.xml`: which tests failed, the assertion message, the top app stack frame, a one-line root-cause read, and the likely source location to fix.

Runs **all** tests, including the 2 live tests (`[Category("Live")]` — `LiveTransportTests`, `LiveRetryTests`) that POST to the deployed receiver and **need network**. A live-test failure is reported separately from a deterministic regression so a network blip isn't mistaken for a code break.

This command takes **no argument**. It is **read-only**: it does not edit code, commit, or stage. If failures are real, it ends by offering to fix them — it does not fix on its own.

> **Timeout:** batchmode startup + the suite takes a few minutes. Run the test step with a large timeout (up to `600000` ms); the Bash tool will usually **auto-background** it — wait for the completion `<task-notification>` rather than polling. `Temp/editmode.xml` stays absent until the run finishes.
>
> **Editor must be closed** — the project is single-instance-locked; batchmode fails if the Editor holds the lock. The run guards on `Library/EditorInstance.json` and stops with a clear message if so.

---

## Step 1 — Run the suite

The "Build & Verify" block from `CLAUDE.md`: anchor on the repo root, read the Unity version dynamically from `ProjectSettings/ProjectVersion.txt`, bail if the Editor is open, then run all EditMode tests.

```bash
ROOT="$(git rev-parse --show-toplevel)"; PROJ="$ROOT/tracking-sdk"
VER=$(awk '/^m_EditorVersion:/{print $2}' "$PROJ/ProjectSettings/ProjectVersion.txt")
UNITY="/Applications/Unity/Hub/Editor/$VER/Unity.app/Contents/MacOS/Unity"
# Bail if the Editor is open (else batchmode fails on the project lock):
if [ -f "$PROJ/Library/EditorInstance.json" ]; then
  PID=$(grep -o '"process_id" : [0-9]*' "$PROJ/Library/EditorInstance.json" | grep -o '[0-9]*')
  if [ -n "$PID" ] && ps -p "$PID" >/dev/null 2>&1; then
    echo "BLOCKED: Unity Editor is open (PID $PID) — close it first, then re-run /check-tests."; exit 1
  fi
fi
[ -x "$UNITY" ] || { echo "ERROR: Unity not at $UNITY — open the Hub or fix the path."; exit 1; }
rm -f "$ROOT/Temp/editmode.xml" "$ROOT/Temp/editmode.log"
"$UNITY" -runTests -batchmode -nographics -projectPath "$PROJ" -testPlatform EditMode \
  -testResults "$ROOT/Temp/editmode.xml" -logFile "$ROOT/Temp/editmode.log"
echo "unity exit=$?"
```

---

## Step 2 — Determine the outcome

```bash
ROOT="$(git rev-parse --show-toplevel)"
if [ ! -f "$ROOT/Temp/editmode.xml" ]; then
  echo "NO RESULTS — likely a compile error (no test-run produced). First errors:"
  grep -iE 'error CS[0-9]+|Compilation failed|error:' "$ROOT/Temp/editmode.log" | head -40
else
  grep -m1 -o '<test-run[^>]*>' "$ROOT/Temp/editmode.xml"
fi
```

- **No `editmode.xml`** → a compile failure. The grep lists the offending `error CS####` lines (each carries its `file(line,col)`). Report the assembly and the first errors with `file:line`; the suite never ran.
- **`<test-run …>` present** → read `total` / `passed` / `failed` / `skipped`. `failed="0"` with a non-zero `passed` = **green**.

---

## Step 3 — Analyze failures (only if `failed > 0`)

Extract just the failed cases into a focused file, then read it:

```bash
ROOT="$(git rev-parse --show-toplevel)"
xmllint --xpath '//test-case[@result="Failed" or @result="Error"]' "$ROOT/Temp/editmode.xml" \
  > "$ROOT/Temp/editmode-failures.xml" 2>/dev/null \
  || grep -nE 'test-case .*result="(Failed|Error)"' "$ROOT/Temp/editmode.xml"
```

`Read` `Temp/editmode-failures.xml` (or the grep output) and, for each failed case, pull:
- the full test name (`fullname` / class + method),
- the `<failure><message>` (the assertion or exception),
- the top **app** frame of `<stack-trace>` (the first `DmytroUdovychenko.Tracking…` frame, not NUnit internals).

For each failure give a one-line root-cause read and the likely fix location (`ClassName.Method` or `file:line`). Cross-check the symbol against the source before asserting a cause.

**Live vs deterministic:** if a failed test's class is `LiveTransportTests` / `LiveRetryTests` or carries `Category("Live")`, report it under a separate **"Live (network)"** heading — its failure is most likely a network/connectivity/receiver issue, not an SDK regression. Failures in the other (deterministic) tests are genuine code breaks.

---

## Step 4 — Report

Print a scannable `## Test results` section:

- **Summary line** — `✓ <passed> passed / 0 failed` (green), or `⚠ <failed> failed / <passed> passed` (or `✗ compile error — suite did not run`).
- **Failures** (if any), grouped **Deterministic** then **Live (network)** — one entry each:
  `<test name> — <message>. Cause: <one line>. Fix near: <ClassName.Method | file:line>.`
- **Compile errors** (if the suite didn't run) — the assembly + first `error CS####` lines with `file:line`.

Do **not** auto-fix. If there are real (deterministic) failures, end by offering to fix them. Never commits or stages.
