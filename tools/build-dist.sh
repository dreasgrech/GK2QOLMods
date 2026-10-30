#!/bin/bash
# The release build (docs/release-checklist.md, section 3): the five mods built with -p:ForDist=true (no debug
# directory) and -p:Deploy=false (the game may be running), copied into dist/, then tools/check-dist.py. Stops at the
# first failure. Build output: release/logs/build-dist.log.
#
#   bash tools/build-dist.sh                 # all five
#   bash tools/build-dist.sh GK2TechTreeSearch   # only these: a release of one mod leaves the others' dist DLLs as released
#
# Afterwards: bash tools/release-scans.sh, then a dev build with the game closed (bin/ holds whichever was built last).
set -u
REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
LOG="$REPO/release/logs/build-dist.log"
mkdir -p "$REPO/release/logs"
: > "$LOG"

MODS=("$@")
[ ${#MODS[@]} -eq 0 ] && MODS=(GK2GlobalStorage GK2BuiltCount GK2OwnedCount GK2TechTreeSearch)  # Move Building retired 2026-10-30 (game 1.011 moves buildings itself)
for m in "${MODS[@]}"; do
  [ -f "$REPO/src/$m/$m.csproj" ] || { echo "no such mod: src/$m/$m.csproj"; exit 1; }
done
for m in "${MODS[@]}"; do
  echo "== $m"
  echo "===== $m" >> "$LOG"
  # --no-incremental: after a dev build, a ForDist build of unchanged sources would reuse the dev output (seen 2026-10-13)
  if ! (cd "$REPO/src/$m" && dotnet build -c Release --no-incremental -p:ForDist=true -p:Deploy=false >> "$LOG" 2>&1); then
    echo "BUILD FAILED: $m (see $LOG)"
    exit 1
  fi
  warn=$(grep -E "^ +[0-9]+ Warning\(s\)" "$LOG" | tail -1 | tr -dc '0-9')
  if [ "${warn:-0}" != "0" ]; then
    echo "WARNINGS in $m: $warn (see $LOG); no warning ships"
    exit 1
  fi
  cp "$REPO/src/$m/bin/Release/netstandard2.1/$m.dll" "$REPO/dist/$m.dll" || exit 1
  echo "   built, 0 warnings, copied to dist/"
done
echo "== check-dist"
python "$REPO/tools/check-dist.py" || { echo "check-dist FAILED"; exit 1; }
echo "DIST BUILT"
