# The Nexus "GK2 Move Buildings" mod: what it does, what goes wrong, and what we do instead

Studied 2026-09-27 against game build 25533739 (Assembly-CSharp MVID `97f92f0d-…`).
Source: the downloaded Nexus package `GK2MoveBuildingNexusMods` (DLL 0.5.3, `gk2.movebuilding`, 161 KB), decompiled with
ILSpy to the scratchpad `movebld\src` (about 6,500 lines in 18 files). All line references below are to that decompile
and to the game decompile `decomp_33`.

## 1. What is in the DLL

| File | Lines | Role |
|---|---|---|
| Plugin.cs | 518 | Config, 15 patches on the core, per-frame `Update` driving every subsystem |
| Move.cs | 1837 | Pick up, place, "build new then demolish old", carry contents, relink extensions, grave move, body rescue |
| Upgrade.cs / UpgradeFlow.cs / UpgradeIcon.cs | 1538 | "Upgrade in place": hides the old building under a construction site, swaps ids when it finishes; an injected UI button and gamepad key |
| Why.cs | 558 | Hints for why the game refuses a removal; unlocks protected graveyard layouts; removes layouts with their empty grave places |
| Porters.cs | 246 | Re-points zombie targets and zone orders to the rebuilt copy; a 3-second scan for porters stuck on gone buildings |
| Pad.cs | 530 | Gamepad chord handling |
| Rusty.cs | 118 | Transpiler letting rusty starting tools be destroyed |
| LinkedStorageCompat.cs / RefundCompat.cs / Framework.cs / FwText.cs | 421 | Pokes into three other mods' private members |
| ExtLoc.cs / Ui.cs / Cm.cs / GraveDiag.cs | 759 | Translations, Move row/icon, ConfigurationManager attributes, placement diagnostics |

Only Move.cs, Ui.cs, Porters.cs and roughly half of Plugin.cs are about moving buildings.

## 2. How its move works

1. **Pick up.** Prefix on `Wgo.DoBuildRemove` (Plugin.cs:496): if the Move row was picked or Ctrl is held, remember the
   building and return `false` so nothing is demolished. `RemovePointer.TryDoBuildAction` would still evaluate the
   removal definition's `expressionAfterBuilding` (RemovePointer.cs:62-67), so a prefix nulls the pointer's private
   `currentSelectedBuildingDef` and a finalizer restores it (Plugin.cs:373-397, 449-467). The expressions are stashed
   as `RemoveEffects` and run later, after the move (Move.cs:1798).
2. **Place.** Next frame: disable the remove pointer by hand, clear the build grid, `BuildManager.EnableBuildMode`
   with the building's definition and an empty item list (Move.cs:131-157). The cost is waived by overwriting the
   pointer's private delegates `canTakeResources` / `takeResourcesAction` through `Traverse` (Move.cs:229-247).
   A postfix on `BuildSelectionCell.OverlapBoxNonAlloc` drops the old building's colliders (Move.cs:249).
3. **Finish.** The game's `WgoBuildPointer.TryDoBuildAction` runs unchanged: it creates a **new** `WgoData`, spawns it,
   evaluates the build definition's `expressionAfterBuilding` (WgoBuildPointer.cs:141-175). A postfix on it then
   demolishes the old one: detach workbench links, release the zombie, drop inventory and craft contents (or carry
   the inventory when `CarryContents` is on), run the stashed demolition expressions, `RemoveWgoDataFromGameScene`,
   then relink extensions around the new copy and re-point porters (Move.cs:663-743).
   - Two exceptions keep the same object: stations with queued crafts (Move.cs:688) and graves (Move.cs:502), which
     use the game's private save-fix helpers `SaveFixContext.RemoveFromWorldZonesLive` / `AssignWorldZoneLive` plus
     `WorldData.ChangeWgoData` (Move.cs:356-453).

## 3. Problems, with evidence

### Design

1. **The moved building is a different object.** `new WgoData(...)` gets a fresh `UniqueId`, empty `GameResStr`
   (per-object flags such as `lock_building_removal`), no `CustomTag` (quests find objects by tag: 32 call sites in
   the game), no delayed events, a fresh script instance. `WgoData.OnRemove` (WgoData.cs:858-916) on the old one
   drops it from NPC groups and points of interest, clears its delayed events, garden orders and script. Everything
   that pointed at the old id must be fixed by hand, which is what Porters.cs, `Detach`/`Relink` and
   `PruneDeadLinks` try to do, each by reflection over private fields (Porters.cs:38-47).
