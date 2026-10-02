# Game update 1.008 (Steam build 25676698): re-audit

On 2026-10-02 the game was updated on Steam from 25601286 to 25676698, the version the changelog calls 1.008. The
user committed the game folder before (`a02a6a5f`) and after (`d2b212ca`) the update. The mods were audited
against the new build before the game was started with it.

## What changed

| | Before | After |
|---|---|---|
| `Assembly-CSharp` MVID | `3ffadd70-169d-402a-bcdf-59c322dfc26d` | `f7d46132-dd54-4296-8f19-917330ceadd9` |
| `LazyBearTechnology` MVID | `8a7a5e04-396d-4525-a779-3f0de1341398` | `f67c6993-6db1-4f08-8352-cf7315263736` |
| Save version | 1007.1 | 1008 |

- **`tests/GameDiff`:**
  - `Assembly-CSharp`: 43,027 → 43,102 members; 149 removed (10 not compiler-generated), 224 added, 78 changed.
  - `LazyBearTechnology`: 3 added, 2 changed (`TextStyleComponent.ApplyStyle`, `EasySpritesCollection.GetSprite`).
- **33 decompiled types differ.** The new decompile is in the scratchpad `decomp_1008`; `decomp_steam` is the old
  build.
- **Also changed:** `Assembly-CSharp-firstpass`, the A* DLLs, the burst DLL, 98 asset bundles and the resource files.
  The three bundles the mods' tools name are unchanged.

## Breaks found (fixed)

| Mod | What broke | Fix |
|---|---|---|
| **Mounts** | `Wgo.UpdateChunkVisibility` gained a second overload, `(bool isVisible, bool allowAsyncPartsLoad)`. `Patches.Verify` looked it up by name only, which is now ambiguous, so the start-up check would have failed. | The lookup names `(bool)`. `CecilScan-Mounts` checks for that overload. The call in `Mount.TrySpawn` compiles to the one-argument overload, which behaves as before (`allowAsyncPartsLoad: false`). |
| **Global Storage** | `ZombieEquipmentInventoryWidgetData.OnInstrumentCellPressed2` was removed: the zombie's hand slot is split into a tool slot and a weapon slot. The patch target was gone, so the build failed and the patch would have failed at run time. | All three cells (armour, tool, weapon) now unequip through the private `UnequipToPlayer`, which keeps the same `new MultiInventory(PlayerData).AddItem`. That method is the single target now. `StaticScan-GlobalStorage` checks it and the three public cells; `CecilScan-GlobalStorage` checks that it still gives the item back through `MultiInventory(PlayerData).AddItem`. |

Built Count, Move Building and Owned Count needed only the new MVIDs.

## Changes near the mods, not breaks

- **Chunk visibility (Mounts):**
  - `ChunkManager` now lets a Wgo load its parts asynchronously while the camera pans (the new
    `IChunkVisibilityAsyncLoadReceiver`).
  - The mod's donkeys are spawned with `ignoreChunkRegistration: true`, so the chunk manager never calls them. The mod
    calls `UpdateChunkVisibility(true)` itself, which loads synchronously as before.
  - `Wgo.RefreshVisuals()` became `RefreshVisuals(bool)`. It's private and the mod never calls it.
- **Empty drops refused (Mounts):**
  - `DropSystem.DropItem`, `DropItemAsDropView`, `GameSceneData.AddDrop` and `AddDropToQueue` now refuse an item
    with `Count <= 0`. `DropItem` returns false.
  - At load, `GameScene.SpawnDropsFromData` removes drops with a count of 0. That's the game's fix for items
    "teleporting into the inventory".
  - The mod's set-down and save paths log "LOST" when `DropItem` returns false. A 0-count cart entry would now show
    up as a LOST line, where before it was written as an empty drop. Watch the log for LOST lines.
