# GK2 Global Storage — Findings

As of 2026-09-24. Covers the Graveyard Keeper 2 install at `C:\Games\Graveyard Keeper 2` and the mod in `src\GK2GlobalStorage`.

## Summary

GK2 Global Storage 1.1.0 (1.1.1 is the same code, re-audited for game build 25509347; see "Game update 25509347") lets player crafting, building and alchemy use materials from storage chests in every zone, not just the current one. It is installed and loads cleanly in-game (14 storage containers found, 0 errors), and it has passed a full code audit. The specific in-game flows still need a hands-on test.

- **What changed:** 6 Harmony patches in one small BepInEx plugin. No game files are modified and nothing is written to the save.
- **What stays the same:** zombie and conveyor automation, quests and dialogue, trading, fishing and faith behave exactly as in the unmodified game.
- **Biggest audit result:** v1.0 had 7 side effects (wrong scope, scrambled material order, hidden containers counted, picker clutter, automation starvation). v1.1 fixes all of them.
- **Quest handovers from storage:** feasible, and low risk if limited to the dialogue answer. Not built yet. The recommended scope is quest handovers only (68 in the game).

## Game and tooling

| Fact | Value |
|---|---|
| Engine | Unity 6000.3.9f1 |
| Scripting backend | Mono (not IL2CPP), so it can be patched with Harmony |
| Game code | `GraveyardKeeper2_Data\Managed\Assembly-CSharp.dll` |
| Audited build MVID | `97f92f0d-a093-49fe-ba4d-74aa62fae81e` (Steam build 25533739, since v1.1.2). Before that `70946fdd-5c76-4d71-95c3-fea95051340d` (25509347, v1.1.1). Originally `dfae7296-aa7e-450f-92b4-4e617878a48d` (build 25457344) |
| Assembly-CSharp SHA256 | `C631C68A9606E48F7DBBF1394E0849D37E0DDCD7B345CBF6D58569EE545B9E03` (25509347). Originally `185A742AB88B13E0CA9AB26EE820DDEFCBA05FAFD76A6027939F889DE65CCD50` |
| Mod loader | BepInEx 5.4.23.5 (win x64), bundles HarmonyX 2.9.0 |
| Save folder | `%USERPROFILE%\AppData\LocalLow\Lazy Bear Games\Graveyard Keeper 2` (`Steam_1.dat/.info` + 3 rotating backups) |
| Save format | Odin Serializer (binary), records each object's concrete type |
| Game folder | `C:\Games\Graveyard Keeper 2`, a git repo (`init commit`, BepInEx and mod commits, then `Game update 25509347`). The previous build's game files remain in `C:\Games\Graveyard Keeper 2 (25457344)` until they're deleted |

Tools used, all outside the game folder:

- `ilspycmd` 8.2 (global .NET tool, run with `DOTNET_ROLL_FORWARD=Major` on the .NET 8 SDK) to decompile the game and the LinkedStorage mod.
- .NET 8 SDK to build the plugin and the test harnesses. A .NET Framework 4.8 harness ran HarmonyX tests.
- Mono.Cecil (from BepInEx) and `System.Reflection.MetadataLoadContext` for static checks.
- UnityPy 1.25.3 in an isolated Python venv (scratchpad) to read asset bundles. The bundles strip the Unity version, so `UnityPy.config.FALLBACK_UNITY_VERSION = "6000.3.9f1"` is required.

### The game's own mod support

`%USERPROFILE%\AppData\LocalLow\Lazy Bear Games\Graveyard Keeper 2\Mods` is official, but it only loads translations (`Languages/`) and voice-over packs. It cannot load code, so BepInEx is still needed. The two do not interact.

## How the unmodified game finds materials

The game already draws materials from chests, but only from chests in the same **world zone** as the workbench.

- `MultiInventory` is a list of inventories treated as one pool. It counts (`GetTotalCount`, `HasItemsById`) and removes (`RemoveItems`) across the list in order.
- `WgoData.GetCraftableMultiInventory(bool excludeWorkerInventory)` builds a workbench's pool: the worker's inventory, the chests in its zone (`MultiInventory(WorldZoneData)`), its craft slots, and the player's inventory if the player is in that zone.
- `MultiInventory(PlayerData, bool addCurrentPlayerWorldZone = true)` builds the player's pool: player inventory plus the current zone's chests. Build desks, town buildings, alchemy, item pickers and tooltips use it.
- Zone storage = objects in a zone's `wgoDataList` whose definition has `OpenInMultiInventory` and a non-zero `inventorySize`. Zone lists are saved in the save file and built once, when an area's content is first created. `PrepareForGame` only clears non-container zones.
- `MultiInventory.Sort()` puts non-fuel inventories first and fuel containers last (smallest first). It uses `List.Sort`, which is an unstable introsort once a list has more than 16 entries.
- `ZombieWgoData` and `ConveyorWgoData` override `GetCraftableMultiInventory` and never call the base method (confirmed with Mono.Cecil).
- When the player uses a workbench, `CraftInteractionHandler` sets the player as its worker, then clears it when the window closes.
- Materials are removed only in `CraftComponent.Start`, whose only caller (`TryContinueFromQueue`) refreshes the material check first, in the same call with the same worker. Instant crafts re-check after every unit.

## The mod (v1.1.0)

Source: `src\GK2GlobalStorage\Plugin.cs` and `GK2GlobalStorage.csproj` (netstandard2.1). Building copies the DLL to `BepInEx\plugins\GK2GlobalStorage.dll`.

### Patches

| # | What is patched | Effect |
|---|---|---|
| 1 | `WgoData.GetCraftableMultiInventory` (postfix) | Adds storage from all zones, only when the player is the worker and `excludeWorkerInventory` is false |
| 2 | `MultiInventory(PlayerData, bool)` constructor (postfix) | Adds storage from all zones for build desks, town buildings, alchemy, pickers, garden seed counts and tooltips. Skipped when `addCurrentPlayerWorldZone` is false or a suppression scope is active |
| 3 | `MultiInventory.Sort` (prefix) | Only for lists the mod extended: removes duplicate entries, then sorts stably with the game's rule. Local inventories stay first, faraway chests after them |
| 4 | `UIMultiInventoryWindowData` constructor (prefix + finalizer) | Pickers only show a faraway chest if it holds an item that picker accepts |
| 5 | `ZombieEquipmentInventoryWidgetData.OnArmorCellPressed2` / `OnInstrumentCellPressed2` (prefix + finalizer) | Unequipping zombie gear behaves like the unmodified game |
| 6 | `PlayerOrderExecutor` constructor (prefix + finalizer) | Hand-delivering zombie orders uses pocket and local zone only, unless enabled in config |

### What counts as storage

Every container in an **active container zone** (`IsContainer && IsActive`) whose definition has `OpenInMultiInventory` and a non-zero size, and that is **not hidden** and **is interactable**. Duplicates are removed by the underlying item data. By default two kinds are left out of the faraway set: fuel containers (they feed furnaces) and conveyor chests (both conveyor chest types hand items onto belts). Chests in the player's current zone behave exactly as before.

### Settings (`BepInEx\config\anvil.gk2.globalstorage.cfg`)