2. **Build and demolition scripts both run on every move.** The new copy runs the build definition's
   `expressionAfterBuilding` (the game does it, WgoBuildPointer.cs:155-158); the mod then runs the removal
   definition's expressions itself (Move.cs:1798-1820). Those are quest, unlock, stat and counter effects.
3. **The game's `BuildBuilding` trigger fires** for the new copy (BuildPointer.cs:119-123), so anything counting
   "built X" counts a move.
4. **Contents drop on the ground by default** (`CarryContents` off, Move.cs:711); craft inputs and outputs always
   drop (Move.cs:1288-1328). Finished-craft outputs are reconstructed from recipe data (Move.cs:1493-1544), which is
   a guess at what the game would have produced.
5. **The same-object path (`UpgradeInto`) is not clean either.** `WorldData.ChangeWgoData` (WorldData.cs:194) removes
   and re-adds the object, so `OnRemove` runs (town quality subtracted, then re-added by `SetDataFromDefinition`),
   and `ChangeId` (WgoData.cs:786-826) rebuilds the inventory (start items are added again), resets HP to full,
   recreates the main part data and resets crafts from the balance cache. The mod calls it even when the id doesn't
   change (Move.cs:442 with `refund: false`).
6. **Construction-site buildings skip a step.** A finished `x` whose entry is `x_place` is rebuilt as a site, which is
   renamed to `x` at once (Move.cs:760-763). The site's placement scripts run again; the finishing craft's effects
   (`replaceWgoId`, `executeOnReplace`) never run.
7. **The body-rescue routine is evidence of harm.** `TickRescue` (Move.cs:562-661) scans every garden bed on every
   save load and drops corpses that "0.4.9" left inside them.

### Robustness

8. **No game-update guard.** Every patch is applied by name and skipped with a warning when missing (Plugin.cs:57-215).
   After a game update the mod can run with some hooks missing, for example moving without the collider filter, or
   the porter fix silently off. `AssemblyVersion` is `0.0.0.0`.
9. **Private state is written by string name through `Traverse`**: `canTakeResources`, `takeResourcesAction`,
   `currentSelectedBuildingDef`, `currentRemovingSelection`, `markedForRemovingObjectRects`, `buildPointer`,
   `craftElementsQueue`, `takenDockPointsParentSGuid`, item `id` fields, and more. A rename makes them no-ops with no
   startup check.
10. **Symptom patches instead of causes.**
    - `HintsFinalizer` swallows `NullReferenceException` from `BuildController.UpdateFullCoverSoftBuildAreaHints`
      for three seconds after a move (Plugin.cs:334-341). The cause is the mod tearing down build mode from inside
      the build-action postfix while the controller's delayed coroutine still runs (`LockBuildInputAndDoDelayedAction`,
      BuildController.cs:417-426).
    - `SelectionRectPre` removes destroyed Unity objects from the remove pointer's static dictionaries on every
      `UpdateSelectionRect` (Plugin.cs:399-431), because the mod despawns a building the pointer still references.
11. **Broad `catch {}`** in `Update`, `TryPickUp`, `IsConveyorElement`, `Controller` and dozens of helpers: failures
    become silent no-ops mid-move.

### Side effects outside moving

12. **Background work every frame, whether or not anything is moved** (Plugin.cs:218-249): porter scan every 3 s over
    all zombies via reflection (Porters.cs:163-243), upgrade hide/show scan every second with
    `FindObjectsByType<Wgo>` re-seeds (Upgrade.cs:270-282, 453-517), an active-window reflection probe each frame
    plus `GetComponentsInChildren<RectTransform>` twice a second while any window is open (UpgradeIcon.cs:62-112,
    281-315), and a global link check on every build-mode close (Move.cs:975-1020).
13. **It changes vanilla state on its own.** The stuck-porter check stops orders and sends porters home whenever a
    caretaker target is missing, including for buildings the player demolished normally (Porters.cs:196-230).
    `TickBuildClosed` prunes workbench links across every scene (Move.cs:993-1015). The body rescue drops items.
14. **Global hooks for the upgrade feature**: `WorldData.ChangeWgoData` postfix on every id change in the game (grave
    exhume, fighter containers, scripted replacements) feeds a "replace older tier under it" routine (Upgrade.cs:296,
    588-600); `WgoData.IsInteractable` and `PlayerInteractionComponent.GetWgoTargetsFromColliders` are patched to hide
    the building it made inactive (Upgrade.cs:399-413). Tiers are guessed from id suffixes with a regex
    (`_(\d+)` / `_rough`, Upgrade.cs:17-18), with `chest_good` special-cased.
