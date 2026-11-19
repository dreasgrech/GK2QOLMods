# Release checklist

What to do before `dist/` goes public, in order. Written 2026-10-13 from the first release audit; keep it current.

## 1. The code

- Every warning in the game's log is fixed (`BepInEx\LogOutput.log` of the last session: no `[Warning` or `[Error`
  lines from our plugins).
- (Move Building, retired 2026-10-30, had its `Debug.FaultInjection` behind the developer marker file
  `BepInEx\config\dreas.gk2.dev`, the marker that unlocks the GK2 Donkey API's dev tools.)
- Each mod's `Version` constant in `Plugin.cs` is the version in `README.md`'s table and in its `.csproj`
  (`<Version>`, the assembly version; four of them lagged on 2026-10-13).
- The audited game: the audit notes (`docs/game-update-*.md`, from `tests/GameDiff` after any update) cover the
  installed game. The build embeds the dependency manifest (`docs/dependency-check.md`); `check-dist.py` verifies
  each dist DLL's manifest against the installed game, so a dist whose members changed or vanished since it was
  built fails the check. (Before the manifest, 2026-10-13: all five dist DLLs pinned an older build and would have
  switched themselves off on every player's first start.)

## 2. The offline checks (no game launch)

`bash tools/release-scans.sh` (after the release build of section 3: the manifest checks read its DLLs) runs them
all and lists each as ok or FAIL, exit 1 on any failure, the output in `release/logs/release-scans.log`: in `tests/`
`CecilScan-GlobalStorage`, `StaticScan-GlobalStorage`, `CecilScan-BuiltCount`,
`CecilScan-OwnedCount`, `CecilScan-TechTreeSearch`, `CecilScan-EventDecorations`, `FrameworkScan`, `HarmonyHarness-BuiltCount` and `HarmonyHarness-EventDecorations` (built, then run), each
mod's dependency manifest verified against the installed game (`tools/DepManifest`), and `check-dist.py`. All must end
green. `bash tools/release-scans.sh <Mod>` limits the two manifest checks (bin and dist) to that mod, for a release of
one mod while another's dist waits for its re-audit (the code scans still run over every mod); `check-dist.py` takes
the same mod list (2026-11-13: Owned Count's 1.0.4 dist fails against 1.012 until its own release).

The translations (since 2026-11-16; the mods named in `tools/l10n.json`, the ones with a settings page): the scans
end with `python -I lib/GK2DreasUI/tools/l10n-sync.py --check` (each mod's `Languages/en.csv` is what its build
gives; the window's own words in its `Languages/<id>.csv` are the library's; no shipped line the mod would leave
out) and the window library's `tests/TranslationScan` (the translator mistake catalogue, then every shipped file
read as the game reads it). After a change of a row, a label or a description, or of the library's pointer: run the
sync without `--check` and commit `src/<Mod>/Languages/` with the change. A window word is reworded in the library.

## 3. The release build

The game may be running: a build is fine, only the deploy copy into the game needs it closed, and `dist/` is filled
from `bin/`, not from the game folder.

```
bash tools/build-dist.sh      # the five DLLs into dist/, then check-dist.py
bash tools/release-scans.sh   # section 2
```

Both take a mod list (`bash tools/build-dist.sh GK2TechTreeSearch`): `build-dist.sh` then checks only the mods it built.

`build-dist.sh` builds each with `dotnet build -c Release --no-incremental -p:ForDist=true -p:Deploy=false`, stops on
a failed build or any compiler warning, copies the DLLs into `dist/` and runs `check-dist.py` (build output in
`release/logs/build-dist.log`).

`-p:ForDist=true` leaves out the debug directory: without it the DLL names its PDB with this machine's user folder
(every dist DLL did, until 2026-10-13). `check-dist.py` fails on any baked path, a `.pdb` reference, a missing
dependency manifest, or a manifest whose members changed or vanished in the installed game. `--no-incremental` matters: after a dev build, a `ForDist` build of the
unchanged sources reuses the dev output and the check fails on the PDB path (seen 2026-10-13).

Afterwards build once more without `ForDist` (and with the game closed) so the DLLs in the game are the dev builds
again; `bin/` holds whichever was built last.

## 4. The texts

- `dist/README.md`: versions, game build and date.
- `README.md`: the version table and each mod's section (settings, keys).

## 5. A first run as a player (this machine)

Rehearse the first start on a clean config, with the game closed:

1. Move `BepInEx\config\dreas.gk2.*.cfg` of these mods aside (they come back afterwards).
2. Put the `dist/` DLLs over the dev builds: each in its own folder, `BepInEx\plugins\<Mod>\<Mod>.dll` (the dev
   deploy's layout since 2026-11-16, the zips' from the start; a mod not built since then still lies loose in
   `plugins\`: one copy only, or the plugin loads twice).
3. Start the game, load a save, open a build desk, a chest tooltip, the Tech Tree, move a building, quit.
4. `BepInEx\LogOutput.log`: one `... loaded.` line per mod and no `[Warning` or `[Error` from any of them; the new
   cfg files hold the defaults.
5. Put the config files and the dev DLLs back (or rebuild without `ForDist`).

For a mod released as a zip (section 7), `tools/rehearsal.sh` does steps 1, 2 and 5 (since 2026-11-18):
`bash tools/rehearsal.sh stage <Mod>` sets every build of ours, every `dreas.gk2.*` file in `config` and the dev tools
aside and extracts `release/`'s zip of the mod's version, so the game folder is a player's with that one mod, on no
cfg; `--cfg <file>` puts a cfg in (an update: the previous release's cfg with values off their defaults),
`--no-framework` sets GK2 Mod Framework aside too, `--with <zip>` extracts another released zip beside it (a Saddle Up
zip from the GK2DonkeyAPI repository: two mods' rows in the game's Settings window). `bash tools/rehearsal.sh restore`
keeps the run's log and cfgs (`BepInEx\qol-rehearsal-aside-<date>-<time>\after`) and puts everything back. Both refuse
while the game runs. `bash tools/rehearsal-test.sh <Mod> <a second zip>` proves the script on a scratch folder.

Run 2026-10-13 with this dist, without the framework and with a stock `BepInEx.cfg`: all five loaded with their
versions, a building moved, the Tech Tree search box shown and searching; no warning, error or exception in the log;
the fresh cfgs at defaults. Second run the same day with the marker lock built: Move Building's fresh cfg has no
`[Debug]` section and no scratch cfg file was written. Both runs were on the 1.009-hotfix dist; the 1.010 dist of
the same evening (new pins, one Global Storage eligibility branch: `docs/game-update-25910882.md`) has not been
rehearsed.

## 6. Versions, changelog and hand-off

Released versions are never reused: a release with any change gets a new number (major.minor.patch: fixes only the
patch, new features or settings the minor), in the mod's `Plugin.cs`, its csproj and the README table. Every change
goes under the mod's **Unreleased** in `CHANGELOG.md` when it is made; at release that heading becomes the version and
date, and the entry is the Nexus changelog text.

The user commits and tags; the release notes name the game build (`26734308`, 1.012.9 as of 2026-11-18).

## 7. The zip and the Nexus page

A mod released on its own (Tech Tree Search 1.0.1 was the first, 2026-10-29): `bash tools/build-dist.sh <Mod>` builds
that one into `dist/` and leaves the others' dist DLLs as released (their Unreleased changes stay unreleased under
their old numbers), then the scans of section 2, then

```
python tools/make-release-zip.py GK2TechTreeSearch   # release/GK2-Tech-Tree-Search-<version>.zip (named after the mod's title)
```

which runs `check-dist.py`, checks the version (Plugin.cs = csproj = the mod's first changelog heading, and present
in the dist DLL) and writes the zip holding `BepInEx/plugins/<Mod>/<Mod>.dll` and, for a mod with translations,
`BepInEx/plugins/<Mod>/Languages/*.csv` from `src/<Mod>/Languages/` (committed ones only: uncommitted files are
refused), each read back and compared byte for byte: the Nexus upload, extracting into the game folder (a folder per
mod, as the Saddle Up zips). `--dry-run` runs every check and lists what would be written, writing nothing.
The zips in `release/` are kept in git (since 2026-10-30): commit each new zip with its release; only
`release/logs/` is ignored. The tool refuses to overwrite a zip that exists (`--overwrite` only for one never uploaded).

The page text is `media/GK2_<Mod>_Nexus.txt` (BBCode), its pictures in `media/<mod>/` (560 wide; the header
1300x372; boxes in `media/README.md`, cut with `tools/crop-shot.py`), hotlinked from the raw GitHub URL, so they
show only once `media/` is pushed. The first run of section 5 is done with the zip extracted into the game folder
(the loose dev DLL moved out first: two copies of a plugin would load twice).

**The Steam Workshop** (Tech Tree Search 1.0.2 the first, 2026-11-13; the facts in `docs/mods-window-and-workshop.md`).
The same run of `make-release-zip.py` stages `workshop\<Mod>\` (not in git): the plugin folder as the zip holds it,
`modinfo.json` (id = the plugin GUID, name, version: the game's Mods window reads them), and the uploader's own files,
which it never sends: `Thumbnail.jpg` from `media/workshop/<Mod>/` (`tools/workshop-thumbnail.py <shot> <box> <Mod>`:
a square cut, 1024 wide, under 1 MB), `_description.bbcode` from `media/GK2_<Mod>_Workshop.txt` (Steam's BBCode: h1-h3,
b, i, url, img, list, olist, hr, code; under 8,000 bytes or the whole upload fails) and `_changenote.txt` from
`docs/nexus/<name>-<version>-workshop-changenote.txt` (else the Nexus note `docs/nexus/<name>-<version>-changelog.md`;
the Nexus note never links to Steam, the user 2026-10-30). `--no-zip` stages without a new zip, `--no-workshop` zips
only. The upload is the user's: the game launched through Steam (otherwise `SteamAPI_Init() failed` and nothing opens),
`"workshopCreatorMode": true` in `%USERPROFILE%\AppData\LocalLow\Lazy Bear Games\Graveyard Keeper 2\Mods\workshop.json`,
Shift+F11 in the main menu, "Create new workshop item", Type **Mod**, the linked folder `workshop\<Mod>`; a new item
starts Unlisted (set public on its Steam page; later uploads keep the visibility); no required items for these mods.
The gallery pictures (`media/workshop/<Mod>/gallery/`, `tools/workshop-gallery.py <shot> <box> <Mod> <name>`: the box
at the screenshot's own pixels, under 1 MB) go up by hand on the item's page. The DLL is the same bytes on both
channels, so a Nexus release and a Workshop upload are one version.

Run 2026-11-13 for Tech Tree Search 1.0.2 (game 1.012.7, the first Workshop item; the Nexus 1.0.1 DLL was identical
against 1.012, but its source had moved: the ungated `[measure]` log line removed 2026-10-30): versions raised in
`Plugin.cs`, the csproj and the READMEs, CHANGELOG's Unreleased closed; `build-dist.sh GK2TechTreeSearch` 0 warnings;
`release-scans.sh GK2TechTreeSearch` 13 of 13 green; `make-release-zip.py GK2TechTreeSearch`:
`GK2-Tech-Tree-Search-1.0.2.zip` 24,619 bytes (the DLL 54,784 bytes, byte-identical in `dist/`, the zip and
`workshop\GK2TechTreeSearch\`; a second `build-dist.sh` gave the same bytes; the version and no `[measure]` inside it);
the preview from the Steam shot of 2026-10-29 23:52 (box 1280,307-1920,947), three gallery cuts; the dist DLL put
into the game over the dev build for the user's hand check (log clean, no `[measure]` line). Uploaded by the user the same evening:
the Workshop item 3839100125 ("Upload complete" in Player.log; the page: 54.901 KB = the DLL plus `modinfo.json`, the
description without the framework section, which Steam players cannot have, the framework being Nexus-only); the Nexus
1.0.2 upload on 421 done by the user the same evening (the page refuses automated reads; the user's word).

Run 2026-11-18/19 for Tech Tree Search 1.1.0 (game 1.012.9; the options window from a row in the game's Settings
window, 10 languages, the two Japanese/Chinese/Korean fixes): versions raised, CHANGELOG's Unreleased closed, the two
notes and both page texts; `build-dist.sh GK2TechTreeSearch` 0 warnings; `release-scans.sh GK2TechTreeSearch` 19 of 19
green; `make-release-zip.py GK2TechTreeSearch`: `GK2-Tech-Tree-Search-1.1.0.zip` 145,122 bytes (the DLL 282,112 bytes,
byte-identical in `dist/`, the zip and `workshop\GK2TechTreeSearch\`, and 11 language files). The rehearsal with
`tools/rehearsal.sh` (section 5), the user playing:
- **Start 1** (the mod alone, no cfg, no GK2 Mod Framework): 1.1.0 loaded, the row in the Settings window, the options
  opened, the Tech Tree searched and jumped, Japanese pasted and found; no warning or error; the fresh cfg at the
  defaults (the 1.0.2 keys, the same binds). **Found there: the row's box empty in Japanese, Chinese and Korean** (a
  copy of the game's Screen Size row, whose value keeps the Latin font): fixed in the window library the same night,
  the full bench run 2470 passed, 0 failed, the zip rebuilt (`--overwrite`: the first one was never uploaded), start 1
  again: the word shown in the three languages, the two page pictures taken.
- **Start 2** (a cfg shaped as 1.0.2's with Results shown 5, the Framework, GK2 Saddle Up 0.17.3 from its zip): both
  mods' rows in one strip (Saddle Up's copy keeps it), both options opened, the Framework's Settings button opened the
  window, the cfg rewritten by 1.1.0 with 5 kept; no warning or error.
- **Start 3** (beside GK2 Saddle Up 0.17.2, a copy of the window library from before the rows): the mod's row under
  the game's rows, Saddle Up's red button above OK, the window whole, both opened; no warning or error.
The saves identical after each start.
Committed and pushed by the user (`e4f5724`), then uploaded the same night. The Workshop item 3839100125, read through
Steam's public file details (2026-11-19 00:45): updated 00:41:20, 346,843 bytes = the staged plugin folder and
`modinfo.json` to the byte (13 files), the description identical to the staged one (4,705 characters), public, and
the page pictures answer from `main`. The Nexus upload on 421 is the user's (the page refuses automated reads).

Run 2026-11-19 for Event Decorations 1.0.0 (game 1.012.9; the mod's first release, a new Nexus page and a new
Workshop item): the READMEs and the changelog brought in line with the mod, the two notes and both page texts;
`build-dist.sh GK2EventDecorations` 0 warnings; `release-scans.sh GK2EventDecorations` 19 of 19 green;
`make-release-zip.py GK2EventDecorations`: `GK2-Event-Decorations-1.0.0.zip` 180,545 bytes (the DLL 291,328 bytes
and 11 language files); `rehearsal-test.sh` passed with this zip. The rehearsal with `tools/rehearsal.sh`, the
user playing:
- **Start 1** (the mod alone, no cfg, no GK2 Mod Framework; 18:40 to 18:49): 1.0.0 loaded on "Halloween (Auto)", its
  row in the Settings window, "On today" saying Halloween is off; in a save, Halloween set to Always: the decorations
  up at once (12 of 12 groups, 140 items, 44 glows, 104 blocking colliders), the pumpkins off (71 of 140), the glow
  off (0 of 44), Defaults, own dates typed (11-01 to 11-20: up), the game's language switched through Turkish, Korean and
  Chinese to Japanese (each file read whole, no line missing) and the window seen in Japanese. One warning, a provoked one: a date mistyped as "11-1A"
  ("dates are month-day like 10-24; using the game's own dates"). The game's own log: four exceptions, all after the
  quit was asked for, in the game's own teardown (`DayNightDependentSound.OnDisable`, `UICraftHintWidget`,
  `UIObjectBubble.Hide` twice); the decorations' bundle holds no `DayNightDependentSound` (read with UnityPy). The
  saves identical. Every page picture but the ghost's is from this start (`media/README.md`).
- **Start 2** (the cfg start 1 left, Halloween on Always; GK2 Mod Framework, Tech Tree Search 1.1.0 and GK2 Saddle Up
  0.17.3 from their zips; 18:55 to 19:02): the four mods loaded, the Framework "Compatible" for each; three rows of
  ours in one strip (Saddle Up's copy of the window library keeps it, each mod makes its own row in it); the options
  opened from the row and from the Framework's Settings button (over `GK2ModsMenuWindow`); **a save loaded with the
  decorations up** (the game's own "Added conditional scene content [RuinedTemple_ContentPart_6_HalloweenData]" at
  the load, then 140 of 140 items); **the ghost out at night** (the game's `halloween_npc_ghost_start_action`, its
  steps, the keeper beside it: the page's ghost picture); the decorations taken down and put up once more from the
  window. In Japanese the rows of this mod and of Tech Tree Search show their word, Saddle Up 0.17.3's own row an
  empty box (its known fault, for Saddle Up's next version). No warning or error from any mod; the game's log has
  the same four exceptions after the quit as in start 1. The saves identical. Not seen by hand: the Halloween zombie
  head (whether a zombie of the user's has the one head the game swaps, 1051, was not checked; the in-game suite
  checks every row of the game's table), the walk-through, the ghost's morning walk home
  (`docs/event-decorations-tests.md` has what stays open).
