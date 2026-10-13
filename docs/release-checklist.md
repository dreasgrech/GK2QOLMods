# Release checklist

What to do before `dist/` goes public, in order. Written 2026-10-13 from the first release audit; keep it current.

## 1. The code

- Every warning in the game's log is fixed (`BepInEx\LogOutput.log` of the last session: no `[Warning` or `[Error`
  lines from our plugins).
- Move Building's `Debug.FaultInjection` is in a player's cfg only with the developer marker file
  `BepInEx\config\dreas.gk2.dev` (the same marker unlocks the GK2 Donkey API's dev tools).
- Each mod's `Version` constant in `Plugin.cs` is the version in `README.md`'s table and in its `.csproj`
  (`<Version>`, the assembly version; four of them lagged on 2026-10-13).
- The audited game: `AuditedGameMvid` / `AuditedLazyBearMvid` in every `Plugin.cs` are the installed game's
  (`tests/GameDiff` after any update; the audit notes are `docs/game-update-*.md`). A dist DLL pinned to an older
  build disables itself on every player's first start ("Game code changed since this mod was audited"); on
  2026-10-13 all five dist DLLs were in that state.

## 2. The offline checks (no game launch)

In `tests/`, each with `dotnet run -c Release`: `CecilScan-GlobalStorage`, `StaticScan-GlobalStorage`,
`CecilScan-BuiltCount`, `CecilScan-MoveBuilding`, `CecilScan-OwnedCount`, `CecilScan-TechSearch`, `FrameworkScan`;
`HarmonyHarness-BuiltCount` with `dotnet build -c Release` then `bin\Release\net48\HarmonyHarness-BuiltCount.exe`.
All must end green.

## 3. The release build

The game may be running: a build is fine, only the deploy copy into the game needs it closed, and `dist/` is filled
from `bin/`, not from the game folder.

```
for %m in (GK2GlobalStorage GK2BuiltCount GK2MoveBuilding GK2OwnedCount GK2TechSearch) do (
  cd src\%m && dotnet build -c Release --no-incremental -p:ForDist=true -p:Deploy=false && cd ..\..
  copy src\%m\bin\Release\netstandard2.1\%m.dll dist\
)
python tools\check-dist.py
```

`-p:ForDist=true` leaves out the debug directory: without it the DLL names its PDB with this machine's user folder
(every dist DLL did, until 2026-10-13). `check-dist.py` fails on any baked path, a `.pdb` reference, or a pinned
MVID that is not the one in `Plugin.cs`. `--no-incremental` matters: after a dev build, a `ForDist` build of the
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
`[Debug]` section and no scratch cfg file was written.

## 6. Hand-off

The user commits and tags; the release notes name the game build (`25752683`, 1.009 hotfix as of 2026-10-13).