15. **Feature creep in one DLL**: upgrade in place, rusty-tool destruction (an IL transpiler, Rusty.cs:39-57),
    graveyard-layout unlocking (`IsBuildRemovable` postfix, Plugin.cs:435-441), removal hints, three cross-mod
    shims that read other mods' private statics (LinkedStorageCompat.cs:29-36).

## 4. What we do instead (GK2 Move Building 1.0.0)

| Topic | Nexus mod | Ours |
|---|---|---|
| Identity | New object; old one demolished | Same `WgoData`, same view. Position, rotation and zone membership change; nothing else |
| Scripts / triggers | Build expressions, demolition expressions and `BuildBuilding` trigger all run | None. The remove click and the build click are both intercepted before the game acts |
| Contents, crafts, worker links | Dropped / carried / re-pointed by hand | Untouched |
| Data-layer method | `RemoveWgoDataFromGameScene` + `AddWgoData` (runs `OnRemove`) or `ChangeWgoData` | `WorldZoneData.RemoveWgoData` / `TryAddWgoData` in the game's priority order: the same steps as the game's own `SaveFixContext.MoveWgo`, through public methods |
| Failure | Half-done states possible (old removed, new missing → warnings) | Transactional: landing anywhere but the original zone is rolled back before any view step |
| Update safety | None; patches skipped by name | MVID guard; all private members resolved at startup; `PatchAll` all-or-nothing |
| Private access | `Traverse` strings at call time | Typed `FieldRefAccess` / `MethodDelegate`, verified once |
| Build-mode teardown | From inside the build postfix; NRE swallowed by a finalizer | Next frame, after the controller's input lock clears |
| Per-frame cost | Always | Only while a move is in progress or the Move row is armed |
| Vanilla state | Background fixes across the save | Never touched outside the one moved object |
| Scope | Eight features | One feature. Refuses what it can't move cleanly, with a message |
| Patches | ~20 across three Harmony ids, two transpilers | 7 prefix/postfix |
| Lines | ~6,500 | ~600 |

### What we deliberately don't do

- **Move buildings with a zombie, an occupied dock point, garden beds or NPC use.** The Nexus mod handles the zombie
  by releasing it (Move.cs:1122-1185); every one of those is a place it can go wrong. We refuse with a message and
  let the player clear the building first.
- **Workbench links** are handled since 1.0.1, like the reference's `Detach`/`Attach` (Move.cs:1759, 869) but by
  running the game's own spawn-time routine instead of a copy of it, and with the drawer subscriptions to the old
  parents removed (the reference leaves them; ConditionalDrawer.cs:78-92 only adds).
- **Baked navigation points** (`gdPointsData`) are re-registered since 1.0.1. The reference never touches them; its
  new copies get fresh points at spawn (Wgo.cs:313-321) and its same-object paths respawn the view.
- **Graves, layouts, conveyors, fighting/base buildings**: own data models. Refused.
- **Cross-zone moves**: zone counters, orders and multi-inventory lists are per zone. Refused (rolled back).
- **Upgrade in place, rusty tools, layout unlocking, hints**: not part of moving.

## 5. Game facts the design relies on (build 25533739)

- `WgoDataCache` indexes by uid, id, tag and group, never by position (WgoDataCache.cs:7-13): a position change needs
  no re-caching.
- `WorldZoneData.GetWgoDataByRect` reads `Position` at query time (WorldZoneData.cs:335-352).
- `WorldZoneData.RemoveWgoData` removes the zone nav cut and stops cut tracking; `TryAddWgoData` re-adds the cut
  for every hole type except `DontSpawnHole` and restarts tracking (WorldZoneData.cs:290-394, 354-372).
  `WgoData.RefreshCustomNavMeshCutUnit` covers the rest (WgoData.cs:623-635).
- `WgoData.Position` setter fires `OnPositionChanged`; `Wgo.HandleChangedPos` moves the transform (Wgo.cs:1596).
- `Wgo.GetChunkableData` offsets its cached bounds by the transform delta (Wgo.cs:2372-2400); only chunk membership
  is fixed at registration (ChunkManager.cs:116-153), hence unregister/register.
- `Wgo.UpdateWgoPartState` is public and applies `rotationIndex` to the view (Wgo.cs:531-545).
- Dock points are baked relative to the part (WgoPartData.cs:489-515); garden GD points are stored in world space
  (Wgo.cs:2601-2640, GDPointData.cs), hence garden beds are refused.
- The preview may be a different object (`customWgoPlacePreview`, BuildPointer.cs:202); rotation is only copied when
  the preview id equals the building id.
- The game's own Remove row is a `BuildData` with mode Remove and no definition (UIBuildingWindowData.cs:45-48); the
  window's press handler sends any non-Script row to `EnableBuildMode` (BuildManager.cs:132-136).
