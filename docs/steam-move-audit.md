# Moving to the Steam install: game-version audit (2026-09-29)

The move was done the same day; see "Outcome" at the end. Old install: `C:\Games\Graveyard Keeper 2` (the audited game).
New install: `C:\Program Files (x86)\Steam\steamapps\common\Graveyard Keeper 2`, Steam build 25601286, no BepInEx yet.

## How the mods handle a game version change today

All six plugins (`GK2BuiltCount`, `GK2GlobalStorage`, `GK2Mounts`, `GK2MoveBuilding`, `GK2OwnedCount`, and the
`tools/GK2MountProbe`) do the same thing in `Awake`: compare `Assembly-CSharp.dll`'s module version id (MVID) with
one hard-coded audited value, `97f92f0d-a093-49fe-ba4d-74aa62fae81e`. On a mismatch, with `Safety.DisableOnGameUpdate`
= true (the default), they log an error and patch nothing; the game runs unmodified. The established procedure for
an update (Global Storage FINDINGS, updates 25509347 and 25533739) is: diff the game's code, re-run the static scans,
then change only the MVID and the version string.

| | Old (`C:\Games`) | Steam |
|---|---|---|
| `Assembly-CSharp.dll` MVID | `97f92f0d-a093-49fe-ba4d-74aa62fae81e` | `3ffadd70-169d-402a-bcdf-59c322dfc26d` |
| `LazyBearTechnology.dll` MVID | `43628768-474d-4d5b-9580-61549811b810` | `8a7a5e04-396d-4525-a779-3f0de1341398` |

**Result on the Steam install as the mods are now: every mod disables itself** ("Game code changed since this mod
was audited"). Safe, but none of them runs.

## What changed in the game's code (method-level diff, Cecil)

- `Assembly-CSharp`: 72 method bodies changed, 169 members added, 79 removed. Every removed member is compiler-generated
  (renumbered lambdas, closure classes, a state machine); no named game member was removed.
- `LazyBearTechnology`: only `ExceptionReportWindow` (a field replaced, 6 methods).

**Patched methods that changed: one.** `SaveSystem.Save` (Mounts' prefix hands cargo back before a save, the postfix
loads it back): it now refuses to save when the save drive has under 100 MB free (`HasEnoughFreeDiskSpaceForSave`,
dialog, `callbackUnsuccessful`). The prefix still runs first and the postfix still reloads the cart on the refused
path, so nothing is lost either way. Also `GetNameForNewSlot` prefixes the platform name when checking existing slots.

**Unpatched code the mods rely on, changed:**
- `EnergySystem.StartSleeping`: prefetches free disk space before the save. Harmless.
- `WgoData.PrepareForGame`: now calls `SyncStoredInventoryFilters` (copies the definition's white/black list onto the
  stored inventory when they differ). Mounts' temporary donkey passes through it; the filters come from the same
  definition, so nothing changes for the cart.
- `MovementAdjustComponentBase` / `WgoMovementAdjustComponent`: moving objects now snap to their own world's recast
  graph (`GetRecastGraph`, by `WorldId`), within 0.1, and only when a node was found. Mounts' `GroundHeight` reads the
  player's scene graph directly (still what the game uses for the player's scene); optional hardening: check
  `nn.node != null` as the game now does.
- `ChunkManager`: the chunk size is now per platform (`ResolveChunkSize`); PC keeps 18 × 30 × 10. Move Building's
  chunk (un)registration goes through `Wgo`, unchanged.
- `Item` / `Inventory` / `UIBaseChestWindowData` / `MultiInventoryWidget(Data)`: bag rules (which items may go into
  a bag) and inventory filter sync; a redraw hook when item availability changes. No mod touches bags or filters.
- `ZombieWgoData`: the caretaker aborts cleanly when the inventory it walks to is gone (null guards). Makes moving
  or removing a storage (Move Building, Global Storage) safer.
- `CraftComponent`: stale pre-finish clearing, output only added when non-empty. Move Building only unsubscribes
  from `OnStatusChanged`, unchanged.

