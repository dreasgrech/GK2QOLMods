# Game update 1.011 (Steam build 26297960): re-audit of the QOL mods

On 2026-10-30 the game went from 25910882 (1.010) to 26297960, the version the changelog calls 1.011. Audited
before the game was started with it, together with the GK2 Donkey API repo (its `docs/game-update-26297960.md` has
the member diff, the decompile notes, the game's new BepInEx shipping and Workshop sync, and the game-wide changes;
this file is the QOL side).

| | Before | After |
|---|---|---|
| `Assembly-CSharp` MVID | `5ec8094e-4c33-4615-9eb8-011501193186` | `36326881-56e1-4dee-a10f-2893840f0687` |
| `LazyBearTechnology` MVID | `6cd86a0b-b1ed-40ba-88b0-a9313146514b` | `46a6f4cd-56c5-410d-993a-de26a488dc60` |

`Assembly-CSharp`: 783 members removed (86 not compiler-generated), 2,303 added, 692 changed; `LazyBearTechnology`:
19 added, 7 changed. 213 decompiled types differ, 49 are new; the ones that matter here are the game's own
**building move** (`BuildMoveHelper`, `BuildMoveData`, `MoveSelectPointer`, `MovePlacePointer`,
`BuildObjectSelectionPointer`) and the **code-mod loader** (`CodeModHost`, `WorkshopSyncCore`, `UICodeModsWindow`).

## Patch targets and references

- **Four of the five mods compile** against the new DLLs; **Move Building does not** (below). `DepManifest verify`
  on the deployed DLLs: Built Count (35 entries), Owned Count (61) and Tech Tree Search (49) **identical**; Global
  Storage (43) one member **changed**; Move Building (134) five members **missing** and seven changed.
  `CecilScan-{BuiltCount,GlobalStorage,OwnedCount,TechTreeSearch}`, `StaticScan-GlobalStorage`, `FrameworkScan`,
  `NoLinqScan`, `HarmonyHarness-BuiltCount` (patches applied against the new game): pass. `CecilScan-MoveBuilding`,
  its manifest verify and `check-dist` (which verifies the dist DLLs) fail on Move Building alone.
