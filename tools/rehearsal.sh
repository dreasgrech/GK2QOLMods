#!/bin/bash
# The release rehearsal of docs/release-checklist.md section 5: the game folder as a player has it, with one mod's
# release zip.
#   bash tools/rehearsal.sh stage <Mod> [--cfg <file>] [--no-framework] [--with <zip>]...
#       every build of ours, every cfg of ours and the dev marker go aside, then release/<the mod's zip> is extracted
#       into the game folder. Without --cfg the mod starts on no cfg (a first start); --cfg <file> puts that file in
#       as the mod's cfg (an update: the previous release's cfg with values off their defaults). --no-framework sets
#       GK2 Mod Framework aside too. --with <zip> extracts another released zip as well (a mod a player has beside
#       this one: a Saddle Up zip from the GK2DonkeyAPI repository's release/).
#   bash tools/rehearsal.sh restore
#       the log and the cfgs the run wrote are kept in the aside folder (after/), the extracted folders removed,
#       everything put back, the saves compared before and after (stage copies them into the aside folder first:
#       a save made in the run is not undone by restore, the copies are there for that).
# Both refuse while the game runs. One rehearsal at a time: the staged one's aside folder
# (BepInEx\qol-rehearsal-aside-<date>-<time>) is named in BepInEx\qol-rehearsal-staged until restore; a staged
# rehearsal of the GK2DonkeyAPI repository (BepInEx\rehearsal-staged) refuses this one.
#
# What goes aside: in plugins every GK2* folder and file (the mods' dev builds and the dev tools; GK2.Framework*
# only with --no-framework), PerformanceHog and gk2mounts; in config every dreas.gk2.* file (the cfgs, the dev marker
# dreas.gk2.dev, the old dreas.gk2.techsearch.cfg a first start would take its settings from) and the perf tool's.
# Other people's mods stay.
#
# The script's own test: REHEARSAL_TEST_GAME and REHEARSAL_TEST_SAVES name a scratch game folder and a scratch saves
# folder (the running game is then not asked about: nothing of the real folders is touched).
set -e
REPO="$(cd "$(dirname "$0")/.." && pwd)"
G="${REHEARSAL_TEST_GAME:-$(cygpath -u "$(sed -n 's/.*<GameDir[^>]*>\([^<]*\)<\/GameDir>.*/\1/p' "$REPO/Directory.Build.props" | head -1)")}"
[ -d "$G" ] || { echo "no game folder at $G (Directory.Build.props GameDir)"; exit 1; }
B="$G/BepInEx"
STAGED="$B/qol-rehearsal-staged"
SAVES="${REHEARSAL_TEST_SAVES:-$(cygpath -u "$USERPROFILE")/AppData/LocalLow/Lazy Bear Games/Graveyard Keeper 2}"

running() { [ "$(tasklist | grep -ic graveyard)" != 0 ]; }
if [ -z "$REHEARSAL_TEST_GAME" ] && running; then echo "the game is running: close it first"; exit 1; fi

# the plugin folders a zip holds; a zip with anything outside BepInEx/plugins/<folder>/ is refused
zip_folders() { # zip
  local bad
  bad="$(unzip -Z1 "$1" | grep -v -E '^BepInEx/(plugins/([A-Za-z0-9._-]+/.*)?)?$' || true)"
  [ -z "$bad" ] || { echo "REFUSED: $1 holds entries outside BepInEx/plugins/<folder>/:" >&2; echo "$bad" >&2; return 1; }
  unzip -Z1 "$1" | sed -n 's#^BepInEx/plugins/\([A-Za-z0-9._-]*\)/.*#\1#p' | sort -u
}

