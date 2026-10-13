# Game update 1.010 (Steam build 25910882): re-audit of the QOL mods

On 2026-10-13 the game went from 25752683 (the 1.009 hotfix) to 25910882, the version the changelog calls 1.010.
Audited before the game was started with it, together with the GK2 Donkey API repo (its
`docs/game-update-25910882.md` has the member diff, the decompile notes and the game-wide changes; this file is the
QOL side).

| | Before | After |
|---|---|---|
| `Assembly-CSharp` MVID | `44edf1a9-b9b5-4276-a731-a680e9e9c44d` | `5ec8094e-4c33-4615-9eb8-011501193186` |
| `LazyBearTechnology` MVID | `02955186-b2b7-474c-a6ff-b860ac92b69c` | `6cd86a0b-b1ed-40ba-88b0-a9313146514b` |

`Assembly-CSharp`: 338 members removed (17 not compiler-generated), 515 added, 372 changed; `LazyBearTechnology`:
a new MVID, no member changes (recompiled only). 75 decompiled types differ; new: `CrowNest`,
`CrowNestInteractionHandler`, `TakeOnlySerializedItemProperty`, `DisableOnPlatforms`, `Flow_SetOverheadVisibility`.

## Patch targets and references

- All five mods compile against the new DLLs. `CecilScan-{BuiltCount,GlobalStorage,MoveBuilding,OwnedCount,
  TechSearch}`, `StaticScan-GlobalStorage`, `FrameworkScan`: pass (0 unresolved references everywhere).
  `HarmonyHarness-BuiltCount` failed only on the old pinned MVID; green once the pin moved.
- **No patch target of the five mods changed** (member diff): `WgoData.GetCraftableMultiInventory`,
  `MultiInventory` ctor and `Sort`, `PlayerOrderExecutor` ctor, `ZombieEquipmentInventoryWidgetData.UnequipToPlayer`
  (Global Storage); `WgoBuildPointer.SetTarget`, `UIBuildingWidget.Redraw`, `BuildSelectionCell.OverlapBoxNonAlloc`,
  `BuildPointer.TryBuildActionInput`, `BuildManager.EnableBuildMode` (Built Count); `RemovePointer.TryDoBuildAction`
  / `OnPointerDisable` / `UpdateSelectionRect`, the `Wgo` chunk and workbench registration methods (Move Building:
  `Wgo.TryRegisterWorkbenchExtensionDelayed` is listed as changed but decompiles to identical text);
  `UITooltip.AddItemWidgets` (Owned Count; `UITooltip.ShowAtPosition` changed, PS4 branch only);
  `TechTreePageWidget.Init` / `Redraw` / `Hide` (Tech Search).

## Breaks found

None. Pins moved in every `Plugin.cs`, the READMEs and `dist/README.md`; dist rebuilt.

## Changes near the mods

- **Global Storage and the crow nest.** 1.010 adds a crow nest (`crow_nest`, `WGODef.InteractionType.CrowNest`,
  spawned on a new game and into every older save at the GD point `crow_nest_point`): a take-only, auto-expanding
  chest (`TakeOnlySerializedItemProperty`, `AutoExpandSerializedItemProperty`) into which the game moves small items
  that have lain on the ground for two days or more (`CrowNest.CollectAgedDrops`, once per game day from
  `DropSystem.CustomUpdate`; not resource drops, not corpses, nothing with an auto-destroy timer or the
  never-auto-destroy property). The game's take-only rule lives in the UI (`Inventory.BlocksPlayerItemInsert`:
  the chest window's move-all, the item move handler) and in `Inventory.TakeAllItemsExistingInMeFromOtherInventory`;
  `MultiInventory.TryAddItem` still fills any listed inventory that has room. Global Storage gathers every
  `OpenInMultiInventory` container of every zone into the crafting multi-inventories, and whether the nest carries
  that flag is balance data we cannot read. **Precaution built 2026-10-13:** `GlobalStorage.IsEligible` refuses an
  inventory whose data has `TakeOnlySerializedItemProperty`, so the nest is never appended (nothing can be crafted
  into it from afar; its contents are taken at the nest, as the game intends). The game's own zone multi-inventory
  is untouched by us.
- **`UIBaseChestWindowData`** (the chest window) has a take-only mode; **`InventoryUIItemMoveOpHandler.TryMoveItem`**
  refuses a move into a take-only inventory. Global Storage does not patch either.
- **Built Count / Move Building:** `BuildGridData` keeps its selection and buff grids instead of reallocating
  (`EnsureSelectionGridData` / `EnsureBuffUsageGridData`); `BuildPointer` and `BuildController` make their preview
  `WgoData` with the new `isTempObject` constructor parameter, and temp objects no longer get nav-mesh cuts
  (`WorldZoneData.TryAddWgoData`, `WgoData`). Move Building's moved building is a real object, never temp; the
  preview is the game's own. `Wgo.RefreshVisuals` got a re-entrancy guard and an "animation chunk flag" that keeps an
  animating view active (`ForceActiveForAnimationChunkFlag`); Move Building re-registers the moved view through the
  unchanged `TryRegisterInChunkManager` / `TryUnregisterInChunkManager`.
- **Owned Count:** `UITooltip.ShowAtPosition` skips `Canvas.ForceUpdateCanvases` on PS4; `UIItemCell.DrawEmpty` no
  longer recolours the icon. The widget list we append to (`AddItemWidgets`) is unchanged.
- **Tech Search:** `TechTreePageWidget` unchanged; `KnowledgeSystem` unchanged this time.
- **Save fixes at load** (saves < 1010: empty overhead items removed, vendor order slots synced to town quality,
  broken-decor chunks restored, crow nest spawned; saves ≤ 1010: body colour catch-up, orphaned carousel zombies
  dropped as items, stacked mushrooms removed) touch no building positions, chest inventories or tech state.
- **Quitting:** addressable releases are skipped while `GameShutdown.IsQuitting` (the hang-on-exit fix); none of the
  five mods holds a handle.

## In-game checks (pending)

1. Start: no `Game code changed since this mod was audited` error; all five load with their versions.
2. A build desk and a chest: storage still listed (and the crow nest, once it has items, is not).
3. A tooltip's owned count, the Tech Tree search box, a building move: as before; no warning or error from the mods.
