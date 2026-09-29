# GK2 Built Count (v1.0.4)

A BepInEx 5 plugin for Graveyard Keeper 2. Build menus show how many of each building already exist in that menu's area:

```
Timber stockpile (1)
Sawhorse (2)
Chopping Spot            <- none built, name unchanged
Woodworking Workbench I (1)
```

It only changes what the menu shows. Build rules, costs and saves are not touched. It is independent of GK2 Global Storage; the two plugins work together or alone.

- GUID: `dreas.gk2.builtcount`
- Game build audited: Steam build 25601286, Unity 6000.3.9f1 Mono, `Assembly-CSharp` MVID `3ffadd70-169d-402a-bcdf-59c322dfc26d`, `LazyBearTechnology` `8a7a5e04-396d-4525-a779-3f0de1341398`. v1.0.3 was audited against 25533739 (`97f92f0d-a093-49fe-ba4d-74aa62fae81e`); v1.0.2 was audited against 25509347 (`70946fdd-5c76-4d71-95c3-fea95051340d`); v1.0.0 and v1.0.1 were audited against build 25457344 (`dfae7296-aa7e-450f-92b4-4e617878a48d`)
- Needs: BepInEx 5.4.23.5, installed in the game folder (`C:\Program Files (x86)\Steam\steamapps\common\Graveyard Keeper 2`)

## Settings

File: `BepInEx\config\dreas.gk2.builtcount.cfg` in the game folder. It is created on the first launch.

| Setting | Default | Meaning |
|---|---|---|
| `General.Enabled` | true | Show counts. `false` makes menus look like vanilla. The value is read each time a row is drawn, but BepInEx reads the file at startup, so restart the game after editing it |
| `Safety.DisableOnGameUpdate` | true | If the game's code changed (the MVID differs), patch nothing and log an error |

## How it works

One Harmony **postfix** on `UIBuildingWidget.Redraw()`, the method that draws a single row in a build menu.

1. Skip the row if any of these is true:
   - The plugin is disabled.
   - The row has no building definition (the "Remove" row).
   - The building has a limit (`BuildingDef.HasLimits`); the game already shows `(x/max)` for these.
   - The build mode doesn't place a new object (`Upgrade`, `Script`).
   - The menu has no area.
2. Find the area the desk builds into: `BuildManager.WorldZone.Data`, falling back to the row's `WorldZoneData`.
3. Count the objects in that area's `wgoDataList` whose ID equals the building's `wgoId`. If the object turns into another object when destroyed (`WGODef.replaceToWgoOnDie`) **and that is a plain ID**, count that ID too. A scripted expression is never evaluated.
4. If the count is above 0, add ` (N)` to the row's name label.