case "$1" in
  stage)
    MOD="$2"
    [ -n "$MOD" ] && [ -f "$REPO/src/$MOD/Plugin.cs" ] || { echo "usage: bash tools/rehearsal.sh stage <Mod> [--cfg <file>] [--no-framework] [--with <zip>]..."; exit 1; }
    shift 2
    CFG=""; NOFW=0; WITH=()
    while [ $# -gt 0 ]; do
      case "$1" in
        --cfg) CFG="$2"; [ -f "$CFG" ] || { echo "no such cfg: $CFG"; exit 1; }; shift 2 ;;
        --no-framework) NOFW=1; shift ;;
        --with) [ -f "$2" ] || { echo "no such zip: $2"; exit 1; }; WITH+=("$2"); shift 2 ;;
        *) echo "unknown option: $1"; exit 1 ;;
      esac
    done
    [ -f "$STAGED" ] && { echo "a rehearsal is staged already ($(cat "$STAGED")): restore first"; exit 1; }
    [ -f "$B/rehearsal-staged" ] && { echo "a rehearsal of the GK2DonkeyAPI repository is staged ($(cat "$B/rehearsal-staged")): restore that one first"; exit 1; }
    # the mod's zip, named as tools/make-release-zip.py names it: the plugin's title with its words dashed, its version
    TITLE="$(sed -n 's/.*\[BepInPlugin(Guid, "\([^"]*\)", Version)\].*/\1/p' "$REPO/src/$MOD/Plugin.cs" | head -1)"
    VERSION="$(sed -n 's/.*const string Version = "\([^"]*\)".*/\1/p' "$REPO/src/$MOD/Plugin.cs" | head -1)"
    GUID="$(sed -n 's/.*public const string Guid = "\([^"]*\)".*/\1/p' "$REPO/src/$MOD/Plugin.cs" | head -1)"
    [ -n "$TITLE" ] && [ -n "$VERSION" ] && [ -n "$GUID" ] || { echo "src/$MOD/Plugin.cs: no title, Version or Guid found"; exit 1; }
    ZIP="$REPO/release/$(echo "$TITLE" | tr ' ' '-')-$VERSION.zip"
    [ -f "$ZIP" ] || { echo "no $ZIP: python tools/make-release-zip.py $MOD first"; exit 1; }
    ZIPS=("$ZIP" "${WITH[@]}")
    for z in "${ZIPS[@]}"; do zip_folders "$z" > /dev/null; done

    ASIDE_NAME="qol-rehearsal-aside-$(date +%Y-%m-%d-%H%M%S)"
    ASIDE="$B/$ASIDE_NAME"
    [ -e "$ASIDE" ] && { echo "$ASIDE exists"; exit 1; }
    mkdir -p "$ASIDE/plugins" "$ASIDE/config" "$ASIDE/after"
    echo "$ASIDE_NAME" > "$STAGED"
    for p in "$B"/plugins/GK2* "$B/plugins/PerformanceHog" "$B/plugins/gk2mounts"; do
      [ -e "$p" ] || continue
      n="$(basename "$p")"
      case "$n" in GK2.Framework*) [ "$NOFW" = 1 ] || continue ;; esac
      mv "$p" "$ASIDE/plugins/" && echo "aside plugins/$n"
    done
    for c in "$B"/config/dreas.gk2.* "$B/config/gk2.performancehog.cfg"; do
      [ -e "$c" ] || continue
      mv "$c" "$ASIDE/config/" && echo "aside config/$(basename "$c")"
    done
    # the saves, copied: a save loaded in the run has none of our other mods (Global Storage, the Donkey API), and a
    # save made then could not be undone otherwise. Quit without saving; if one was made, the copies are here.
    mkdir -p "$ASIDE/saves"
    cp -p "$SAVES"/Steam_* "$ASIDE/saves/"
    (cd "$SAVES" && sha1sum Steam_* > "$ASIDE/sha1-before.txt")
    echo "saves: $(ls "$ASIDE/saves" | wc -l) file(s) copied into the aside folder (quit the run without saving)"
    : > "$ASIDE/extracted.txt"
    for z in "${ZIPS[@]}"; do
      for f in $(zip_folders "$z"); do
        [ -e "$B/plugins/$f" ] && { echo "REFUSED: plugins/$f is there already (not ours to replace): nothing extracted from $(basename "$z"); run restore"; exit 1; }
        echo "$f" >> "$ASIDE/extracted.txt"
      done
      unzip -q "$z" -d "$G"
      echo "extracted $(basename "$z"): $(zip_folders "$z" | tr '\n' ' ')"
    done
    if [ -n "$CFG" ]; then cp "$CFG" "$B/config/$GUID.cfg"; echo "cfg: $(basename "$CFG") as config/$GUID.cfg"; else echo "cfg: none (a first start)"; fi
    [ "$NOFW" = 1 ] && echo "GK2 Mod Framework: aside" || echo "GK2 Mod Framework: as installed"
    echo "STAGED: $TITLE $VERSION as a player has it (the aside folder: $ASIDE_NAME)"
    ;;
  restore)
    [ -f "$STAGED" ] || { echo "nothing staged (no $STAGED)"; exit 1; }
    ASIDE="$B/$(cat "$STAGED")"
    [ -d "$ASIDE/plugins" ] && [ -f "$ASIDE/extracted.txt" ] || { echo "$ASIDE is not a staged rehearsal's folder"; exit 1; }
    cp -p "$B/LogOutput.log" "$ASIDE/after/" 2>/dev/null || true
    # what the run wrote into config (every cfg of ours there now is the run's: ours all went aside)
    for c in "$B"/config/dreas.gk2.*; do [ -e "$c" ] && mv "$c" "$ASIDE/after/" && echo "kept the run's config/$(basename "$c")"; done
    while read -r f; do
      case "$f" in ""|*[!A-Za-z0-9._-]*|.|..) echo "extracted.txt: not a folder name: '$f'"; exit 1 ;; esac
      [ -d "$B/plugins/$f" ] && rm -rf "$B/plugins/$f" && echo "removed the extracted plugins/$f"
    done < "$ASIDE/extracted.txt"
    for p in "$ASIDE"/plugins/*; do
      [ -e "$p" ] || continue
      n="$(basename "$p")"
      [ -e "$B/plugins/$n" ] && { echo "plugins/$n is there (a copy made during the run?): not restoring over it; move it away and run restore again"; exit 1; }
      mv "$p" "$B/plugins/" && echo "back plugins/$n"
    done
    for c in "$ASIDE"/config/*; do
      [ -e "$c" ] || continue
      mv "$c" "$B/config/" && echo "back config/$(basename "$c")"
    done
    (cd "$SAVES" && sha1sum Steam_* > "$ASIDE/sha1-after.txt")
    if diff -q "$ASIDE/sha1-before.txt" "$ASIDE/sha1-after.txt" > /dev/null; then echo "saves identical"; else echo "SAVES CHANGED (a save made during the run?):"; diff "$ASIDE/sha1-before.txt" "$ASIDE/sha1-after.txt" || true; fi
    rm -f "$STAGED"
    echo "RESTORED (the run's log and cfgs: $ASIDE/after)"
    ;;
  *) echo "usage: stage <Mod> [--cfg <file>] [--no-framework] [--with <zip>]... | restore"; exit 1 ;;
esac
