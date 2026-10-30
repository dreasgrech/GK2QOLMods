#!/bin/bash
# Every offline check of this repo before a release (docs/release-checklist.md, section 2), no game launch needed.
# Run after tools/build-dist.sh: the manifest checks read the release DLLs in bin/. Each check's output goes to
# release/logs/release-scans.log; the summary lists each with its exit code. Exits 1 if any check failed.
#
#   bash tools/release-scans.sh
set -u
REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
LOG="$REPO/release/logs/release-scans.log"
mkdir -p "$REPO/release/logs"
: > "$LOG"
GAME="$(sed -n 's:.*<GameDir[^>]*>\([^<]*\)</GameDir>.*:\1:p' "$REPO/Directory.Build.props" | head -1)"
MANAGED="$GAME/GraveyardKeeper2_Data/Managed"
fails=0
total=0

run() { # folder, label, command...
  local d="$1" label="$2"
  shift 2
  echo "===== $label" >> "$LOG"
  (cd "$d" && "$@" >> "$LOG" 2>&1)
  local rc=$?
  echo "----- exit $rc ($label)" >> "$LOG"
  total=$((total + 1))
  if [ $rc -eq 0 ]; then echo "ok    $label"; else echo "FAIL  $label (exit $rc)"; fails=$((fails + 1)); fi
}

T="$REPO/tests"
for t in CecilScan-BuiltCount CecilScan-GlobalStorage CecilScan-OwnedCount CecilScan-TechTreeSearch StaticScan-GlobalStorage FrameworkScan NoLinqScan; do
  run "$T/$t" "$t" dotnet run -c Release
done
run "$T/HarmonyHarness-BuiltCount" "HarmonyHarness build" dotnet build -c Release
run "$T/HarmonyHarness-BuiltCount" "HarmonyHarness run" ./bin/Release/net48/HarmonyHarness-BuiltCount.exe
run "$REPO/tools/DepManifest" "DepManifest build" dotnet build -c Release
for m in GK2BuiltCount GK2GlobalStorage GK2OwnedCount GK2TechTreeSearch; do  # Move Building retired 2026-10-30
  run "$REPO" "DepManifest verify $m.dll" dotnet tools/DepManifest/bin/Release/net8.0/DepManifest.dll verify "src/$m/bin/Release/netstandard2.1/$m.dll" "$MANAGED"
done
run "$REPO" "check-dist" python tools/check-dist.py
echo "$((total - fails)) of $total checks green (log: $LOG)"
[ $fails -eq 0 ] || exit 1