| Setting | Default | Meaning |
|---|---|---|
| `General.Enabled` | true | Master switch |
| `General.IncludeBuildAndAlchemy` | true | Patch 2 on or off |
| `Storage.IncludeFuelContainers` | false | Draw from fuel piles in other zones |
| `Storage.IncludeConveyorStorage` | false | Draw from conveyor chests in other zones |
| `Storage.IncludeZombieOrderDelivery` | false | Zombie-order deliveries from all storage |
| `Safety.DisableOnGameUpdate` | true | Do nothing if the game code changed since the audit |
| `Debug.VerboseLogging` | false (true in the current cfg) | Log discovered storage count and removed duplicates |

### Safety mechanisms

- **Version guard:** if the game assembly's MVID differs from the audited one, the mod patches nothing and logs an error.
- **All-or-nothing patching:** if any patch fails to apply, all patches are removed.
- **Error containment:** every patch catches its own exceptions. The sort patch falls back to the game's sort on error.
- **No persistent state:** nothing is written to the save. Tracking uses a `ConditionalWeakTable` in memory.

## Audit findings (v1.0 to v1.1)

| # | Problem in v1.0 | Fix in v1.1 |
|---|---|---|
| 1 | Patch 2 affected 22 call sites, not the 3 first claimed (a search was truncated) | Every site reviewed and classified; unwanted ones scoped out (patches 5 and 6, flag respected) |
| 2 | Zombie gear picker showed every chest, although the game passes `addCurrentPlayerWorldZone: false` | Patch 2 respects the flag |
| 3 | Unequipping zombie gear could send the item to a faraway chest | Suppression scope (patch 5) |
| 4 | Callers that `Add()` after construction re-run the unstable sort, putting faraway chests ahead of local ones about 100% of the time (counts stayed correct, no crash) | Stable sort for extended lists (patch 3) |
| 5 | Hidden, locked and inactive-zone containers counted | `!IsHidden`, `IsInteractable`, active container zones only |
| 6 | Fuel piles and conveyor chests in other zones could be drained | Excluded by default, configurable |
| 7 | Pickers drew one panel per chest in the world | Picker filter scope (patch 4) |
| 8 | (Hardening) An inventory added twice would be counted twice | Duplicate removal in patch 3 |

### Checks that came back clean

- **No free crafts:** check and removal always run together with the same worker.
- **No item duplication:** every picker removes the chosen item from the same list it displayed, by unique ID. Checked for autopsy, embalming, resurrection, graves, garden and zombie equipment.
- **Zombies and conveyors are unaffected:** their overrides never call the base method; conveyor workbenches are always `ConveyorWgoData`, including after loading.
- **Consistent lists:** workbenches, alchemy, garden, graves, autopsy, embalming, surveys, resurrection and town buildings use the same list for checking and removing.
- **Output is untouched:** crafted items never pass through a patched list. The only add paths are the zombie-equipment ones, now scoped out.
- **Durability tools** are checked and consumed from the worker's own inventory only; the patch does not affect them.
- **Alchemy:** ingredients checked and removed through the patched list; flasks checked and removed through the zone-only list.
- **Build desk extra inventories:** only the military base's temporary buildings inventory, created fresh each time. Alchemy boosts pass none.
- **Picker filters:** all 13 filter functions and their helpers only inspect the item; none builds another inventory list. Picker redraws reuse the panels built in the constructor.
- **Chests in unvisited areas:** included, because zone lists are saved in the save file.
- **Networking:** crafting and inventory changes are not networked. The game's networking covers lobby, save transfer, movement and combat only.
- **Threads:** the only background jobs are chunk-visibility jobs; all patched paths run on the main thread.
- **Out of scope by design:** quests, dialogue prices, trading, fishing bait, faith, the exhume certificate and the hotbar read the player's inventory directly.

## Verification performed

| Check | Method | Result |
|---|---|---|
| Patch targets | MetadataLoadContext against the real `Assembly-CSharp.dll` | 24/24: each target resolves to exactly one method, parameter names match, every field and property read exists |
| Overrides calling base | Mono.Cecil scan of every `ZombieWgoData` and `ConveyorWgoData` method | No call to `GetCraftableMultiInventory` |
| Game sort crash risk | Line-for-line port of the game's mscorlib `ArraySortHelper` introsort with the game's comparator | 0 exceptions in over 250,000 randomized lists |
| Game sort ordering | Same port, local plus appended lists | Faraway before local in about 100% of cases (the bug fixed by patch 3) |
| New sort and dedupe | Property test, 50,000 randomized lists | 0 violations: valid under the game's comparator, nothing lost, no duplicates, local first, local order kept |
| Harmony behaviour | Test harness on .NET Framework 4.8 with the bundled HarmonyX 2.9 | Prefix `__state` reaches finalizer; finalizer restores state when the method throws; private-method replacement works; unpatch restores the original |
| Constructor inlining | Same harness | A tiny constructor was inlined into a caller compiled before patching, so its patch was skipped. In the game only `PlayerOrderExecutor()` (24 bytes IL) is near Mono's roughly 20-byte limit; worst case is harmless. All other targets are 43 bytes or more |
| In-game load | `BepInEx\LogOutput.log` (22:57 run) | v1.1.0 loaded, guard passed, all patches applied, 14 storage containers found, 0 exceptions |
| Deployment | Byte comparison | Installed DLL identical to the final build |

## Cross-check against LinkedStorage

the downloaded Nexus package `GK2-LinkedStorageMod` (Linked Storage 0.2.3, experimental, built for Steam build 25467846) implements shared crafting plus a storage browser UI.

- **Adopted from it:** the stricter storage filter (`!IsHidden`, `IsInteractable`, active zone), deduplication by the underlying item data, and a game-version guard.
- **Not adopted, conflicts with this mod's goals:** it also patches `ConveyorWgoData.GetCraftableMultiInventory`, and it has no worker check. In its version zombies and conveyors pull from every chest, and it ignores `excludeWorkerInventory`.
- **Coverage difference:** it covers the build desk by injecting into `BuildManager` additional inventories, which is narrower and avoids picker side effects. But it misses town buildings, alchemy and pickers.
- **Do not install both:** both patch `WgoData.GetCraftableMultiInventory`.

## Known quirks and limitations

None of these lose or duplicate items.

- Tools that wear down during a craft must be in the player's own inventory. The craft window may show one sitting in a chest as available, as the game already does for same-zone chests.
- Alchemy flasks must be at the workbench or in its zone.
- Queued crafts that continue after the player walks away only use that zone's storage, and wait if short.
- With a zombie gardener assigned, the garden bed's Plant button can light up but planting does nothing.
- Cancelling a started craft drops its consumed materials at the workbench, as the game already does for same-zone chests.
- Fuel indicators and alchemy flask counts use the zone-only list (`excludeWorkerInventory: true`), so they do not count faraway storage.
- The mod turns itself off after any game update until it is rebuilt against the new build and re-audited.

## Feasibility study: quest handovers from storage

**Verdict:** feasible, and low risk if limited to the dialogue answer, which nearly every quest handover goes through. Not built.

### How quests take items today

- Each quest's `finishCheck.phraseReqs` lists requirements. `QuestFinishCheck.GetAnswerDataByReqs` turns item requirements into a dialogue answer: **Price** (items are taken) or **Lock** (items must be held, not taken).
- `UIMultiAnswerOption` displays the answer, checks it (`ShowIcons` via the local function `ProcessItems`), and removes the items (`HandlePrice`). All of it reads `PlayerData.Inventory` only.
- `QuestFinishCheck.IsReadyToFinish` drives only the NPC "ready" marker and interaction hint. The marker refreshes only on player-inventory changes, a new day, or visibility changes.
- `UIQuestInfoWindow.ConstructLinkedForQuestData` colours quest-log requirements from the player inventory.