- **Global Storage, `WgoData.GetCraftableMultiInventory`** (our postfix appends the other zones' storage): the
  body gained a branch for a workbench a zombie assistant operates through the new `AutoCrafterOrderPlacer`
  while no zombie is its worker (`flag`): the craft inventory is added first and the zone's inventories after, or,
  without a zone, the object's own inventory. Our postfix runs after the whole body, only when the worker is the
  player and the extension is on, and appends at the end: the order and the player-only rule are as before. The
  zombie assistants' own autocrafts have a zombie worker or run through orders, so they never get the extension
  (the right outcome: a remote chest should not feed a zombie's furnace). Lifted buildings (the game's move) are
  `IsInteractable = false` and removed from their zone's multi-inventory list while lifted; the mod's chest
  collection skips non-interactable chests already, so a chest in mid-move is not offered.
- Owned Count, Built Count and Tech Tree Search use nothing that changed; their patch targets
  (`UITooltip.AddItemWidgets`; `BuildManager.EnableBuildMode`, `UIBuildingWidget.Redraw`,
  `BuildSelectionCell.OverlapBoxNonAlloc`, `BuildPointer.TryBuildActionInput` is Move Building's; `TechTreePageWidget`)
  are byte-identical in IL.

## Breaks found

**Move Building is broken by 1.011, and the game now has the feature itself.**

- Missing: the field `RemovePointer.currentRemovingSelection` and the methods `RemovePointer.TryDoBuildAction`,
  `OnPointerDisable`, `UpdateSelectionRect` (the mod's three patches on the remove pointer and its selection
  accessor), and `WgoBuildPointer.SetTarget(Wgo, GameScene, BuildingDef, List<NeedItemData>, MultiInventory)` (the
  overload gained a sixth parameter, `ignoreBuil...`, so the patched signature is gone). `RemovePointer` was
  rewritten on a new base, `BuildObjectSelectionPointer` (hover, selection groups, tint, a gamepad cursor;
  `TryDoBuildAction`, `OnPointerDisable` and `UpdateSelectionRect` live there now, two of them private), with
  `CanSelect` / `ConfirmSelection` / `RefreshBlocked` as the pointer's own.
- Changed (bodies): `BuildPointer.TryBuildActionInput` (a move placement fires no build event),
  `BuildController.DisableBuildMode`, `BuildGrid3D.Draw`, the `UIBuildingWidgetData` and `UIBuildingWindowData`
  constructors (the Move row is added to every tab, not on fight builders), `WgoData.RemoveWorkbenchExtension`,
  `WgoData.set_WorldZoneData`.
- **What the game's move does** (`BuildingDef.BuildingMode.Move`): a Move row in every build desk tab
  (`BuildData.GetDataForMove`); `MoveSelectPointer` picks a building whose `Wgo.IsBuildMovable` (not temp, not
  despawning, not lifted, no destroying craft, custom tag not `alchemy_chest`, no `lock_building_move` game res,
  the balance's `moveMod` not `DoNotMove`; `ForceMove` overrides the removable rule); `BuildMoveHelper.TryLift`
  lifts the building with its full-cover extensions, suspends its orders and its place in the zone's multi-inventory,
  pauses the zombies heading to it, detaches conveyors and hides the view; the game's own build preview
  (`MovePlacePointer`, a conveyor variant) places it; `TryPlace` relocates **the same `WgoData`**
  (`WorldData.RelocateWgoData`: contents, crafts, id, references kept), relinks extensions and conveyors, resumes
  the zombies. That is the mod's design (`INVESTIGATION.md`, `SAVE-SAFETY.md`) done by the game.
- **What the mod did that the game's move does not:** the Left Ctrl modifier in the game's Remove mode, staying
  in move mode after a move (`StayInMoveMode`), the configurable tint of movable buildings (`HighlightMovable`,
  `MovableColor`), the Mods-menu rows. The game blocks by its balance lists; the mod blocked by its own rules
  (`Mover.cs`).
- **On 1.011 the deployed 1.1.6 stops itself at start** (five missing members: `Safety` stops the mod, status
  "Off" with the members named) and the game runs unmodified, with its own Move row. No player is harmed; the
  Nexus page should say so.
- **Decision (the user's):** retire the mod (a Nexus note that 1.011 has the feature built in, the download kept
  for older builds) or rebuild it on `BuildObjectSelectionPointer` for the extras above. Not changed in this audit;
  the project stays in the repo as it was, failing its scans.

## Changes near the mods

- **BepInEx and the Workshop** (the API repo's report has the detail): the game now ships BepInEx 5.4.23.5 (the
  files this machine already had), a preloader patcher `GK2.WorkshopSync` that copies subscribed Workshop items'
  `BepInEx\` folders into the game and removes only what it copied, and `README_MODDING.txt`, the code-mod contract.
  Our zip layout (`BepInEx\plugins\<Mod>\`, settings in `BepInEx\config`) is the contract's section 2; a Workshop
  item is the zip's content plus a `modinfo.json`. The Nexus "install BepInEx first" step is redundant for Steam
  players now. The user's calls.
- **Crafting UI** shows the whole queue's output (`CraftQueueOutputIndicator`, `UICraftHintWidget`); Owned Count's
  tooltip patch (`UITooltip.AddItemWidgets`) is unchanged and the item widgets it counts are the same.
- **Controller:** LB/RB move ten items in the count window (`GameKey.ItemCountWindow_*`). Not on our paths.
- **Ultrawide:** the main camera can render to an upscaled target; `UITooltip.ShowAtPosition` and
  `UIMouseTooltipCollider` changed with it (Owned Count only adds widgets to the tooltip, it never places it).
- **The build grid** (`BuildGrid3D.Draw`, `BuildCellData.GetData` with a `BuildingDef`, `BuildGridData.FormGridData`):
  changed for the move placement; Built Count's `BuildSelectionCell.OverlapBoxNonAlloc` and
  `UIBuildingWidget.Redraw` patches are unchanged.

## In-game checks (pending)

1. Start: Built Count, Owned Count, Tech Tree Search "Active"; Global Storage "Active" after the rebuild (its
   manifest matches again); Move Building "Off" with the missing-member status and no error of its own.
2. Global Storage: a craft at a workbench draws from another zone's chest as before; a zombie assistant's autocraft
   at a furnace does not; a chest lifted by the game's Move is not offered while lifted.
3. Built Count: the build desk counts with the new Move row present in every tab.
4. Owned Count: the tooltip with the queue-output indicator shown.
5. Tech Tree Search: the search box on the tech page.
6. The Mods menu (GK2 Mod Framework, not ours) still shows the pages.
