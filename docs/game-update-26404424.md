# Game update 1.011.1 (Steam build 26404424): re-audit of the QOL mods

On 2026-11-03 the game went from 26297960 (1.011) to 26404424, which the game calls 1.011.1. It was audited before the
game was started with it, together with the GK2 Donkey API repo, whose `docs/game-update-26404424.md` has the member
diff, the decompile notes, the art and data diff and the save fixers. This file is the QOL side.

| | Before | After |
|---|---|---|
| `Assembly-CSharp` MVID | `36326881-56e1-4dee-a10f-2893840f0687` | `7274b6c4-2447-4669-baff-1ced3407f34e` |
| `LazyBearTechnology` MVID | `46a6f4cd-56c5-410d-993a-de26a488dc60` | the same (not rebuilt) |

`Assembly-CSharp`: 53 members removed (3 not compiler-generated: `PickupOrder.HasItemsToPickUp()`,
`PickupOrder.PickupInventoryOwner()`, `CraftComponent.TryStartCurCraft(bool)`), 65 added, 37 changed. The
13 decompiled types that differ are a hotfix of 1.011's zombie-worked autocrafts (the delivery, pickup and start
orders, `AutoCrafterOrderPlacer`, `CraftComponent`'s zombie-serviced queue) plus two save fixers. The other game
assemblies the update recompiled (`Assembly-CSharp-firstpass`, the A* pathfinding) are referenced by none of the
mods here.

## Patch targets and references

- **Every live mod compiles** against the new DLLs. Move Building, retired with 1.011, still fails on the
  members missing since then (`docs/game-update-26297960.md`).
- **`DepManifest verify`: identical** for Built Count (35 entries), Global Storage (43), Owned Count (61) and Tech
  Tree Search (49). That holds for the deployed DLLs, `dist/` and the released zips (Global Storage 1.1.6, Tech Tree
  Search 1.0.1). Each writes one info line at start (the game recompiled, every member identical) and runs.
  **Nothing needs a re-release.**
- **`tools/release-scans.sh`: 15 of 15 green**: the four CecilScans, `StaticScan-GlobalStorage`, `FrameworkScan`,
  `NoLinqScan`, `HarmonyHarness-BuiltCount` (patches applied against the new game), the four manifest verifies and
  `check-dist`. The release run's own log in `release/logs` was put back unchanged; this run's is
  `qol-scans-10111.log` in the job's `audit_26404424` folder.

## Global Storage near the changed code

All of the mod's patch targets are unchanged: `WgoData.GetCraftableMultiInventory`, the
`MultiInventory(PlayerData, bool)` constructor, `MultiInventory.Sort`, the `UIMultiInventoryWindowData` constructor
and the `PlayerOrderExecutor` constructor. What changed around them:

- **`[F] Put` at a zombie-worked autocrafter**, with `Storage.IncludeZombieOrderDelivery` on: the pool is the one
  the `PlayerOrderExecutor` patch widens to every zone, and that patch is unchanged.
  - `DeliveryOrder.CanOrderBeExecuted` still asks the executor for the order's whole count first.
  - For a workbench order, `ExecuteOrder` now takes from the pool only what the station still lacks
    (`AutoCrafterOrderPlacer.GetMissingCount`, over the station's own craft inventory), and puts in the same.
  - Before, it took the order's whole count. The placer now sizes and replaces its orders to the missing count, so the
    two usually agree.
  - Orders a zombie crafter places for its own inventory are unchanged.
- **The zombie crafter still never takes from chests.** Its start decision reads the station's craft inventory
  (`HasMissingIngredients`), so the finding of 2026-10-10 (`src/GK2GlobalStorage/FINDINGS.md`, "Zombie-worked stations
  and storage") stands.
- **"No free crafts" still holds.** Materials are still removed only in `CraftComponent.Start`. Its one caller,
  `TryContinueFromQueue`, refreshes the material check (`UpdateQueueElementsCraftStatus`) in the same call.
  `CraftInteractionHandler.CanPlayerStartZombieServicedCraft` now asks `GetStartCraftStatusFor(null, pool)` with
  the same pool as before (the station's craftable multi-inventory plus the pocket).

Built Count, Owned Count and Tech Tree Search use nothing near the changed classes.

## In game (2026-11-03, 21:35)

- **Start:** the API, Built Count, Global Storage, Owned Count and Tech Tree Search each wrote the one info line
  ("the game was recompiled since this mod was built ... identical"), and the framework's build contract passed
  for each. No warning from any of them.

## The rebuild of 2026-11-03, 22:07

The four QOL mods (and the API, in its repo) were rebuilt against 1.011.1 and deployed: the shared check
knows the pathfinding as a scope (`docs/dependency-check.md`; these mods use none of it, their manifests
are unchanged). So the next start differs from the 21:35 one:

- **No "recompiled ... identical" line** from any of the five: each is now built against this game, so the fast
  path passes and nothing is logged; the framework's build contract still passes for each.

## In-game checks (pending)

1. Global Storage with `IncludeZombieOrderDelivery` on: `[F] Put` at an assistant-worked autocrafter takes from storage
   only what the station still lacks.