### What the game data contains

Read from `GameBalance` (in `resources.assets`, object 2445) and all 24,170 asset bundles with UnityPy.

- **68 quest handovers require items:** 66 Price, 2 Lock. Found by matching the requirement's byte pattern and keeping only matches with a recovered quest phrase (73 matches, minus 5 false matches; full list in Appendix F). An earlier chat summary said "about 66 (64 Price, 2 Lock)"; that mixed up the two counts.
  - Bulk materials: wooden planks ×5, building kits ×5, wooden kits ×2–5, fish fillets ×5 (three quests), printed money ×20, signed printed money ×10, empty bottles ×10, alchemy bottles ×10, steel ingots ×3, armor ×5, shovels ×3, clean paper ×5, wheat ×5, ceramics ×6, lenses ×3, potatoes ×3, ink ×3, beer ×2.
  - Unique story items: royal pardon, forest pass, looters' keys, signed agreements, notebook, red eye (one Lock and two Price variants), 7 portal stones, prosthetic hands, printing press, trilogy.
  - Faith ×3 and ×100 (faith is an inventory item).
- **16 scripted dialogues also charge items** (`Flow_AnswerData`, 48 nodes): looters' keys ×1/×2/×4, green scrolls ×1/×3/×5/×10, iron or steel ingots ×3, a squad shield, plus one mercenary-hire price (`Flow_MercenariesPrice`). They go through the same `UIMultiAnswerOption`.
- **No quest script takes items another way:** all 82 `Flow_AddItem` nodes use the default mode, Add (29 add to objects, 17 give to the player).
- **Scripted player-item checks are intro possession gates only:** `HasPlayerItemInInv` for `sword_0`, `armor_0`, `intro_prison_key`, `note_lab_tower`, plus `RemoveItem("intro_prison_key")`. `Flow_HasItem`: 8 nodes, 5 of them "object inventory empty" checks.

### Proposed design (option A)

| Patch | Purpose | Verified call sites |
|---|---|---|
| Scope check: answer ID in `GameBalance.questDefByReqPhrase` | Limits the change to quest handovers, using the game's own lookup | — |
| Transpiler on `UIMultiAnswerOption.<ShowIcons>g__ProcessItems\|47_1` | Answer selectable if pocket plus storage covers the items | 1 × `Item.HasItemQuantityInInventory` |
| Transpiler on `UIMultiAnswerIcon.Setup` | Correct "have/need" numbers | 2 × `Item.GetTotalCountInInventory` |
| Transpiler on `UIMultiAnswerOption.HandlePrice` | Take from pocket first, then storage | 1 × `Inventory.RemoveItemById` |
| Transpiler on `QuestFinishCheck.IsReadyToFinish` | NPC "ready" marker counts storage | 1 × `Item.HasItemQuantityInInventory` |
| Transpiler on `UIQuestInfoWindow.ConstructLinkedForQuestData` | Quest log shows requirements as met | 1 × `Item.GetTotalCountInInventory` |

Storage would be the same set the mod already uses. Each transpiler would check that it found exactly the expected number of calls and cancel all patches otherwise, in addition to the version guard. The compiler-generated name `...|47_1` can change between game builds. Estimated size: about 200 lines, verified the same way as the current mod.

### Risks

- A handover could take a unique story item from a forgotten chest. The player still chooses the answer, and the dialogue shows counts.
- Quest markers can lag after moving items between chests (cosmetic). Fixing it needs extra event hooks.
- Toolbelt items still would not count, as in the unmodified game (affects the shovel ×3 quest).
- The quest census is approximate: full decoding of `GameBalance` failed because UnityPy's generated type tree does not match the stored layout. The script counts are exact.

### Scope options

- **A: quest handovers only** (recommended). Covers the ask; "quest or not" uses the game's own lookup.
- **B: A plus scripted dialogue costs.** Adds looters' keys, green scrolls, ingots, the shield and the mercenary price. Some of these behave like shop payments.
- **Not recommended:** patching scripted "has item" checks. No quest handover needs it, and it would break the intro gates.

## Files, install and rollback

