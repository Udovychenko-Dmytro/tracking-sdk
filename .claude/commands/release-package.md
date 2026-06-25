---
description: Build a UPM-installable .tgz of the tracking SDK after a full pre-flight gate (version consistency, CHANGELOG entry, green tests). Never bumps the version; never commits or stages.
argument-hint: "[expected version, e.g. 1.2.0] (optional — asserted against the package if given)"
allowed-tools: Bash, Read, Grep, Glob
---

# /release-package — build the UPM `.tgz` archive

Produces `dist/com.dmytroudovychenko.tracking-<version>.tgz` via `npm pack` — the canonical UPM artifact, installable with **Package Manager → Add package from tarball…** and attachable to a GitHub release. `npm pack` respects the package `.npmignore` (drops OS cruft, keeps `Runtime/`, `Tests/`, `Samples~/`, docs, and all `.meta`).

It is **release-gated**. Releasing/versioning is a user action — this command **never bumps the version, never edits source, never commits, never stages**. It verifies the package is release-ready and, only if every gate passes, packs it. **Abort with a clear message on any gate failure.**

The argument (`$ARGUMENTS`) is an optional expected version; if given, it must match the package version or the command stops.

> **Timeout:** the tests gate is the headless EditMode run (a few minutes). Run it with a large timeout (up to `600000` ms); the Bash tool will usually **auto-background** it — wait for the completion `<task-notification>`. The Editor must be **closed** (single-instance lock).

---

## Step 1 — Version consistency gate

The version lives in **three** places (a test pins them). Extract all three and assert they're equal; if an argument was passed, assert it matches too.

```bash
ROOT="$(git rev-parse --show-toplevel)"
PKG="$ROOT/tracking-sdk/Packages/com.dmytroudovychenko.tracking"
ver_q() { grep -oE '"[0-9]+\.[0-9]+\.[0-9]+[-.0-9A-Za-z]*"' "$1" | head -1 | tr -d '"'; }
PKG_VER=$(ver_q "$PKG/package.json")
SDK_VER=$(grep -E 'VERSION *= *"' "$PKG/Runtime/TrackingSdk.cs" | grep -oE '"[0-9][^"]*"' | head -1 | tr -d '"')
SMOKE_VER=$(grep -E 'TrackingSdk\.VERSION' "$PKG/Tests/Editor/SmokeTests.cs" | grep -oE '"[0-9][^"]*"' | head -1 | tr -d '"')
ARG_VER="$ARGUMENTS"
echo "package.json   : $PKG_VER   ($PKG/package.json)"
echo "TrackingSdk.cs : $SDK_VER   ($PKG/Runtime/TrackingSdk.cs:VERSION)"
echo "SmokeTests.cs  : $SMOKE_VER ($PKG/Tests/Editor/SmokeTests.cs)"
if [ -z "$PKG_VER" ] || [ "$PKG_VER" != "$SDK_VER" ] || [ "$PKG_VER" != "$SMOKE_VER" ]; then
  echo "ABORT: version mismatch across the 3 pinned places — fix them, then re-run."; exit 1
fi
if [ -n "$ARG_VER" ] && [ "$ARG_VER" != "$PKG_VER" ]; then
  echo "ABORT: requested version '$ARG_VER' != package version '$PKG_VER'."; exit 1
fi
echo "VERSION OK: $PKG_VER"
```

On mismatch, report each location's value with its path and **stop** — do not edit any of them.

---

## Step 2 — CHANGELOG gate

The version must have a **released** section in `CHANGELOG.md` — not sit only under `[Unreleased]`.

```bash
ROOT="$(git rev-parse --show-toplevel)"
PKG="$ROOT/tracking-sdk/Packages/com.dmytroudovychenko.tracking"
PKG_VER=$(grep -oE '"[0-9]+\.[0-9]+\.[0-9]+[-.0-9A-Za-z]*"' "$PKG/package.json" | head -1 | tr -d '"')
if grep -qE "^## \[$PKG_VER\] - [0-9]{4}-[0-9]{2}-[0-9]{2}" "$PKG/CHANGELOG.md"; then
  echo "CHANGELOG OK: released section for $PKG_VER present."
else
  echo "ABORT: CHANGELOG.md has no released '## [$PKG_VER] - YYYY-MM-DD' section."
  echo "Move the [Unreleased] entries to [$PKG_VER] (per CLAUDE.md), then re-run. This command does not edit it."
  exit 1
fi
```

