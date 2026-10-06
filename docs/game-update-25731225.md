# Game update 1.009 (Steam builds 25731225 and 25752683): re-audit

**Second build the same day, 25752683:** two lines differ from 25731225 (`EnvironmentEngine.CustomUpdate` and
`MathUtilities.ClampCycleWithinRange`, the day-rollover fix finished). Nothing near these mods. Pins moved to
`Assembly-CSharp` `44edf1a9-b9b5-4276-a731-a680e9e9c44d`, `LazyBearTechnology` `02955186-b2b7-474c-a6ff-b860ac92b69c`;
rebuilt and rescanned, all pass.

On 2026-10-06 the game was updated on Steam from 25676698 (1.008) to 25731225 (1.009). The 1.008 DLLs came from the
game folder's git `HEAD`. Audited before the game was started with the new build. The full member diff and the
list of changed types are in the GK2DonkeyAPI repo's `docs/game-update-25731225.md`.

| | Before | After |
|---|---|---|
| `Assembly-CSharp` MVID | `f7d46132-dd54-4296-8f19-917330ceadd9` | `6d888e09-1564-414a-8e4f-04e3e6757694` |
| `LazyBearTechnology` MVID | `f67c6993-6db1-4f08-8352-cf7315263736` | `c51bcee3-42e5-497e-9982-9da1dc3f4ba1` |

## Result: no breaks. New MVIDs only.

- All five mods compile against the new DLLs.
- Scans: `CecilScan-MoveBuilding` 87 references resolved, `CecilScan-OwnedCount` 41, `CecilScan-TechSearch` 27, all
  0 unresolved; `CecilScan-BuiltCount`, `CecilScan-GlobalStorage`, `StaticScan-GlobalStorage`, `HarmonyHarness-BuiltCount`
  and `FrameworkScan`: pass.
- None of the patch targets changed: `BuildGrid3D.Draw`, `BuildManager.EnableBuildMode`, `BuildPointer.TryBuildActionInput`,
  `BuildSelectionCell.OverlapBoxNonAlloc`, `MultiInventory.Sort`, `RemovePointer.*`, `TechTreePageWidget.*`,
  `UIBuildingWidget.Redraw`, `UITooltip.AddItemWidgets`, `WgoBuildPointer.SetTarget`, `WgoData.GetCraftableMultiInventory`.

## Changes near the mods, not breaks

- **Owned Count:** `UITooltip.ShowCraftInfo` now passes the object to `TryAddRequiredExtensionWidget`, which hides a
  station extension that is already attached. `AddItemWidgets`, the patch target, is unchanged.
- **Tech Search:** `KnowledgeSystem.RevealTech` now restores a purchased tech that was hidden
  (`hiddenPurchasedTechs`, the tier-2 kitchen oven fix). `TechTreePageWidget` is unchanged; the search reads the
  widget's drawn techs, not the knowledge lists.
- **Global Storage:** `UIMultiInventoryWindow` reworked its gamepad navigation (Redraw/Hide); the mod doesn't patch
  that window. `MultiInventory.Sort` and `GetCraftableMultiInventory` are unchanged. `Inventory.GetItemsByType` gained
  an optional parameter (bait in bags); not used by the mod.
- **Move Building / Built Count:** `WgoData.TrySetWorker` now replaces an invalid worker; neither mod calls it. The
  new save fixes (`< 1009`: food energy buff, kitchen oven tech) touch no world objects.

## Pending

Start the game once: no `Game code changed` error for any of the five mods in `BepInEx/LogOutput.log`. `dist/` is
unchanged (release artifacts; rebuild on the next release).