- **Full-hands kick (Mounts' full cart):**
  - `PlayerData.CollectDrop` now calls `DropView.BlockAutoCollect()` when the keeper can't take a drop.
  - The block lifts once the keeper is more than 2 units away (`DROP_AUTO_COLLECT_REENABLE_DISTANCE`).
  - A ground pickup refused by a full cart therefore stays down until the keeper walks off. To check in game.
- **Teleport (Mounts' `StayMountedThroughDoors`):** `PlayerController.Teleport` now runs `MainGame.HiddenOptimization`
  after the old scene unloads, not before the teleport. That's the path the scene-door re-summon rides on. It was
  untested before and is still untested.
- **Saves (Mounts' persistence):**
  - `GameSave.PrepareToSave` and `SaveSlotData` gained `townCitizens` and `hasTownCitizens`.
  - The patched `SaveSystem.Save` and `SaveSystem.Remove` are unchanged.
  - The side file's stale check (the slot's save time and day) doesn't read the new fields.
- **Save fixes at load (version < 1008):**
  - orphaned conveyor transporters are dropped;
  - the panic-reduction machine resource is fixed;
  - zombie hands are split into a tool and a weapon;
  - the zombie debuff is refreshed.
  None of these touch what the mods own.
- **Global Storage, zombies and conveyors:**
  - `ConveyorSystem.AddWorker` and `RemoveWorker` now match workers by id.
  - `BodyPocketInventoryWidget.ShouldSkipItem` also skips the equipped weapon.
  - The `ZombieWgoData` and `ConveyorWgoData` overrides of `GetCraftableMultiInventory` still don't call the base
    method (checked with Cecil).
- **`Wgo.DoBuildRemove` (near Move Building):** removing a building with a zombie worker now drops the zombie and
  stores it. Move Building doesn't call or patch it.

## Found on the way

`StaticScan-GlobalStorage`'s "calls base" lines have always printed False whatever the IL says.
`Module.ResolveMethod` isn't supported under MetadataLoadContext, and the exception was swallowed. The comment there
now says so. The real check has been in `CecilScan-GlobalStorage` since an earlier update.

## Verification

- All five plugins, the two probes and the two examples build against the new game.
- `CecilScan-Mounts`, `CecilScan-MoveBuilding`, `CecilScan-OwnedCount`, `CecilScan-BuiltCount`, `StaticScan-GlobalStorage`,
  `CecilScan-GlobalStorage`, `ApiSurface` and `FrameworkScan` all pass on the new build.
- `HarmonyHarness-BuiltCount` passes with the new MVIDs.

## Versions

| Mod | Before | After | Change |
|---|---|---|---|
| Global Storage | 1.1.4 | 1.1.5 | the unequip patch target, MVIDs |
| Built Count | 1.0.5 | 1.0.6 | MVIDs |
| Move Building | 1.1.5 | 1.1.6 | MVIDs |
| Owned Count | 1.0.3 | 1.0.4 | MVIDs |
| Mounts | 0.1.0 | 0.1.0 | the `Verify` lookup, MVIDs (pre-release) |
| Showcase | 0.1.0 | 0.1.0 | none |

## The guard test (2026-10-02)

The game was started once with the mods built for 25601286, to the main menu only (no save loaded, nothing written).
- **Our guard worked:** Built Count, Global Storage, Mounts, Move Building and Owned Count each logged
  "Game code changed since this mod was audited", naming both DLLs, and stayed off.
- **The framework's Mods menu still said "Compatible" for all of them.** Our bridges registered with
  `requiresKnownBuild: false`, which means the framework never judges the mod. Now they use `true` and confirm the
  build only when their own guard passed (`docs/gk2-mod-framework.md`). Next time the list says "Unknown build".
- **The showcase said "loaded" and "Active"** although GK2 Mounts was off. It now says Mounts is off, logs a warning,
  and doesn't confirm the build.

Then the 1.008 builds were deployed (installed DLLs byte-identical to the builds).

## In-game checks

1. **The guard, with the old DLLs (before deploying):** start the game once with the mods built for 25601286.
   - Each mod should log "Game code changed since this mod was audited", naming both DLLs, and stay off.
   - Quit without saving and without sleeping. A save while Mounts is off would make its side file stale, and the
     kept donkeys wouldn't come back.
2. **After deploying:** each mod's "loaded" line, and no "Game code changed".
3. **Mounts:**
   - ride, drive, the cart's cargo, a full cart refusing a pickup (does the item wait until you walk off?);
   - a save and reload with kept donkeys;
   - no LOST lines.
4. **Global Storage:** unequip a zombie's armour, tool and weapon. Each should go to the pocket or the local zone,
   not to a far chest.
