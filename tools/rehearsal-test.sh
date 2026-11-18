#!/bin/bash
# tools/rehearsal.sh on a scratch game folder (nothing of the real one is touched, the game may run): stage as a first
# start and restore; stage with a cfg, without the framework and with a second zip, and restore. Each time the scratch
# folder must be as it was, byte for byte, the aside folders apart.
#   bash tools/rehearsal-test.sh <Mod> <a second zip>      (the mod's zip must be in release/; the second zip: any
#                                                           released zip of another mod, a Saddle Up one)
set -e
REPO="$(cd "$(dirname "$0")/.." && pwd)"
MOD="$1"; WITH="$2"
[ -n "$MOD" ] && [ -f "$WITH" ] || { echo "usage: bash tools/rehearsal-test.sh <Mod> <a second zip>"; exit 1; }
GUID="$(sed -n 's/.*public const string Guid = "\([^"]*\)".*/\1/p' "$REPO/src/$MOD/Plugin.cs" | head -1)"
[ -n "$GUID" ] || { echo "src/$MOD/Plugin.cs: no Guid"; exit 1; }
T="$(mktemp -d)"
mkdir -p "$T/game/BepInEx/plugins" "$T/game/BepInEx/config" "$T/saves"
P="$T/game/BepInEx/plugins"; C="$T/game/BepInEx/config"
for d in $MOD GK2DonkeyAPI GK2SaddleUp GK2ModTestBench GK2.Framework GraveZoom PerformanceHog; do mkdir -p "$P/$d"; echo "dev $d" > "$P/$d/$d.dll"; done
echo fw > "$P/GK2.Framework.dll"
for c in "$GUID.cfg" dreas.gk2.techsearch.cfg dreas.gk2.dev dreas.gk2.saddleup.cfg gk2.performancehog.cfg com.gravezoom.mod.cfg BepInEx.cfg; do echo "mine $c" > "$C/$c"; done
echo log > "$T/game/BepInEx/LogOutput.log"
echo save > "$T/saves/Steam_1.dat"
export REHEARSAL_TEST_GAME="$T/game" REHEARSAL_TEST_SAVES="$T/saves"
snap() { (cd "$T" && find game saves -type f | sort | xargs sha1sum); }
snap > "$T/before.txt"

echo "== 1: stage, a first start"
bash "$REPO/tools/rehearsal.sh" stage "$MOD"
echo "-- plugins now:"; ls "$P"; echo "-- config now:"; ls "$C"
[ -f "$P/$MOD/$MOD.dll" ] && ! grep -q "^dev " "$P/$MOD/$MOD.dll" || { echo "FAIL: the zip's DLL is not there"; exit 1; }
[ ! -e "$C/$GUID.cfg" ] || { echo "FAIL: a cfg is there on a first start"; exit 1; }
echo "== 1b: a second stage refuses"
bash "$REPO/tools/rehearsal.sh" stage "$MOD" && { echo "FAIL: staged twice"; exit 1; } || true
echo "written by the run" > "$C/$GUID.cfg"
echo "== 1c: restore"
bash "$REPO/tools/rehearsal.sh" restore
snap > "$T/after1.txt"
A1="$(ls -d "$T"/game/BepInEx/qol-rehearsal-aside-* | head -1)"
grep -q "written by the run" "$A1/after/$GUID.cfg" || { echo "FAIL: the run's cfg was not kept"; exit 1; }
diff "$T/before.txt" <(grep -v "qol-rehearsal-aside" "$T/after1.txt") && echo "PASS 1: the folder is as it was (the aside folder apart)"

sleep 1
echo "== 2: stage with a cfg, no framework, a second zip"
echo "MaxResults = 5" > "$T/old.cfg"
bash "$REPO/tools/rehearsal.sh" stage "$MOD" --cfg "$T/old.cfg" --no-framework --with "$WITH"
echo "-- plugins now:"; ls "$P"; echo "-- config now:"; ls "$C"
[ ! -e "$P/GK2.Framework" ] && [ ! -e "$P/GK2.Framework.dll" ] || { echo "FAIL: the framework stayed"; exit 1; }
grep -q "MaxResults = 5" "$C/$GUID.cfg" || { echo "FAIL: the cfg is not in"; exit 1; }
[ -d "$P/GK2SaddleUp" ] && ! grep -q "^dev " "$P/GK2SaddleUp/GK2SaddleUp.dll" || { echo "FAIL: the second zip's Saddle Up is not the extracted one"; exit 1; }
bash "$REPO/tools/rehearsal.sh" restore
snap > "$T/after2.txt"
diff "$T/before.txt" <(grep -v "qol-rehearsal-aside" "$T/after2.txt") && echo "PASS 2: the folder is as it was (the aside folders apart)"
echo "== 3: restore with nothing staged refuses"
bash "$REPO/tools/rehearsal.sh" restore && { echo "FAIL"; exit 1; } || echo "PASS 3"
rm -rf "$T"