**Static scans against the Steam code** (copies of the repo's tests, pointed at the Steam `Managed` folder):
Mounts ALL PASS (217 IL refs resolved), Move Building ALL PASS (87), Owned Count ALL PASS (41), Global Storage
static suite all OK and the base-call check passes, Built Count: every member present with the same shape.

**Save format:** the Steam build adds save fixes for versions 1007 and 1007.1 (`RollBackEarlyFightA4Start`,
`RestoreWonFightA4Stage`, `ClearStalePanicReductionMachinePreFinish`, ...). Both installs use the same save folder
(`%USERPROFILE%\AppData\LocalLow\Lazy Bear Games\Graveyard Keeper 2`). A save written by the Steam build may not load
in the old one.

## What needs fixing before the move

1. **The audited MVID** in all six plugins: the audit above clears the Steam build, so the new MVID can be accepted
   (per the procedure: MVID + version string; Mounts stays 0.1.0). Accepting both old and new is harmless while
   both installs exist.
2. **The guard covers only `Assembly-CSharp`.** Every plugin also references `LazyBearTechnology.dll` (Mounts uses
   its sound player, Built Count its widget base class). An update that changed only that DLL would not be
   noticed. Add its MVID to the check.
3. **The game path is hard-coded** in 6 plugin `.csproj` files (`GameDir`), 7 test projects (`Program.cs` paths and
   `.csproj` hint paths into `BepInEx\core`), and 3 docs (`src/GK2BuiltCount/README.md`,
   `src/GK2GlobalStorage/FINDINGS.md`, `tests/README.md`). One shared `Directory.Build.props` at the repo root would
   set it once. The Steam folder is writable by the user (Users: FullControl), so deploying there works.
4. **The deploy step** says it only copies when the game is closed, but it copies anyway (seen twice this session).
   A real check would stop a half-written DLL from ever meeting a running game.
5. **For future updates:** the method-level diff used here (scratchpad `bepcheck`) could live in `tests/` so every
   update gets the same check.

## Before the first Steam launch

Back up the save folder. Once the Steam build has saved, keep playing there rather than going back to the old copy.

## Outcome (2026-09-29)

All five fixes were made and the move done:
1. The six plugins accept only the Steam build: `Assembly-CSharp` `3ffadd70-…` **and** `LazyBearTechnology`
   `8a7a5e04-…` (the guard now checks both). Versions: Built Count 1.0.4, Global Storage 1.1.3, Move Building 1.1.4,
   Owned Count 1.0.2; Mounts stays 0.1.0. `dist/` updated.
2. One game path: `Directory.Build.props` at the repo root (`GameDir`, default the Steam folder; `-p:GameDir=...`
   overrides). Plugins use it directly; tests through `tests/Directory.Build.props` and `tests/GamePaths.cs`.
3. `DeployToGame.targets`, imported by every plugin: copies only when no `GraveyardKeeper2.exe` is running, otherwise
   a build warning "built but NOT deployed" (tested with a stand-in process of that name: the installed DLL untouched).
4. Mounts' two navmesh reads accept a hit only when a node was found (`nn.node != null`), as the game now does.
5. `tests/GameDiff`: the member diff used for this audit, now the first step of every game update (`tests/README.md`).

Carried into the Steam folder (copies; the old folder untouched): `winhttp.dll`, `doorstop_config.ini`,
`.doorstop_version`, `BepInEx\core`, `BepInEx\config` (`BepInEx.cfg` + the six `dreas.gk2.*.cfg`), the dev dumps
(`GK2-cargo-sheet`, `GK2-sprites`, `GK2-*.txt/.csv/.json`), empty `patchers` / `plugins`. All byte-identical to the
originals. The plugins were built against the Steam build and deployed there, byte-identical to the build output.

Verification against the Steam build, run from the repo: Cecil scans Mounts / Move Building / Owned Count ALL PASS
(217 / 87 / 41 references resolved), Global Storage static suite 24 OK and base-call check passing, Built Count Cecil
scan and HarmonyX harness ALL PASS, `GameDiff` reproducing 72 changed / 169 added / 79 removed.

Still to do, in game: first launch (BepInEx loads, each plugin logs `… loaded.` without "Game code changed"), then
each mod's test list. The Steam copy's repo shows the carried-over files as untracked, for the user to commit.

## First run on Steam (2026-09-29 16:37)

- **BepInEx:** 5.4.23.5 loaded 5 plugins, all "loaded", no errors and no "Game code changed". Built Count and Global
  Storage logged their old version numbers (1.0.3, 1.1.2) because their log line had its own copy of the version;
  both now use one `Version` constant (fixed, rebuilt, installed and in `dist/`; the loader already showed 1.0.4/1.1.3).
- **Mounts in play:** mounted in Cart style, picked a corpse and wood onto the cart, set both down with the interact
  key, dismounted for work. No warnings.
- **Game (Player.log):** loader detected as before; no exceptions. The save (version 1.006) got the game's save fixes
  to 1.007.1 (moved a bush and `chest_home` by 0.3); the new "Updated inventory filters" lines came from a survey table
  and a conveyor chest. "No data for GameLogicDef fish_*_restore" and the BoxCollider warnings were in the old build's
  log too.
- **Save:** slept and woke, the game checked free space (35 GB) and wrote `Steam_1.dat` (1.007.1, not an autosave).
