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
`CecilScan-OwnedCount`, `CecilScan-TechTreeSearch`, `FrameworkScan`, `HarmonyHarness-BuiltCount` (built, then run), each
mod's dependency manifest verified against the installed game (`tools/DepManifest`), and `check-dist.py`. All must end
green.

## 3. The release build

The game may be running: a build is fine, only the deploy copy into the game needs it closed, and `dist/` is filled
from `bin/`, not from the game folder.

```
bash tools/build-dist.sh      # the five DLLs into dist/, then check-dist.py
bash tools/release-scans.sh   # section 2
```

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

The user commits and tags; the release notes name the game build (`26297960`, 1.011 as of 2026-10-30).

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
`release/` is not kept in git.

The page text is `media/GK2_<Mod>_Nexus.txt` (BBCode), its pictures in `media/<mod>/` (560 wide; the header
1300x372; boxes in `media/README.md`, cut with `tools/crop-shot.py`), hotlinked from the raw GitHub URL, so they
show only once `media/` is pushed. The first run of section 5 is done with the zip extracted into the game folder
(the loose dev DLL moved out first: two copies of a plugin would load twice).
