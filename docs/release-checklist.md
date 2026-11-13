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
`CecilScan-OwnedCount`, `CecilScan-TechTreeSearch`, `CecilScan-EventDecorations`, `FrameworkScan`, `HarmonyHarness-BuiltCount` (built, then run), each
mod's dependency manifest verified against the installed game (`tools/DepManifest`), and `check-dist.py`. All must end
green. `bash tools/release-scans.sh <Mod>` limits the two manifest checks (bin and dist) to that mod, for a release of
one mod while another's dist waits for its re-audit (the code scans still run over every mod); `check-dist.py` takes
the same mod list (2026-11-13: Owned Count's 1.0.4 dist fails against 1.012 until its own release).

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
2. Put the `dist/` DLLs into `BepInEx\plugins` over the dev builds.
3. Start the game, load a save, open a build desk, a chest tooltip, the Tech Tree, move a building, quit.
4. `BepInEx\LogOutput.log`: one `... loaded.` line per mod and no `[Warning` or `[Error` from any of them; the new
   cfg files hold the defaults.
5. Put the config files and the dev DLLs back (or rebuild without `ForDist`).

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

The user commits and tags; the release notes name the game build (`26624074`, 1.012.7 as of 2026-11-13).

## 7. The zip and the Nexus page

A mod released on its own (Tech Tree Search 1.0.1 was the first, 2026-10-29): `bash tools/build-dist.sh <Mod>` builds
that one into `dist/` and leaves the others' dist DLLs as released (their Unreleased changes stay unreleased under
their old numbers), then the scans of section 2, then

```
python tools/make-release-zip.py GK2TechTreeSearch   # release/GK2-Tech-Tree-Search-<version>.zip (named after the mod's title)
```

which runs `check-dist.py`, checks the version (Plugin.cs = csproj = the mod's first changelog heading, and present
in the dist DLL) and writes the zip holding `BepInEx/plugins/<Mod>/<Mod>.dll` alone, read back and compared with
`dist/` byte for byte: the Nexus upload, extracting into the game folder (a folder per mod, as the Saddle Up zips).
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