The counts for an area are built once per frame and cached (the cache is also rebuilt if the area's object count changes), so drawing a whole tab walks the area's object list once. Errors are caught and logged once. A failure only means the menu shows no counts.

## Findings behind the design

All references are to the decompiled `Assembly-CSharp`.

- **Draw path:**
  - `UIBuildingWidget.Redraw()` (`UIBuildingWidget.cs:85-128`) is the only place the row name is set.
  - It writes `LLBase.L(data.Name)`, then appends `" (" + GetLimitsString() + ")"` for limited buildings, and never writes the label again.
  - Rows are pooled, but every `Redraw` resets the text, so the appended count can't stack up.
  - The only creator of these rows is `UIBuildingWindow.DrawTab` (`UIBuildingWindow.cs:181`). It passes `data.AssignedWgo.Data.WorldZoneData`, the area of the build desk you opened.
- **The game's existing counter:** `BuildingDef.GetLimitsString()` returns `currentLimitExpression.EvaluateInt()/limitMax`. It only exists for buildings with `limit_max > 0`. The Yard buildings have none, which is why no count showed.
- **What building creates:** `WgoBuildPointer.TryDoBuildAction` (`WgoBuildPointer.cs:146`) creates `new WgoData(buildData.WgoId, …)` and adds it to the scene. So the objects with that ID are what you built.
  - `UpgradeBuildPointer` creates nothing; it only runs expressions on the host object.
  - `Script` builds only run expressions.
  - The modes that create objects are `Place`, `ConveyorPlace`, `FightingPlace` and `FightBuilding`, the same set `GameBalance.CreateBuildCache` treats as buildable.
- **Area object list:** `WorldZoneData.wgoDataList` is a saved `List<SGuid>`, filled by `WorldZoneData.TryAddWgoData` for objects inside the area rectangle, and only for areas where `IsContainer && IsActive`. Each entry resolves with `WorldData.GetWgoData(SGuid)` (a dictionary lookup). The game walks the list the same way in `WorldZoneData.CountItemsOnTownPalettes`.
- **Build preview:** it is spawned with `isTempObject = true` and never added to an area (`BuildPointer.PrepareAndSpawnWgoBuildPointer`). The plugin skips temporary objects anyway.
- **`replaceToWgoOnDie`:** the game evaluates this expression for the same row in `UIBuildingWidgetData`'s constructor (`UIBuildingWidgetData.cs:58-61`), so evaluating it again adds no new risk.
- **Objects that change ID later:**
  - Craft results can replace an object with a new ID (`CraftDef.replaceWgoId`, handled in `WgoData.cs:1177-1201`).
  - So can scripts (`ChangeWgoData`/`ReplaceWgoData`).
  - These objects then count under their new ID. This was chosen as "exact matches only".

## Audit (v1.0.1)

A second review, done while v1.0.0 was running in the game, found and fixed three things.

1. **Wrong area in some layouts (fixed).**
   - v1.0.0 counted the area the desk *stands in* (`AssignedWgo.Data.WorldZoneData`).
   - The game builds into the area chosen by `BuildManager.TryEnable` → `Wgo.TryGetNearestBuilderWorldZone`. That is the nearest area within 10 units whose `WorldZoneDef.builderId` is this desk, and it can be a different area from the one the desk stands in.
   - The window is only ever opened by `BuildManager.OpenBuildingWindow` (from `TryEnable`, or from `Disable` after leaving build mode). `BuildManager.WorldZone` is always set before that.
   - `WorldZone.Data` is the saved `WorldZoneData` from `gameSceneData.worldZones` (`WorldZone.Spawn` → `Init(data)`), the same object that holds `wgoDataList`.
   - Now uses `LazySingleton<BuildManager>.Instance.WorldZone.Data`.
2. **Possible expression evaluation (removed).**
   - `replaceToWgoOnDie` is a `LazyExpression`. When its text is a plain ID, `Evaluate()` just returns the string. When it contains `( " + - / *`, `Evaluate()` runs the expression through the game's expression engine with an empty context, and that engine has functions with side effects.
   - The game evaluates it for the same row in `UIBuildingWidgetData`'s constructor, except for rows with module icons, which return earlier.
   - v1.0.1 only reads plain IDs (`HasPureValue` → `GetRawExpressionString()`) and never evaluates anything.
3. **Log noise (removed).** `GameBalance.GetData<T>` logs a warning when an ID is missing. v1.0.1 uses `GetDataOrNull<T>`, which is silent.

Also checked, no issue found:
- **Nothing else changes the name.** `UIBuildingWidget` has no subclasses. `nameLabel` is private and only written in `Redraw`, and `UIBuildingWindow` never reads it: gamepad tips use `DisplayedIngredients`, not the name.
- **Redraw frequency.** Rows are redrawn only when the window opens (`Redraw` → `DrawTab`), when you switch tabs, and on next/prev tab. There is no per-frame redraw, so the count costs one walk of the area per draw.
- **Freshness.** After you place a building, `BuildManager.Disable` reopens the window. That is a new draw, so counts are rebuilt. The cache is also keyed on the area's object count.
- **`LazySingleton.Instance`** creates a `BuildManager` if none exists. One always exists while the build window is open, because `BuildManager` opened it.
- **Errors.** Any exception, including a missing field after a game update, is caught and logged once, and the row shows its normal name. The MVID guard disables the plugin entirely after a game update anyway.
- **Isolation.** Nothing is written to game state or saves. Global Storage patches none of these methods. The only change is the text shown on a UI label.

## Game update 25601286 (move to the Steam install): re-audit (v1.0.4)

The full comparison is in `docs/steam-move-audit.md` (and `..\GK2GlobalStorage\FINDINGS.md`, "Game update 25601286").

**Relevant here:** nothing. `UIBuildingWidget`, `UIBuildingWindow`, `UIBuildingWidgetData`, `WorldZoneData` and
`WgoData.isTempObject` are unchanged. `LazyBearTechnology` changed only in `ExceptionReportWindow`, not in
`LazyWidget<T>`.

**Verification:** Cecil scan (same member shapes), HarmonyX harness ALL PASS (audited MVID matches, one postfix on
`UIBuildingWidget.Redraw`). **Code change:** the MVIDs (now also `LazyBearTechnology`'s) and the version string.

## Game update 25533739: re-audit (v1.0.3)

The full comparison is in `..\GK2GlobalStorage\FINDINGS.md`, section "Game update 25533739".

**Relevant here:**
- `UIBuildingWindow` only adds `uiInfoWidget.Hide()` when it closes.
- `WorldZoneData` gains `RemoveItemsFromTownPalettes`. `wgoDataList` is unchanged.
- `Wgo` switches zombie colliders while zombies move.
- Every other file this plugin depends on is unchanged.

**Verification:**
- Cecil against the new DLL: the same members, types and access as before.
- HarmonyX harness: the MVID matches, and exactly one postfix applies, on `UIBuildingWidget.Redraw()`.
- Rebuilt with 0 errors. The one MSB3277 warning is about the game's newer `System.IO.Compression`, which this plugin doesn't use.
- Installed byte-identical to the build.

## Game update 25509347: re-audit (v1.0.2)

The update from Steam build 25457344 to 25509347 changed `Assembly-CSharp` (39 files plus 1 new) and `LazyBearTechnology` (3 files). The full comparison is in `..\GK2GlobalStorage\FINDINGS.md`, section "Game update 25509347".

**Unchanged:** every file this plugin depends on is identical in the decompiled source:
- `UIBuildingWidget`, `UIBuildingWidgetData`, `UIBuildingWindow`, `UIBuildingWindowData`
- `BuildManager`, `BuildingDef`, `BuildData`
- `BuildPointer`, `WgoBuildPointer`, `UpgradeBuildPointer`
- `WorldZone`, `WorldZoneData`, `WorldData`, `WgoData`, `WgoDataCache`
- `WgoExtensions`, `WgoDataExtensions`
- `GameBalance`, `WGODef`, `LazyExpression`
- In `LazyBearTechnology`: `LazyWidget`, `LazySingleton`, `LazyExpressionBase`, `GameBalanceBase`

**Changed, but irrelevant to this plugin:**
- `BuildController`: new visual hints for conveyor connectors.
- `RemovePointer`: removal is now allowed in the `resurrection` zone.
- `TextStyle.ApplyStyleToString`: a quoting fix. This plugin's labels go through `ApplyStyle`, which sets font, color and material, not text.
- `MainGame` and `GameSave`: preorder reward and camera updates.

**Verification:**
- Cecil against the new DLL: `Redraw` is virtual with a body, `nameLabel` is a `TextMeshProUGUI`, and `data` is a protected field on `LazyWidget<T>`.
- Rebuilt with 0 warnings.
- HarmonyX harness (.NET Framework 4.8): the MVID matches, and exactly one postfix applies, on `UIBuildingWidget.Redraw()`.
- Installed byte-identical to the build.

**Code change:** only the audited MVID and the version string.

## Limitations

- **Upgrades:** an upgraded object counts under its new row. For example, after an upgrade, Workbench I shows nothing and Workbench II shows (1).
- **Area only:** the count covers only the area of the build desk you opened, not the whole world.
- **Limited buildings:** these keep the game's own `(x/max)` label, unchanged.
- **Inactive areas:** an area that is not a container or not active has no object list, so no counts are shown.
- **Long names:** very long names plus the count might get cut off. Check in-game.

## Verification done

- **Build:** Release, netstandard2.1, 0 warnings, 0 errors.
- **Mono.Cecil check** against the installed game:
  - The MVID matches.
  - `UIBuildingWidget.Redraw` is virtual, not abstract, has no parameters, and has a body.
  - `nameLabel` is `TMPro.TextMeshProUGUI`.
  - `data` is a protected field `T` on `LazyBearTechnology.LazyWidget<T>`.
  - `UIBuildingWidgetData.BuildData` and `WorldZoneData` are public.
  - `WorldZoneData.wgoDataList`, `WgoData.isTempObject` and `GameSave.worldData` are public.
- **HarmonyX test** on .NET Framework 4.8 with the game assemblies:
  - Both field accessors resolve.
  - `PatchAll` applies exactly one postfix, to `UIBuildingWidget.Redraw`, and patches nothing else.
- **Overlap:** GK2 Global Storage does not patch `UIBuildingWidget`.
- **Deploy:** v1.0.1 is installed at `BepInEx\plugins\GK2BuiltCount.dll`, byte-identical to the build output (build 0 warnings; HarmonyX test passes).

## Still to test in-game

1. `LogOutput.log` shows `GK2 Built Count 1.0.4 loaded.` and no errors after opening a build menu.
2. The Yard menu shows counts on rows you have built. Unbuilt rows are unchanged.
3. Build one: reopen the menu and the count goes up by 1. Remove one: it goes down.
4. Switching tabs keeps the counts right. The "Remove" row and limited buildings look as before.
5. A build desk in another area counts only that area.
6. With `Enabled = false`, the menus look like vanilla.

## Build, install, uninstall

- **Build and deploy** (game closed): `dotnet build -c Release` in this folder.
- **Build only** (game running): `dotnet build -c Release -p:Deploy=false`.
- **Uninstall:** delete `BepInEx\plugins\GK2BuiltCount.dll` in the game folder, and optionally `BepInEx\config\dreas.gk2.builtcount.cfg`.
