# Game update 1.012 (Steam build 26624074): re-audit of the QOL mods

On 2026-11-13 the game went from 26404424 (1.011.1) to 26624074, which the game calls 1.012.7 (Steam's news post:
1.012). It was audited before the game was started with it, together with the GK2 Donkey API repo, whose
`docs/game-update-26624074.md` has the member diff, the decompile notes, the art and data diff and the save fixers.
This file is the QOL side.

| | Before | After |
|---|---|---|
| `Assembly-CSharp` MVID | `7274b6c4-2447-4669-baff-1ced3407f34e` | `c69d845c-001c-4bd4-9adf-7b288a697087` |
| `LazyBearTechnology` MVID | `46a6f4cd-56c5-410d-993a-de26a488dc60` | `53b976b3-a36d-477a-bea4-e43c6634914e` |

`Assembly-CSharp`: 687 members removed (42 not compiler-generated: the game's own dev windows, three path
calculation methods, `WgoPart.UpdateDropView` without a count, `DropView.GetSpawnPosFromSample`, three
`IgnoreChunkVisibility` getters, turret wait fields), 1,472 added, 403 changed; 19 new types (the PR machine, resting
on the bench, the map's day icons, mercenaries' pay, pooled drop views, sprite atlases, `RuntimeDevApi`).
`LazyBearTechnology`: 31 added, 11 changed (sprite caching in `EasySpritesCollection`, `LazyUI` statics and window
cache, `DevMode`). None of the removed members is used by a mod here.

## Patch targets and references

- **Every live mod compiles** against the new DLLs (built with `-p:Deploy=false`, nothing deployed), Event
  Decorations included. Move Building stays retired.
- **`DepManifest verify`: identical** for Built Count (35 entries), Global Storage (43) and Tech Tree Search (49):
  each writes its one info line at start (the game recompiled, every member identical) and runs.
- **Owned Count: changed, 1 of 61**: `UITooltip.AddItemWidgets(List<LazyWidgetDataBase>, ItemDef, bool, string, bool,
  bool, IReadOnlyList<ItemDef>)`. The sermon branch's early return became an `if` around the "crafted at" line, and
  `TryAddPrMachineLoadDaysWidget` adds a line for the PR machine's items at the end of both paths. The mod's postfix
  appends the owned count after whatever the game added; it still fits. At start the mod logs one warning ("the game
  changed the code of 1 of the 61 game members ... Running anyway until a re-audited build") and runs
  (`Safety.StopOnChangedGameCode` false). A build with the new manifest silences the warning; whether that is a 1.0.5
  release is the user's call (a released version is never rebuilt).
- **Seen in game 2026-11-13 (the deployed 1.011.1 builds, before any rebuild):** the four lines above exactly, no
  warning or error from any of the four after the start.
- **`tools/release-scans.sh`:** to run after the rebuild.

## Near the mods

- **Global Storage:** its patch targets (`WgoData.GetCraftableMultiInventory`, the `MultiInventory(PlayerData, bool)`
  constructor, `MultiInventory.Sort`, the `UIMultiInventoryWindowData` constructor, the `PlayerOrderExecutor`
  constructor) are unchanged. The zombie crafter's order logic changed again (`ZombieWgoData.CrafterReevaluateCurrentCraft`,
  stale deliveries cancelled when the craft changes, "zombies stopping work when the first craft in the queue has
  insufficient resources" fixed): with `IncludeZombieOrderDelivery` on, `[F] Put` still takes what the station lacks.
- **Built Count:** the building and craft windows it counts in are unchanged in what it reads; `UIItemCell` has 20
  changed members (the optional bank, `UIItemCellOptionalBank`), none of them the mod's.
- **Tech Tree Search:** the tech window members it uses are unchanged. Its text box fell back to the plain one in
  this session ("the game's text box wasn't loaded"), the documented path when the bug report window is not loaded.
- **Event Decorations** (unreleased): `SceneWgoContentPart`'s script is among the changed MonoScripts and the
  villagers' simulation gained `someone_inside` flags on their houses; to re-check against `docs/seasonal-events.md`
  before it is deployed.

## In game (pending)

After the rebuild: each of the four starts without a "recompiled" line, Owned Count's tooltip shows the owned count
under the game's new PR-machine line on a hypno device or newspaper, and the release scans are green.