| Path | Contents |
|---|---|
| `src\GK2GlobalStorage\Plugin.cs` | Mod source |
| `src\GK2GlobalStorage\GK2GlobalStorage.csproj` | Build project; `dotnet build -c Release` rebuilds and copies the DLL into the game |
| `C:\Games\Graveyard Keeper 2\BepInEx\plugins\GK2GlobalStorage.dll` | Installed mod |
| `C:\Games\Graveyard Keeper 2\BepInEx\config\anvil.gk2.globalstorage.cfg` | Settings |
| `C:\Games\Graveyard Keeper 2\BepInEx\LogOutput.log` | Load and error log |
| `winhttp.dll`, `doorstop_config.ini`, `.doorstop_version`, `BepInEx\` in the game folder | BepInEx loader |

- **Disable the mod:** set `Enabled = false`, or delete `GK2GlobalStorage.dll`.
- **Remove BepInEx entirely:** delete `winhttp.dll` (loader off), or remove the files above.
- **Game folder repo:** BepInEx files were committed by the user; the current DLL differs from the committed copy until committed again.
- **After a game update:** the mod disables itself. Rebuild against the new `Assembly-CSharp.dll`, update the audited MVID, and re-run the static checks.

### Still to do

- In-game test of v1.1: workbench craft, build-desk placement, one picker (alchemy or garden) and resurrection, each using materials from a chest in another zone.
- Decide on the quest-handover scope (A or B) before implementation.

---

## Game update 25533739: re-audit (v1.1.2)

On 2026-09-26 the game was updated from 25509347 to 25533739. The user's first download was byte-identical to 25509347 (every file compared); the second download was the real update. `.git`, `BepInEx\` and the loader files were moved into the new `C:\Games\Graveyard Keeper 2`, and the user committed it as `updated to 25533739`. The game was launched once before the mods were updated. Both mods logged the MVID mismatch and disabled themselves, as designed, and that is what the user saw ("build count not working").

**What changed in the game's code:**
- `Assembly-CSharp`: 84 files changed and 24 added.
- `LazyBearTechnology`: `GamepadTypeData` changed, and `ExceptionReportWindow` was added.
- `Assembly-CSharp-firstpass` also changed. `UnityPlayer.dll` is unchanged.

**Changes near the mod, none affecting it:**
- **Crafting core:** `CraftComponent` did not change.
- **Crafting windows:** `UIAlchemyWindow`, `UIAutopsyWindow`, `UIEmbalmWindow`, `UIFuelCraftWindow`, `UIGardenBedWindow`, `UIPorterStationWindow`, `UIResourceBasedCraftWindow`, `UIResurrectionWindow`, `UISingleCraftWindow`, `UITownBuildingWindow` and `UIBuildingWindow` now only add `infoWidget.Hide()` when they close.
- **`ZombieWgoData`:** porters call `vendorSystem.TryResolveOrders()` after delivering to a town palette. The `GetCraftableMultiInventory` override is unchanged, and Cecil confirms it still doesn't call the base method.
- **`WorldZoneData`:** new `RemoveItemsFromTownPalettes`, used for vendor orders.
- **`Item`:** the capacity check behind `cantAddItemsCount` now counts free slots inside bags. That's the add path, which the mod doesn't touch.
- **`ItemDef`:** new `isQuestItem`, used only by `DropViewAtomSprite`.
- **`Wgo`:** zombie capsule colliders are switched off while zombies move.
- **`PlayerController` and `MainGame`:** asset unloading and preloading.
- **Vendor order changes:** `Vendor`, `VendorSystem`, `VendorOrderData`, `UIVendorOrders*` and `TownPaletteInteractionHandler`. None goes through a patched method.
- **Save version 1006, one-time fixes run at load:** `ExpandCraftInventory(50, kitchen_oven, kitchen_oven_t2)`, `DropForeignItemsFromGardenWells` (drops non-water items) and `RemoveStuckGardenBeds`.
- **Call-site totals are unchanged:** `new MultiInventory(` 36, `GetCraftableMultiInventory(` 25, `UIMultiInventoryWindowData(` 17, `new PlayerOrderExecutor(` 1, `OnArmorCellPressed2` 2, `OnInstrumentCellPressed2` 2.

**New game features relevant to modding:**
- **`ModLoaderDetection`:**
  - Looks for `doorstop_config.ini`, a `BepInEx\` folder, `winhttp.dll`, the `DOORSTOP_*` environment variables, and the loaded assemblies `BepInEx`, `BepInEx.Preloader` and `0Harmony`.
  - Logs `loader: …` to `Player.log`. It logged `loader: doorstop_config.ini, BepInEx, winhttp.dll, assembly:BepInEx.Preloader, assembly:BepInEx, assembly:0Harmony`.
  - Adds `modLoader {detected, evidence}` to bug reports.
  - It is informational only: nothing is blocked or changed.
- **In-game bug reporter:**
  - Posts to `https://bugs.lazybeargames.com/api/v1/gk2/reports`, only after the player opens the window (pause or main menu) and submits.
  - At startup it retries reports that were submitted earlier and failed (`FlushPending`).
  - Writes `bug-reporter-relevant-save.txt` in the save folder: a version line, the slot name and a demo flag.
- **`ExceptionReportWindow`:** shows a popup on Unity `LogType.Exception`. The mod catches all its own exceptions and logs them through BepInEx as errors, so it can't trigger it.

**Verification:**
- **Static suite:** output identical to 25509347 apart from the MVID, with 22 OK.
- **Cecil base-call check:** passes.
- **Rebuild:** 0 errors. There is 1 new warning, MSB3277: the game now references a newer `System.IO.Compression` than the netstandard2.1 reference pack. The mod doesn't use it, and at runtime the game supplies it.
- **Install:** v1.1.2 is installed, byte-identical to the build.

**Code change:** only the MVID and the version string.

## Game update 25509347: re-audit (v1.1.1)

On 2026-09-25 the game was updated from Steam build 25457344 to 25509347. The user moved `.git`, `BepInEx\`, `winhttp.dll`, `doorstop_config.ini`, `.doorstop_version` and `block-exes-recursive.ps1` into the new build's folder, renamed that folder to `C:\Games\Graveyard Keeper 2`, and committed it as `Game update 25509347`.

**What changed in the game's code:**
- Changed DLLs: `Assembly-CSharp`, `Assembly-CSharp-firstpass`, `AstarPathfindingProject`, `AstarPathfindingProjectExamples`, `LazyBearTechnology`, plus the new `Unity.Pipeline.Attributes`.
- Unchanged: `UnityPlayer.dll`, Unity, TextMeshPro, `UnityEngine.UI` and BepInEx.
- The save format moves to version 1005 (`SaveCodeFixes`). A save opened in the new build may not open in the old one.

**Decompiled comparison** (the old and new decompiled source trees are in the scratchpad):
- `Assembly-CSharp`: 39 files changed and 1 was added (`ConveyorConnectorHint`).
- `LazyBearTechnology`: 3 small changes (`LazyButton` icon code, `PlaySoundOnEnable`, `TextStyle.ApplyStyleToString`), none used by the mod.

**Changes that touch mod-related code, and why none affects the mod:**
- **`CraftComponent`:**
  - New `TryRestoreWaitingForWorkerPickUpStatus` and `TryRecoverInterruptedFinish`, plus a try/catch around finishing a craft.
  - The new methods only change the status or drop a finished element from the queue. They never check or take items.
  - The places that start crafts and take materials are identical: one `Start(currentCraftElement)` in `TryContinueFromQueue` after `UpdateQueueElementsCraftStatus`, `RemoveRequirements` ×2, `ProcessInstantCraft`, and 5 external callers. So the "no free crafts" argument still holds.
- **New save fixes (`saveVersion < 1005`)**, run once when an old save loads:
  - `RestartCrematoriumCraftsWithBody` goes through `TryStartCurCraft`/`TryStartCraft`, which both existed before. That path calls `TryContinueFromQueue`, so materials are freshly checked right before the craft starts.
  - `DropForeignItemsFromSurveyTables` and `RecoverInterruptedCraftFinishes` don't use any patched code.
  - Patch 1 applies only if the crafter is the player (`Worker is PlayerController`). If a crematorium were player-attached while the save loads, the result would be the intended behaviour anyway.
- **`ZombieWgoData`:** changes are in `PrepareForGame` (restores the pickup status) and `CrafterStartCraftActivity` (skips `TryContinueFromQueue` while waiting for pickup). Its `GetCraftableMultiInventory` override (line 1092) is unchanged. Cecil confirms it, and the `ConveyorWgoData` override, still never call the base method.
- **`UIResurrectionWindow` and `UIResurrectionWindowData`:** only the gamepad "can't start" dialog changed. The patched calls inside are unchanged.
- **Other changed files that contain a patched call:** `PlayerController` (the talent bonus now reads the toolbelt) and `TownBuildingPlaceInteractionHandler` (a sound). The changed lines are not the call sites.
- **Call-site totals (old → new) are unchanged:**

  | Call | Old | New |
  |---|---|---|
  | `new MultiInventory(` | 36 | 36 |
  | `GetCraftableMultiInventory(` | 25 | 25 |
  | `UIMultiInventoryWindowData(` | 17 | 17 |
  | `new PlayerOrderExecutor(` | 1 | 1 |

  The decompiled files for all patch targets (`MultiInventory`, `WgoData`, `UIMultiInventoryWindowData`, `ZombieEquipmentInventoryWidgetData`, `PlayerOrderExecutor`) are unchanged.

**Verification:**
- **Static suite** (`verify\`, MetadataLoadContext), run against both builds: all 7 patch targets and 15 properties and fields are OK. The output is identical apart from the MVID, including every target's IL size.
- **Cecil base-call check:** the zombie and conveyor overrides don't call the base method.
- **Build:** rebuilt against the new DLLs with 0 warnings and 0 errors. That confirms every member the mod uses still binds.
- **Harness limit:** the .NET Framework 4.8 HarmonyX harness can't load this mod's patch attributes. One patched game type implements a default interface method, which the game's Mono runtime supports but .NET Framework doesn't. So a runtime patch test for Global Storage is only possible in-game.
- **Install:** v1.1.1 is installed, byte-identical to the build.

**Code change:** only the audited MVID (`70946fdd-5c76-4d71-95c3-fea95051340d`) and the version string (1.1.1).

**Still to do in-game:**
- The log should show `GK2 Global Storage 1.1.1 loaded.`, the storage count and no errors.
- Test crafting from remote storage once.

# Appendices

## Appendix A: every `new MultiInventory(PlayerData, ...)` call site (patch 2)

22 sites in the game. "Global" means the mod adds storage from all zones there.

| Call site | What it does | Adds or removes items? | Mod behaviour |
|---|---|---|---|
| `BuildManager.cs:57` `EnableBuildMode` | List used for build-mode placement | Removes (via `WgoBuildPointer.cs:111,126`) | Global; then `.Add()` of additional inventories re-sorts (patch 3 keeps order, drops duplicates) |
| `BuildManager.cs:111` `CanBuild` | Build window affordability check | Reads | Global |
| `BuildManager.cs:138` `OnBuildPressed` (script-mode buildings) | Check and remove | Removes | Global |
| `UIBuildingWindow.cs:93` `Redraw` | Build window counts | Reads | Global |
| `Flow_OpenTownBuildingWindow.cs:39` | Town building build or level-up: check, then remove after a 0.6 s fade using the same list | Removes | Global |
| `TownBuildingPlaceInteractionHandler.cs:52` | Same pattern for town building places | Removes | Global |
| `UITownBuildingWindow.cs:51` | Town building window counts | Reads | Global |
| `UIAlchemyBoostsWindow.cs:39` | Alchemy boost list counts; `.Add()` of additional inventories (none passed in practice) | Reads | Global |
| `PlayerController.cs:191` `WorkerMultiInventory` | Used by `UIAlchemyWindow` 374/589/678 (ingredient check and removal) and `UITooltip.cs:901` | Removes (alchemy) | Global |
| `UITooltip.cs:1653` | Craft requirement tooltip | Reads | Global |
| `UIGardenBedWindow.cs:679` | Seed count for the Plant button | Reads | Global |
| `UIResourceBasedCraftWindowData.cs:64` | Survey window ingredient counts | Reads | Global |
| `UIPrayWindowData.cs:40` | `PlayerMultiInventory` property | Never read anywhere | No effect |
| `UIMultiInventoryWindowData.cs:14` | The item picker window (see list below) | Picker callbacks remove by unique ID from this same list | Global when `addCurrentPlayerWorldZone` is true, filtered by patch 4 |
| `PlayerOrderExecutor.cs:5` (field initializer) | Hand-delivering items a zombie crafter asked for (`CraftInteractionHandler.cs:183`) | Removes (`RemoveItem`) | Suppressed by patch 6 unless `IncludeZombieOrderDelivery` |
| `ZombieEquipmentInventoryWidgetData.cs:60` | Unequip armor, item returned with `AddItem` | Adds | Suppressed by patch 5 |
| `ZombieEquipmentInventoryWidgetData.cs:95` | Unequip hand item, returned with `AddItem` | Adds | Suppressed by patch 5 |
| `MultiInventory.cs:51` | The constructor itself (inserts player inventory at 0, then sorts) | — | Patched |
| Remaining sites (`CharMainPageWidgetData:80`, `ChestInteractionHandler:20`, `Flow_OperateCargoLift:96`, `Flow_FinishZombie*Craft`, `ZombieGardenerOrderExecutor:18`, `ZombieWgoData:3021/3025/3999`, `WgoData:1401`) | Use the zone constructor `MultiInventory(WorldZoneData)` or other constructors | — | Not patched |

Item picker (`UIMultiInventoryWindowData`) users and how each removes the picked item:

| User | Filter function | Removal |
|---|---|---|
| `UIAlchemyWindow.cs:635` ingredient pick | `IsItemValidForMix` | Later via `WorkerMultiInventory.RemoveItems` |
| `UIAutopsyWindowData.cs:100,109` organ insert/change | `CanInsertOrgan`, `CanChangeOrgan` | `currentMultiInventory.RemoveItemFromInventoryByUID` (lines 239, 269) |
| `UIEmbalmWindowData.cs:82` | `CanEmbalm` | `currentMultiInventory.RemoveItemFromInventoryByUID` (103) |
| `UIGardenBedWindow.cs:526` fertilizer, `:610` seed | `IsItemValid`, `IsSeedValid` | Garden craft via bed's `GetCraftableMultiInventory` with player as worker |
| `UIGraveElementWidgetData.cs:93` | `IsGraveItem` | Grave craft; `UIGraveWindow.cs:101` sets player as worker |
| `UIPrayWindowData.cs:106` sermon pick | `CanUsePray` | Nothing consumed (`CraftElementSermon` requirements are empty) |
| `UIResourceBasedCraftWindowData.cs:153` survey pick | `IsItemAllowed` | `CraftElementSurvey.RemoveCraftRequirements` via `GetCraftableMultiInventory`, player is worker |
| `UIResurrectionWindowData.cs:132` collar pick | lambda (collar type, skull borders) | `currentMultiInventory.RemoveItemFromInventoryByUID(Collar)` (196); liquid via `Wgo.GetCraftableMultiInventory().RemoveItems` (198), player set as worker at line 51 |
| `ZombieEquipmentInventoryWidgetData` 35/43/70/78/105 | `IsArmor`, `IsInstrumentOrWeapon`, `CanUpgradeCollarTo` | Passes `addCurrentPlayerWorldZone: false`, so the mod adds nothing |
| `UIMultiInventoryWindow.cs:29` `TestDraw` | always false | Debug only |

All 13 filter functions and their helpers (`TryFindGardenCraft`, `IsSeedableSeed`, `CanAddItemToBody`, `CanChangeItemInBody`, `GetOccupiedSlotCount`) only inspect the item. `UIMultiInventoryWindow.Redraw` reuses the panels built in the constructor.

## Appendix B: every `GetCraftableMultiInventory` caller (patch 1)

21 call sites. Patch 1 only adds storage when the player is the worker and `excludeWorkerInventory` is false.

| Call site | Use | excludeWorkerInventory |
|---|---|---|
| `CraftComponent.cs:673` `UpdateQueueElementsCraftStatus` | Craft status for queued elements | false |
| `CraftElementBase.cs:383` `CanStartCraft` | Material check | false |
| `CraftElementBase.cs:436` | Zombie output-space check (`CanAddItems`) | **true** (unpatched) |
| `CraftElementBase.cs:528` `RemoveCraftRequirements` | Material removal | false |
| `CraftElementSurvey.cs:44,52` | Survey material removal | false |
| `ConveyorCraftElement.cs:122` | Conveyor craft check (resolves to `ConveyorWgoData` override) | false |
| `CraftDefExtensions.cs:34` | Group-item variant expansion | false |
| `UIBaseCraftSelectionWindowData.cs:120` | Craft cell counts | false |
| `UIBaseCraftSelectionWindowData.cs:224` | Fuel indicator for mix crafts | **true** (display only) |
| `UIBaseCraftSelectionWindowData.cs:265` | Fuel-craft start check | false |
| `UIBaseCraftWidgetData.cs:124` | Craft widget counts | false |
| `UITooltipNeedsItemWidgetData.cs:25` | Tooltip counts | false |
| `UIResurrectionWindowData.cs:87,101,158,198` | Liquid display, check, removal | false |
| `UIAlchemyWindow.cs:361` | Flask count | **true** |
| `UIAlchemyWindow.cs:679` | Flask removal | **true** |
| `UIInfoWidget.cs:191,205` | Fuel counts (flag from data) | varies (display) |

## Appendix C: further game-code facts established

- **Player crafting:** `CraftInteractionHandler.cs:49-51` sets the player as worker only if the bench has none; it clears the worker on close. `PlayerController.SetCraftActivity`/`SetHPActivity` also set the player as worker. Worker lookup: `IWorker.FromId` returns `MainGame.PlayerController` for the player's GUID, otherwise a zombie from `ZombieSystemData`.
- **Vanilla quirk, line 1407 of `WgoData.cs`:** the player inventory is added to a bench's pool when the player is in the same zone and is not the worker.
- **Queue behaviour:** `TryContinueFromQueue` runs `UpdateQueueElementsCraftStatus` (fresh check) and then `Start`; `TrySetIdxCurrentCraftFromQueue(useCachedCraftStatuses: true)` only picks the index. `ProcessInstantCraft` calls `GetStartCraftStatus` after each unit.
- **Auto crafts:** `CraftComponent.Update` retries a delayed queue every 1 s with the worker at that moment.
- **Conveyor workbench:** `ConveyorWorkbenchComponent.cs:368` briefly sets the player as worker to call `ContinueAutoCraft`. Safe, because every definition with `conveyorType != None` is created as `ConveyorWgoData` (`GameSceneData.cs:119`, `ConveyorBuildPointer.cs:14`, `GameBalance.cs:1193` builds `conveyorWgosCache`).
- **Conveyor chests:** `ConveyorChestComponent` pulls items from belts (`PerformItemTransfer`) and both it and `ConveyorChestOutComponent` hand items onto belts (`GiveItem`).
- **Fuel containers:** created with `Inventory.Create(size, isFuelInventory: true)`, which adds `FuelContainerSerializedItemProperty`.
- **Hidden / interactable / active:** `IsHidden` is set by `Flow_SetWgoHiddenState`, `CPA_SetWgoHidden`, `FightingLevel` and `Flow_DisableWorkshopConveyors`. `IsInteractable` is set by `Flow_SetWgoInteractableState`, `LazyExpression`, `MilitaryBaseData`, `PowerSourceInteractionHandler` and `TakeAllInteractionHandler`. Zone `IsActive` is toggled by `FightingLevel`; `WorldZoneData.TryAddWgoData` refuses inactive or non-container zones.
- **Durability tools:** `CraftElement.CanStartCraft` and `RemoveCraftRequirements` use `CraftableAttachedWorker.WorkerInventory` only; `UICraftItemCell`/`UITooltipCraftItemCell` display from the pool.
- **Alchemy mix:** `CraftElementMix.RemoveCraftRequirements` is empty; `UIAlchemyWindow.OnStartMix` removes ingredients and flasks itself.
- **Garden:** `UIGardenBedWindow` calls `data.TrySetWorker()` (player, only if no worker) before `TryApplySeed` / fertilizer. With a zombie gardener assigned the worker stays the zombie.
- **Cancel:** `CraftComponent.Cancel` drops `CraftInput` at the workbench.
- **Vanilla bug, unchanged:** unequipping zombie gear via `MultiInventory.AddItem` silently loses the item if pocket and zone storage are full (the return value is ignored).
- **Vanilla quirk, unchanged:** resurrection creates a free bronze collar if none was chosen.
- **Military base build desk:** `MilitaryBaseData.CreateBaseBuildingsInventory` creates a fresh 99-slot inventory holding building items each time.
- **Crafted output:** never routed through a patched `MultiInventory` (checked via every `AddItem`/`TryAddItem`/`CanAddItems` call site).
- **Networking:** `LazyNetwork`, `ConnectionManager`, lobby windows, `LobbyHelper` (publishes `GameSave`), commands for movement, zombie attacks and tool use (`WgoDataCommand`). No crafting or inventory commands.
- **Threads:** only `ChunkDataIntersectsJob`/`ChunkVisibilityJob` and a Burst job registration.
- **Performance:** `WorldData.GetWgoData(SGuid)` is a null-safe dictionary lookup (`wgoDataByUidCache`). The mod rebuilds its storage list at most once per frame and per save.
- **Saves:** `SaveSystem` uses `OdinBinaryFileSerializer` (`.dat`) plus JSON (`.info`); `WorldZoneData.wgoDataList` is a serialized public field.
- **Game sort comparator:** not antisymmetric for two fuel containers (`Compare(x,y) = -1` when `x` is smaller, but `Compare(y,x) = 0`). Self-comparison returns 0, so the introsort never threw in testing.
- **Player-only item paths (not patched):** `QuestFinishCheck:72`, `UIMultiAnswerOption:286,427`, `UIMultiAnswerIcon:119-121`, `UIQuestInfoWindow:236`, `Flow_HasItem:75`, `Flow_AddItem:75`, `LazyExpression:675` (`HasPlayerItemInInv`), `InspirationData:40` (faith), `Trading:175`, `UIFishingWindow:374` and `UIFishingWindowData:65` (bait), `UIGraveWindowData:62,73,81` (exhume certificate), `UIHotBarWidget:89`, `CharInspirationPageWidget:197`.

## Appendix D: methodology corrections and tool caveats

Mistakes made during the work, and how each was caught and corrected:

- **Truncated search (v1.0 scope claim):** the first `MultiInventory` grep was cut off at 80 lines, so patch 2 was described as "build desk, alchemy, tooltips". The full list has 22 sites (Appendix A).
- **Vacuous IL check:** a `MetadataLoadContext` check reported "overrides do not call base", but `Module.ResolveMethod` is unsupported there and errors were swallowed. Redone with Mono.Cecil, which confirmed the result.
- **Silent bundle-scan failure:** the first UnityPy scan loaded 0 of 24,170 bundles (stripped Unity version) and reported "no graphs". Fixed with `FALLBACK_UNITY_VERSION`; the rescan had 0 failures.
- **Broken awk pattern:** the first picker-filter check used a regex with an unescaped parenthesis and returned meaningless "0 refs". Redone with plain string matching.
- **`sed` mangled the csproj:** backslashes in a Windows path were interpreted as escapes; the file was rewritten.
- **Quest count mix-up:** chat said "about 66 handovers (64 Price, 2 Lock)"; the correct figures are 68 handovers, 66 Price, 2 Lock.
- **`GameBalance` decoding:** UnityPy's type-tree generator produced list fields as single objects and could not resolve `TriLibDef.dll` (not shipped), so full decoding failed ("Negative length read"). Brute-force offset decoding gave only false starts. The census uses a byte-pattern match instead (Appendix F).
- **`ilspycmd` latest (11.x):** fails to install on the .NET 8 SDK; version 8.2.0.7535 was installed and run with `DOTNET_ROLL_FORWARD=Major`.
- **HarmonyX on .NET 8:** the BepInEx build of HarmonyX (targets old .NET Framework) crashes in `AccessTools` on .NET 8; tests ran on .NET Framework 4.8 instead.

Assumptions not verified in-game:

- MonoMod on Mono disables inlining of patched methods for later-compiled callers. Not verified; only matters for `PlayerOrderExecutor()` (24 bytes IL), where the failure mode is harmless.
- Mono's default inline limit is about 20 bytes of IL. From knowledge of the Mono JIT, not measured.
- The in-game flows (workbench, build desk, pickers, resurrection with faraway materials) have not been played through with v1.1.

## Appendix E: LinkedStorage 0.2.3 details

- Files: `BepInEx/plugins/LinkedStorage/LinkedStorage.dll` and `README.md` only; a separate source archive is mentioned but not present. Built with OpenAI Codex, per its README.
- Guard: disables itself unless the game assembly's SHA256 matches Steam build 25467846.
- `CraftInventoryPatch`: postfix on `WgoData.GetCraftableMultiInventory` **and** `ConveyorWgoData.GetCraftableMultiInventory`, no worker or `excludeWorkerInventory` check; replaces `inventoryList` with a merge deduplicated by `Inventory.Data`.
- `BuildingInventoryPatch`: prefix on `BuildManager.OpenBuildingWindow(Wgo, List<Inventory>)` and `EnableBuildMode(Wgo, BuildData, List<NeedItemData>, List<Inventory>)`, injects storage into `additionalInventories` minus what the player's own pool already has (`LinkRules.AdditionalOnly`).
- `StorageIndex.Eligible`: not hidden, interactable, `OpenInMultiInventory`, non-zero size, zone not null and active, inventory data present. Cache refreshed every 1 s of real time, sorted by zone ID then object ID, re-validated per call.
- Extra UI: a storage browser (type tabs, location picker, search, hide empty, paging keys) via `ChestOpenPatch`, `ChestHidePatch`, `StorageChangedPatch`, `StorageBrowser`.
- Its README reports that 0.2.2 showed zero materials at the Yard construction desk because only crafting was patched, and that 0.2.3 prevents local chests being counted twice.

## Appendix F: quest item requirements found in `GameBalance`

Byte pattern used, per `QuestPhraseRequirement` (requirement, entity, itemCount, gameResAtom, dayNumber, order): `requirement ∈ {0,1}`, `entity = 0`, an identifier `itemId`, `1 ≤ count ≤ 9999`, then empty `type`, `0.0` value and empty `dayNumber`/`order`. The phrase was recovered from the string just before the requirement list. Raw data: `quest_item_reqs.json` in the scratchpad.

| Phrase (quest answer) | Type | Item | Count | Note |
|---|---|---|---|---|
| `zmb_worker_template` | Price | `zombie_worker` | 15 | likely false match (not a quest phrase) |
| `zmb_assistant` | Price | `zombie_assistant` | 15 | likely false match (not a quest phrase) |
| `wz_town_guard_barracks` | Price | `0_tut_craft_start` | 1 | likely false match (not a quest phrase) |
| `24_doctor_zombies_showing_1` | Lock | `powder_dreams` | 1 |  |
| `10_village_food_samples_0` | Price | `wheat` | 5 |  |
| `163_village_fish_1` | Price | `fish_roach` | 1 |  |
| `9_village_brothers_pottery_1` | Price | `ceramic_1` | 6 |  |
| `12_village_boat_wood_1` | Price | `wooden_plank` | 5 |  |
| `12_village_boat_tools_1` | Price | `details_1_bronze` | 5 |  |
| `wz_church` | Price | `19_base_ceremony_tutorial` | 1 | likely false match (not a quest phrase) |
| `14_guard_faith_head_1` | Price | `faith` | 3 |  |
| `14_guard_faith_bell_1` | Price | `quest_bell` | 1 |  |
| `41_forest_bonfire_food_1` | Price | `berry_pie` | 1 |  |
| `134_history_keeper_donkey_1` | Price | `beer` | 2 |  |
| `27_port_astrologer_cards_1` | Price | `card_deck` | 1 |  |
| `76_astrologer_press_device_1` | Price | `hypno_device_charged` | 1 |  |
| `78_astrologer_jack_1` | Price | `agreement_frontman_signed` | 1 |  |
| `44_port_violine_1` | Price | `quest_paint` | 1 |  |
| `65_doctor_lacquer_1` | Price | `lacquer` | 1 |  |
| `66_port_smell_1` | Price | `violine` | 1 |  |
| `67_port_incense_1` | Price | `incense_basic` | 1 |  |
| `87_doctor_linda_notebook_1` | Price | `notebook` | 1 |  |
| `34_port_looters_keys_1` | Price | `keys_looters` | 1 |  |
| `45_guards_reading_1` | Price | `book_love` | 1 |  |
| `52_village_pass_papers_1` | Price | `temp_village_head_paper` | 1 |  |
| `53_forest_pass_1` | Price | `forest_pass` | 1 |  |
| `57_forest_mine_larry_robert_1` | Price | `candle_basic` | 1 |  |
| `162_village_pardon_ink_1` | Price | `ink` | 3 |  |
| `56_forest_pardon_1` | Price | `royal_pardon` | 1 |  |
| `99_workshop_repair_material_1` | Price | `building_kit_1` | 5 |  |
| `99_workshop_repair_material_2` | Price | `wooden_kit_1` | 5 |  |
| `99_workshop_repair_pr_1` | Price | `agreement_frontman` | 1 |  |
| `42_crossroad_nun_supplies_1` | Price | `clean_paper` | 5 |  |
| `73_crossroad_bigger_crossroad_1` | Price | `wooden_kit_1` | 2 |  |
| `73_crossroad_bigger_crossroad_t2` | Price | `furniture_kit_1` | 2 |  |
| `40_village_fishing_line_1` | Price | `thread_flax` | 3 |  |
| `39_village_frill_lace_1` | Price | `frill` | 1 |  |
| `36_port_trademaster_lenses_1` | Price | `lence` | 3 |  |
| `142_foundry_zombie_1` | Price | `zombie_power` | 3 |  |
| `insert_portal_stone_lust` | Price | `portal_stone_lust` | 1 |  |
| `insert_portal_stone_envy` | Price | `portal_stone_envy` | 1 |  |
| `insert_portal_stone_pride` | Price | `portal_stone_pride` | 1 |  |
| `insert_portal_stone_sloth` | Price | `portal_stone_sloth` | 1 |  |
| `insert_portal_stone_gluttony` | Price | `portal_stone_gluttony` | 1 |  |
| `insert_portal_stone_wrath` | Price | `portal_stone_wrath` | 1 |  |
| `money` | Price | `157_base_palace_home` | 1 | likely false match (not a quest phrase) |
| `103_workshop_steel_1` | Price | `ingot_steel` | 3 |  |
| `158_astrologer_palace_prothesis_1` | Price | `hand_prothesis` | 1 |  |
| `158_astrologer_palace_press_1` | Price | `small_printing_press` | 1 |  |
| `152_astrologer_dark_hand_1` | Price | `hand_prothesis_elegant` | 1 |  |
| `100_village_swamp_faith_1` | Price | `faith` | 100 |  |
| `113_sewers_god_red_eye` | Lock | `red_eye` | 1 |  |
| `113_sewers_god_give_eye_1` | Price | `red_eye` | 1 |  |
| `150_base_90not_give_eye_1` | Price | `red_eye` | 1 |  |
| `144_crossroad_home_complete_1` | Price | `wooden_kit_2` | 2 |  |
| `146_stilt_master_materials_1` | Price | `building_kit_1` | 2 |  |
| `105_guards_armor_1` | Price | `armor_3` | 5 |  |
| `159_crossroad_palace_trilogy_1` | Price | `trilogy` | 1 |  |
| `159_crossroad_palace_device_1` | Price | `speech_writing_device` | 1 |  |
| `153_crossroad_dark_chair_1` | Price | `chair_comfortable` | 1 |  |
| `149_sluice_tools_tools_1` | Price | `shovel_3` | 3 |  |
| `93_linda_potato_1` | Price | `potato` | 3 |  |
| `160_linda_palace_bottles_1` | Price | `alchemy_bottle_2` | 10 |  |
| `160_linda_palace_chocolate_1` | Price | `chocolate` | 1 |  |
| `154_linda_dark_drape_1` | Price | `drape_luxury` | 1 |  |
| `insert_portal_stone_greed` | Price | `portal_stone_greed` | 1 |  |
| `102_town_captain_beer_1` | Price | `beer` | 2 |  |
| `124_village_money_stamp_1` | Price | `printed_money` | 20 |  |
| `124_village_money_chest_1` | Price | `printed_money_signed` | 10 |  |
| `135_town_fisher_good_decent_1` | Price | `fillet_fish_river` | 5 |  |
| `135_town_fisher_good_sea_1` | Price | `fillet_fish_sea` | 5 |  |
| `135_town_fisher_good_myst_1` | Price | `fillet_fish_myst` | 5 |  |
| `139_town_homobonus_16` | Price | `bottle_empty` | 10 |  |

Matches without a recoverable phrase: 378 (other structures with the same byte layout, excluded). Top item IDs: `talent_yellow` ×73, `talent_green` ×39, `talent_blue` ×12, `i_b_carousel` ×2, `i_b_conveyor_furnace_1` ×2, `zperk_factory_operative` ×2, `i_b_conveyor_loader` ×1, `i_b_conveyor_workbench_1` ×1, `i_b_conveyor_workbench_2` ×1, `i_b_conveyor_workbench_3` ×1.

## Appendix G: dialogue and scripted item logic in detail

- **`QuestPhraseRequirement`:** `Requirement` = Price, Lock, Day, Order; `Entity` = Item, GameResAtom, Day, Order; fields `itemCount`, `gameResAtom`, `dayNumber`, `order`.
- **`AnswerData`:** `lockRes`, `costRes`, `rewardRes`, `fakeRewardRes` (each a `SmartRes` = `GameRes` + `List<ItemCount>`), `dayNumber`, `order`, `customHideCondition`, `notAvailable`.
- **Sources of answer data:** `QuestFinishCheck.GetAnswerDataByReqs` (quest table), `Flow_AnswerData` (scripted: locks, costs, rewards, fake rewards, day, order, hide), `Flow_MercenariesPrice` (items from `needItems`, else `money` as a game resource).
- **`Flow_MultiAnswer.cs:79-92`:** a connected answer input uses the scripted `AnswerData`; otherwise, if the answer ID is in `GameBalance.questDefByReqPhrase`, the quest's `finishCheck` builds it.
- **`UIMultiAnswerOption`:** `ShowIcons` computes the private `available` flag from lock/cost game resources (`IsEnoughRes`), items (`HasItemQuantityInInventory` on the player inventory), day number and finished vendor order. `OnItemPress` returns early if not available, then `HandlePrice` (items via `PlayerData.Inventory.RemoveItemById`, game resources via `SubRes`) and `HandleRewards` (items to the player inventory, dropped at the player if full). `forceSelectionNoHandle` skips payment.
- **`UIMultiAnswerIcon`:** only used by `UIMultiAnswerOption`; shows `have/need` from the player inventory.
- **Quest marker refresh:** `Wgo.SubscribeQuestFinishIndicator` hooks the player inventory's `OnItemsAdd`/`OnItemsRemove`, `OnGameResChanged` and `OnNewDayStarted`; `GameScene.RedrawWgosWithInteractionEvents` runs on player control-state changes; `Wgo.DrawMultiAnswerReadyWidget` on visibility.
- **`Flow_HasItem` (player mode):** true if the item is in the inventory **or** toolbelt. **`Flow_AddItem` Remove (player mode):** removes from the inventory only. A vanilla mismatch, but no graph uses Remove mode.
- **`LazyExpression` item functions:** `HasPlayerItemInInv` (inventory or toolbelt), `HasPlayerItemEquip` (toolbelt), `RemoveItem`/`UseItem` (one stack looked up in the player inventory by ID), plus object-scoped `HasItemInInv`, `HasItemInInvByGroup`, `RemoveItemByGroup`, overhead-item functions and others.
- **Asset scan totals (all 24,170 files, 0 load failures):** `Flow_MultiAnswer` 177 nodes / 92 graphs; `Flow_AnswerData` 48 / 16; `Flow_AddItem` 82 / 10 (46 fully decoded; `giveType` absent in all 82 = Add; `addToPlayer: true` in 17); `Flow_HasItem` 8 / 4 (7 decoded: 5 object "inventory empty" checks, 2 player checks with item ID wired from another node); `Flow_MercenariesPrice` 1; `HasPlayerItemInInv` 8 occurrences and `RemoveItem("intro_prison_key")` 2, all in `resources.assets`.
- **Scripted answer costs seen:** iron ingots ×3, steel ingots ×3, squad shield ×1, looters' keys ×1/×2/×4, green scrolls ×1/×3/×5/×10, and day locks (`day_wrath`, `day_lust`).

## Appendix H: timeline

| Time (2026-09-23/24) | Event |
|---|---|
| Start | Game identified as Unity 6 Mono; decompiled; zone-limited storage found |
| 22:37 | v1.0 built and installed with BepInEx 5.4.23.5 |
| — | User tested v1.0: works. Save location documented |
| 22:56 | v1.1 built after the first audit (8 fixes, LinkedStorage cross-check) |
| 22:57 | User ran v1.1: loaded, 14 storage containers, 0 errors |
| 23:03 | Final build with duplicate removal in the sort patch (installed, identical to build) |
| Later | Quest handover question and feasibility study (game running, nothing changed) |
| 2026-09-24 | This findings file |

## Appendix I: artifacts left on disk

Persistent:

| Path | What |
|---|---|
| `src\GK2GlobalStorage\` | Mod source, build output (`bin\`, `obj\`), this file |
| `C:\Games\Graveyard Keeper 2\` | BepInEx loader + mod DLL + config + log |
| `%USERPROFILE%\.dotnet\tools\ilspycmd` | Global .NET tool (v8.2.0.7535); remove with `dotnet tool uninstall -g ilspycmd` |
| `%USERPROFILE%\.nuget\packages\` | NuGet cache (e.g. `System.Reflection.MetadataLoadContext` 8.0.0) |

Session scratchpad (temporary, may be cleaned up): the session's temporary scratchpad folder

| Item | What |
|---|---|
| `decomp\` | Decompiled `Assembly-CSharp` (2,213 files) |
| `linked\` | Decompiled LinkedStorage |
| `arraysorthelper.cs` | Decompiled game mscorlib sort |
| `bep\`, `bepinex.zip` | BepInEx download |
| `sorttest\`, `sorttest2\` | Sort simulation and property test |
| `verify\`, `ilscan\`, `cecilscan\` | Static checks (ilscan is the vacuous one, superseded by cecilscan) |
| `harmonytest\` | HarmonyX pattern tests (.NET Framework 4.8) |
| `upyenv\` | Python venv with UnityPy 1.25.3 and TypeTreeGeneratorAPI |
| `scan_bundles.py`, `scan_*` | First bundle scan (invalid: all loads failed) |
| `scan2.py`, `scan2_*` | Valid bundle scan and snippets |
| `balance*.py`, `nodes*.py`, `questscan.py`, `textassets.py`, `diag.py` | `GameBalance` decoding attempts and diagnostics |
| `quest_item_reqs.json`, `quest_table.md` | Quest requirement census |

Online: a Claude Doc "GK2 Global Storage — Findings" was started before the switch to this local file; only its summary section is filled. `https://claude.ai/code/artifact/0a13ccf3-d788-409f-a148-af0914e931df`

## Appendix J: open items

- In-game test of v1.1 flows (see "Still to do").
- Commit the current DLL and this file in the game-folder repo if wanted.
- Decide the quest-handover scope (A or B), then implement with the game closed.
- Decide whether to delete the half-filled online doc.
- Set `VerboseLogging = false` once testing is done.
- The user's firewall script `block-exes-recursive.ps1` sits in the game folder; it blocks the game's executables from network access and does not affect modding.
