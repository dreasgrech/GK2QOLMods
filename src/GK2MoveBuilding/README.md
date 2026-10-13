# GK2 Move Building

BepInEx plugin for Graveyard Keeper 2: pick up a built building and put it down elsewhere in the same area.
The building is the **same object** afterwards. Its contents, crafts, fuel, upgrades, scripts, id and every
reference to it (orders, porters, quests) stay as they were. Nothing is paid, nothing is refunded, no build or
demolition scripts run.

Version 1.1.6, built and audited against Steam game build 25910882, game 1.010 (Assembly-CSharp MVID `5ec8094e-…`,
LazyBearTechnology `6cd86a0b-…`; re-audit in `docs/game-update-25910882.md`). 1.1.5 was the same code for 25601286, 1.1.4 was the same move code without the
Mods-menu integration, and 1.1.3 the same code for 25533739.
See `INVESTIGATION.md` for the study of the Nexus mod this was designed against, and why this one works differently.

## How to use

1. Open a build desk. Every tab has a **Move** row after **Remove**. Pick it.
2. The game's remove mode starts, but a click now **picks the building up** instead of demolishing it.
   If it can't be moved, the keeper says why.
3. The game's own build preview appears for that building. Put it where you like (rotate as usual). The old spot
   counts as free, so it can overlap where it stood.
4. Click to place. The building is relocated and you are back in move mode for the next one (Back opens the desk menu).
   In move mode, movable buildings are tinted light blue and have no red patch; unmovable ones keep the red.

Alternative: in the game's own **Remove** mode, hold **Left Ctrl** (setting `MoveModifier`) and click.

Back / right click during the preview cancels the move. Nothing was changed at that point.

## Optional: GK2 Mod Framework