If the version is still unreleased, **stop** and tell the user to promote `[Unreleased]` → `[<version>]` themselves.

---

## Step 3 — Tests-green gate

Run the full headless EditMode suite (all tests, incl. the live ones). Abort if not green. For detailed failure analysis, point the user to `/check-tests`.

```bash
ROOT="$(git rev-parse --show-toplevel)"; PROJ="$ROOT/tracking-sdk"
VER=$(awk '/^m_EditorVersion:/{print $2}' "$PROJ/ProjectSettings/ProjectVersion.txt")
UNITY="/Applications/Unity/Hub/Editor/$VER/Unity.app/Contents/MacOS/Unity"
if [ -f "$PROJ/Library/EditorInstance.json" ]; then
  PID=$(grep -o '"process_id" : [0-9]*' "$PROJ/Library/EditorInstance.json" | grep -o '[0-9]*')
  if [ -n "$PID" ] && ps -p "$PID" >/dev/null 2>&1; then
    echo "ABORT: Unity Editor is open (PID $PID) — close it first."; exit 1
  fi
fi
[ -x "$UNITY" ] || { echo "ABORT: Unity not at $UNITY."; exit 1; }
rm -f "$ROOT/Temp/editmode.xml" "$ROOT/Temp/editmode.log"
"$UNITY" -runTests -batchmode -nographics -projectPath "$PROJ" -testPlatform EditMode \
  -testResults "$ROOT/Temp/editmode.xml" -logFile "$ROOT/Temp/editmode.log"
echo "unity exit=$?"
RUN=$(grep -m1 -o '<test-run[^>]*>' "$ROOT/Temp/editmode.xml" 2>/dev/null)
echo "$RUN"
if [ -z "$RUN" ]; then
  echo "ABORT: no test-run — likely a compile error:"; grep -iE 'error CS[0-9]+|Compilation failed' "$ROOT/Temp/editmode.log" | head; exit 1
fi
FAILED=$(printf '%s' "$RUN" | grep -oE 'failed="[0-9]+"' | grep -oE '[0-9]+')
if [ "${FAILED:-1}" != "0" ]; then
  echo "ABORT: $FAILED test(s) failed — run /check-tests for analysis, fix, then re-run."; exit 1
fi
echo "TESTS GREEN."
```

---

## Step 4 — Pack

From the package root, `npm pack` into `dist/` (respects `.npmignore`):

```bash
ROOT="$(git rev-parse --show-toplevel)"
PKG="$ROOT/tracking-sdk/Packages/com.dmytroudovychenko.tracking"
mkdir -p "$ROOT/dist"
( cd "$PKG" && npm pack --pack-destination "$ROOT/dist" )
echo "npm pack exit=$?"
ls -la "$ROOT/dist"/*.tgz
```

Produces `dist/com.dmytroudovychenko.tracking-<version>.tgz`.

---

## Step 5 — Verify & report

Inspect the artifact and confirm it is complete and clean:

```bash
ROOT="$(git rev-parse --show-toplevel)"
PKG_VER=$(grep -oE '"[0-9]+\.[0-9]+\.[0-9]+[-.0-9A-Za-z]*"' "$ROOT/tracking-sdk/Packages/com.dmytroudovychenko.tracking/package.json" | head -1 | tr -d '"')
TGZ="$ROOT/dist/com.dmytroudovychenko.tracking-$PKG_VER.tgz"
echo "artifact: $TGZ"
echo "size    : $(wc -c < "$TGZ") bytes"
shasum -a 256 "$TGZ"
echo "--- contents ---"
tar -tzf "$TGZ"
```

Confirm the listing **contains** `package/package.json`, `package/README.md`, `package/CHANGELOG.md`, `package/LICENSE.md`, `package/Runtime/`, `package/Samples~/`, and `.meta` files; and **excludes** `Library/`, `Temp/`, `dist/`, `.DS_Store`, `._*`. Flag anything missing or any cruft that slipped in.

Print a scannable `## Release package` section:
- **Gates** — `✓ version <v> (3/3)` · `✓ CHANGELOG` · `✓ tests <passed>/0`.
- **Artifact** — path, size, sha256.
- **Contents check** — `✓ Runtime/Tests/Samples~/.meta present, no cruft` (or the issue).
- **Install** — *Unity → Package Manager → + → Add package from tarball…* → select the `.tgz`; and that it's attachable to a GitHub release.
- **Reminder** — version unchanged, nothing committed or staged; `dist/` is an upload-only build artifact.