With [GK2 Mod Framework](https://www.nexusmods.com/graveyardkeeper2/mods/42) (0.1.x, by SuperMan4eg) installed,
the mod appears in the game's **Mods** menu (main menu and ESC menu). It shows:
- a Status row (Active, Off, or disabled after a game update);
- Enabled, the Move row and its text, the modifier key, stay-in-move-mode and the tint switch.

Each applies the next time it's used: the desk menu, the next click, the next frame.

Two notes on the modifier key in the menu:
- **Press a single key.** The mod uses only the main key, so a combination like Shift+K acts as plain K.
- **The menu can't switch it off**, because Esc cancels the capture. For that, set `MoveModifier = None` in the cfg.

`MovableColor` (the framework has no colour control) and `Safety` stay cfg-only; `Debug.FaultInjection` is in the cfg only
with the developer marker file `BepInEx\config\dreas.gk2.dev` (2026-10-13). Without the
framework, the same DLL works exactly as before. Details: [`docs/gk2-mod-framework.md`](../../docs/gk2-mod-framework.md).

## Settings (`BepInEx\config\dreas.gk2.movebuilding.cfg`)

Every value is read where it's used, so edits made in the Mods menu apply at once. Each move also re-reads the cfg
file, so direct edits to it apply at the next move.

| Key | Default | Meaning |
|---|---|---|
| `General.Enabled` | true | Master switch. |
| `General.ShowMoveRow` | true | Adds the Move row to the desk menu. |
| `General.MoveLabel` | Move | Text of that row. |
| `General.MoveModifier` | LeftControl | Hold in Remove mode to move instead. `None` disables. |
| `General.StayInMoveMode` | true | After a move, or after Back during the preview, return to move mode instead of the desk menu. Back in move mode still opens the menu. |
| `General.HighlightMovable` | true | In move mode, movable buildings lose the red grid patch and are tinted; the one under the cursor is tinted stronger. Unmovable ones keep the game's red. |
| `General.MovableColor` | light blue | The tint colour. |
| `Safety.StopOnChangedGameCode` | false | At start the mod compares every game member it uses with the installed game (`docs/dependency-check.md`). A member that is gone always stops the mod; one whose code changed runs it with a warning, or stops it too with true. |
| `Safety.LastDependencyCheck` | | Written by the mod: the cached result of that comparison for the current game build. |

## What can't be moved (and the message)

| Refused | Message | Why |
|---|---|---|
| Conveyors | Conveyors can't be moved. | Graph links live in the conveyor system, not on the object. |
| Graves, graveyard layouts | Graves and graveyard layouts can't be moved. | Separate data model (slots inside layouts). |
| Anything the game won't let you demolish | The game won't let this one be picked up. | Same rule as the game's remove mode (`Wgo.IsBuildRemovable`). |
| No build entry | There's no build entry for this one. | Nothing to show a preview with. |
| Fighting, base, conveyor, upgrade, script entries | This kind of building can't be moved. | Their own placement flows and bookkeeping. |
| Strict / full-cover area buildings | This one belongs to a special build area. | Grave places, workbench extensions: they must sit inside another building's area. |
| A zombie working in it | Take the zombie off it first. | The zombie's dock point and job would need re-pointing. |
| A dock point in use (a porter is at it) | Someone is using it right now. | Same. |
| Garden beds | Garden beds can't be moved. | Beds link approach points with their neighbours. Other baked navigation points (e.g. supplier stations) are re-registered after the move. |
| Buildings villagers use (`npcLifeSimGroup`) | Villagers use this one; it can't be moved. | NPC schedules reference it by place. |
| Self-moving objects (`isMovable`) | This one moves on its own. | Different chunk layer and movement adjust. |
| Outside the desk's area | It's outside this desk's area. | Only same-zone moves; the desk's area decides. |
| Landing spot outside / in another area | That spot is outside the area. / …belongs to another area. | The move is rolled back. |

## How it works

Patches (10, all attribute-based, applied all-or-nothing):

| Target | Kind | Purpose |
|---|---|---|
| `UIBuildingWindowData` ctor | postfix | Appends the Move row (a Remove-mode `BuildData`) to each tab. |
| `UIBuildingWidgetData` ctor | postfix | Names that row. |
| `BuildManager.EnableBuildMode` | prefix | Arms the move when the picked row is ours. |
| `RemovePointer.TryDoBuildAction` | prefix | While armed (or Ctrl held): pick the building up; the original never runs, so nothing is demolished and no demolition expressions are evaluated. |
| `WgoBuildPointer.SetTarget` | postfix | While placing: the cost check always passes (nothing is taken because the original build action never runs). Refuses pointer subclasses. |
| `BuildSelectionCell.OverlapBoxNonAlloc` | postfix | While placing: the picked building's own colliders don't block. |
| `BuildPointer.TryBuildActionInput` | prefix | While placing: relocate the existing object instead of building a new one; also skips the game's `BuildBuilding` trigger and build sound (we play the sound ourselves). |
| `RemovePointer.UpdateSelectionRect` | postfix | Move-mode tints on top of the game's hover tint. |
| `RemovePointer.OnPointerDisable` | postfix | Clears those tints. |
| `BuildGrid3D.Draw` | prefix | In move mode, cells occupied only by movable buildings are drawn as plain cells (the grid is display-only in remove mode). |

The relocation itself (`Mover.Relocate`) uses only public game methods and mirrors the game's own save-fix
`SaveFixContext.MoveWgo`:

1. `WorldZoneData.RemoveWgoData` — leaves the zone list; the zone removes its navmesh cut and stops cut tracking.
2. Set `WgoData.Position` (the view follows through the game's `OnPositionChanged`) and, if the preview is the same
   object type and was rotated, `rotationIndex`.
3. Re-add through the zones in the game's priority order (`TryAddWgoData`). If it doesn't land in the original zone
   (outside, or a higher-priority zone), everything is put back and the move is refused.
4. `Wgo.UpdateWgoPartState` when rotated, `RefreshCustomNavMeshCutUnit`, `Physics.SyncTransforms`.
5. Re-register the view in the chunk manager (chunk membership is fixed at registration; the bounds themselves
   follow the transform).

Nothing is removed from or added to the scene, so `WgoData.OnRemove` never runs: town quality, NPC groups, delayed
events, attached scripts and custom tags are untouched.

Two kinds of position-bound data are refreshed the way the game's first spawn creates them:

- **Workbench links** (tool racks on an anvil, etc.) come from collider overlap. Before the move every link is cut on
  both sides and the moved object's drawers stop listening to the old parents; after the move (or a rollback) the
  game's own `TryRegisterWorkbenchExtensionDelayed` runs for the object, as a workbench and/or as an extension. A
  workbench moved away leaves its extensions standing where they were, unlinked, which is the state they have when
  built in open ground; move them onto the workbench again to re-attach.
- **Baked navigation points** (`gdPointsData`, e.g. the zombie supplier station) are stored in world space; they are
  re-registered with `RegisterGDPointsFromBakedData` and re-bound to the view.

## Test rounds

**Round 1 (1.0.0, user):** only instantly built things moved (timber storage, a rough chest). Every station built
through a construction site was refused with the generic message. Cause: the game's `WorldData.ChangeWgoData` (site →
finished building) runs `OnRemove` on the same object, which sets `isRemovingFromData = true` and nothing clears it;
the pick-up check treated that as "being removed". The game itself only reads the flag in garden-bed navigation.
Fixed in 1.0.1 with a liveness check (`worldData.GetWgoData(uid)` is this object). The same round showed the
tool rack / anvil refusal (links) and the supplier station refusal (navigation points); both are now supported as
described above. Cross-check with the reference mod: it never reads `isRemovingFromData` (it tracks its own removed
object); it detaches links the same way and re-links with its own copy of the game routine, leaving drawer
subscriptions stale; it never handles navigation points because it respawns views.

Private members used (resolved at startup, any missing one disables the mod): `BuildManager.currentBuildDesk`,
`BuildManager.additionalInventories`, `BuildController.buildPointer`, `BuildController.isBuildModeInputLocked`,
`WgoBuildPointer.canTakeResources`, `BuildPointerObject.shownAsActive`, `RemovePointer.currentRemovingSelection`,
`Wgo.TryRegisterInChunkManager`, `Wgo.TryUnregisterInChunkManager`, `UIBuildingWidgetData.Name` setter,
`Wgo.RegisterGDPointsFromBakedData`, `Wgo.TryRegisterWorkbenchExtensionDelayed`, `ConditionalDrawer.OnParentCraftStatusChanged`.

## Safety

- Dependency check (`docs/dependency-check.md`): a game member the mod uses that is gone disables it at start; one whose code changed keeps it running with moving off ("Moving is off: the game changed code this mod uses") until a re-audited build.
- `Access.Verify()` and the patches (applied class by class from `Plugin.PatchClasses`) run in one try, and a
  failure unpatches everything.
- No per-frame work unless a move is in progress or the Move row was picked.
- The only save-data writes are position, rotation and zone membership of the one moved object, and only after
  the game's preview said the spot is valid. A refused landing is rolled back before anything else happens.
- Per-move errors cancel the move and are logged; the move is never half done because the data-layer steps are
  the rollback-able ones and the view steps come after.

## Limitations

- Same area only.
- Buildings that were built from a construction site (`x_place`) use the site's preview to choose the spot;
  its footprint is normally the same as the finished building's. Rotation is kept, not taken from the preview.
- A building that a porter is walking towards keeps being its target; the porter should re-path. Watch for this.
- Limited buildings show the game's "x/max" bubble on the preview while moving; it is only a display.

## In-game test list (game build 25601286)

Commit the saves repo first.

1. Log shows `GK2 Move Building 1.1.5 loaded.` **once**, no errors.
2. Desk menu: **Move** row after **Remove** on every tab; Built Count still shows its counts on other rows.
3. Move an empty sawhorse-type station: preview appears, old spot is free, place, window returns. Check it's at the
   new spot, the old spot is walkable and buildable, and the Built Count is unchanged.
4. Move a chest with contents: contents intact.
5. Move a station with a queued craft and fuel: craft continues, fuel intact.
6. Rotate the preview before placing: building rotated; walk around it (colliders, navmesh).
7. Place it overlapping its old position; place it right next to another building.
8. Try each refusal: a conveyor, a grave, a workbench with an extension, a station with a zombie, a garden bed. Each
   shows its message and nothing changes.
9. Back / right click during the preview: nothing changed.
10. Ctrl + click in the game's own Remove mode moves; plain click still demolishes.
11. Save, reload: the building is where it was put, still works.
12. Leave the area and come back (chunk culling): the building appears at the new spot, not the old one.
13. With GK2 Mod Framework installed:
    - Mods → GK2 Move Building shows Status Active and the six settings;
    - change the row text (press Enter) and it shows on the desk;
    - capture a new modifier key, and it moves in Remove mode;
    - turn the tint off and on;
    - Reset all.
14. With the framework removed: the mod loads as normal, a move works, and the log has no warning or error, and
    nothing mentioning the framework.

## Verification done before release

- `dotnet build`: 0 errors, 0 warnings from this code (one harmless MSB3277 from the game's newer System.IO.Compression).
- Cecil scan (`scratchpad\mbscan`): all 86 IL references into the game assemblies resolve; every by-name member above
  exists with the expected type; every patch target is a single non-abstract method.
- HarmonyX harness: not possible for this mod (like Global Storage) because `IWorker` uses default interface methods,
  which the .NET Framework harness can't load, and BepInEx 5's Harmony build doesn't initialise on .NET 8.
- Round 1 in-game test (1.0.0) done by the user; see "Test rounds" below.
- 1.1.5:
  - `tests/FrameworkScan` passes: all 10 patch classes are in `Plugin.PatchClasses`, and the framework is confined
    to `FrameworkLink`;
  - `tests/CecilScan-MoveBuilding` resolves 87/87;
  - in game on 2026-09-30, tests 13 and 14 passed.
    - Framework installed: registered Compatible. In the log: `MoveLabel=Relocate` saved and shown on the desk,
      `MoveModifier=LeftAlt` captured and then used for a move, and the tint toggled off and on. Moves of
      `furnace_1` and `mf_hammer_0` in the yard, with no warnings.
    - Framework removed: a clean log, and three moves (`furnace_1`, and `spinning_wheel` twice).
  - The first key-capture attempt saved nothing, and the log showed no change. The second worked. There's no sign
    of a fault in either mod: the capture needs the key button clicked, then one key pressed and released.
