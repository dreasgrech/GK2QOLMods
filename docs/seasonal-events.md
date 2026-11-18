# Seasonal events: what the game has and how a mod can switch them

Investigated 2026-11-11 on game 1.011.1 (Steam build 26404424, `Assembly-CSharp` MVID
`7274b6c4-2447-4669-baff-1ced3407f34e`) for a possible new QOL mod that turns the game's seasonal content on or off
by the mod's own setting instead of the PC's date. Built 2026-11-11 as **GK2 Event Decorations**
(`src/GK2EventDecorations`): design points 1, 3 and 5 below (the gate per event, discovery of new events, the ghost
home), seen in game on 1.012 (Halloween forced On: the decorations up, 2026-11-13). On 2026-11-13 also points 2 and 4
(per-piece switches; the live switch), own dates, areas, kinds, glow and walk-through: built and deployed; the live
switch seen in game 2026-11-15 (three take-downs and put-ups from the options window), the rest **not yet seen in game**.
On 2026-11-15 the settings were gone through: the live switch is always on (its setting gone), SendGhostHome is cfg
only, and every group and every item of 1.012's decorations is under one of the areas and kinds (layout: section
"Game 1.012"). The same day a leak was found and fixed: the game's `ShouldLoad` (`return IsHalloweenEvent();`) and
`Resolve` (`if (!IsHalloweenEvent()) return headId;`) ask the gate the mod answers with the Ghost setting, so a part
following the game's dates took the ghost's answer; they now run with the gate bypassed (`Events.Bypass`). A recursive
sweep of the 1.012 decompile (game and LazyBear) finds five files naming the gate: those two, `TemporaryEventDates`
itself, `LazyExpression` and `Flow_IsHalloweenEvent` (the ghost's rows and graph only). `tests/HarmonyHarness-
EventDecorations` holds the truth table (2430 combinations, both seasons forced).

## Verdict

**Feasible, and simple at the core.** The game has exactly **one** seasonal event, Halloween, and everything it does
goes through **one static method**, `TemporaryEventDates.IsHalloweenEvent()`. It is a plain date check (24 Oct to 7 Nov
inclusive, `DateTime.Now`). A Harmony prefix on that method forces Halloween on or off on any date.

- **Applied when a save loads:** low risk. This is when the game itself checks the date for the decorations, and the
  decorations write nothing into the save (checked below).
- **Switched live in the middle of a session:** possible through two existing `GameSceneConfig` methods, but it has
  three open points that need one in-game probe run (section "Open points").

## What is seasonal in the game

Searched in the decompile (code), the addressables catalog and every one of the 25,284 bundles (assets and visual
scripts), and in `resources.assets` (GameBalance), `globalgamemanagers.assets`, `sharedassets0.assets`, `level0` and
every `Managed\*.dll`.

| Searched for | Found |
|---|---|
| `halloween`, `christmas`, `xmas`, `easter`, `valentin`, `holiday`, `festiv`, `seasonal`, `new year`, `TemporaryEvent` | Halloween only. "easter" hits are the `PixelFonts/Eastern` font; "xmas" hits in DLLs are words like `IndexMask`. |
| Real-date reads in code (`DateTime.Now/Today/UtcNow`, `.Month`, `DayOfYear`) | Only `TemporaryEventDates`. The rest are bug-report timestamps, the save slot's date and the Workshop console. LBT's `Common.GetYearDay()` has no callers. |
| Date logic in data (the expression engine's `#NOW#`, `#TODAY#`, `MonthOf`, `DayOf(`, `YearOf`, `DaysBetween`, `HourOf`, `MinuteOf`, `AddMonths`, `AddDays`) | None in any bundle or in GameBalance. They exist only as function classes in LBT. |
| Conditional scene content (`ConditionalLoadContentPart` subclasses; `conditionalLoads` in all 6 `GameSceneConfig`s) | One subclass (`HalloweenConditionalLoadContentPart`), one asset (`HalloweenConditionalLoad`), one content entry (RuinedTemple index 29). |

The class name `TemporaryEventDates` and the generic `ConditionalLoadContentPart` base suggest the developers plan more
events (Christmas would be the obvious next one). The mod should therefore discover events instead of hard-coding
Halloween (see "Mod design").

### What Halloween changes (four pieces, one gate)

| # | Piece | How it is gated | When it is checked |
|---|---|---|---|
| 1 | **Decorations**: `RuinedTemple_ContentPart_6_HalloweenData`. Pumpkins (6 kinds), flags, scarecrows, pies, candles, `DecorLight`s, 11 particle systems, 3 FX trigger zones (`SPBats`, `BirdsTriggerZone`), 104 `Obstacle` objects (107 colliders in all). Grouped as HalloweenVillage, Port, Dungeons, PlayerZone, PlayerInterior, TavernInterior, RestaurantInterior, BarracksInterior, Barracks, AstrologerInterior, WarehouseInterior, DecorationsStatic, FX. | `GameSceneConfig.CanLoadContent` → `HalloweenConditionalLoadContentPart.ShouldLoad()` → gate; the part's own `conditionalLoad` is checked again in `TryLoadSceneWgoContentData`. | Only in `WorldData.PrepareForGame` → `AddMissingConditionalContent`, i.e. when a save is loaded. **A session started on 7 Nov keeps the decorations past midnight.** |
| 2 | **The ghost's night walks** (`npc_ghost`). The ghost itself is a permanent WGO in every save (placed by `RuinedTemple_ContentPart_2`; the 1.011 save fixer adds it to older saves). It waits at `RT_spot`, where the game places it (both at (-199.68, 0, 0) in RuinedTemple). | Two GameBalance `GameLogicDef` rows with condition `IsHalloweenEvent()`: `halloween_npc_ghost_start_action` (fires `WGOFireEvent("npc_ghost","start_action")`) and `halloween_npc_ghost_go_home` (fires `go_home`). The ghost's graph (`VisualScripts/WGODataScripts/npc_ghost.asset`) checks `Is Halloween Event` again on every `roll_action`: yes → walk to a random village point; no → teleport back to `RT_spot`. | Each day at the rows' in-game times, and at every step of the walk. **Mostly self-correcting:** when the gate turns false, the ghost goes home at its next step, also across a save (see "Save safety"); one exception, the walk home. |
| 3 | **Head swap**: `HalloweenHeadSkinOverride.asset` maps head 1051 → 1060 (a zombie head; the keeper is not affected; see docs in GK2DonkeyAPI `game-update-26297960.md` check 5). | `HalloweenHeadSkinOverride.Resolve` → gate. Callers: `SkinPresetGK2` (3×), `SkinChangerGK2`, `UIWorkerIcon`, `DropViewAtomMesh.ActivateZombie` (lying/carried zombies). | When a view or icon is built; already-built views keep their head until rebuilt. |
| 4 | **Script hooks** for designers: the FlowCanvas node `Flow_IsHalloweenEvent` and the expression function `IsHalloweenEvent()` (`LazyExpression`). | Gate. | Only piece 2 uses them today (the ghost graph and the two GameLogic rows). |

Four bat/eyes VFX prefabs live in `WorldFxs/Halloween/` (`world_vfx_bats_left/right`, `bats_circling_vfx`,
`glowing_eyes`). They are not gated themselves; they are most likely spawned by the decorations' FX trigger zones
(not traced).

## Save safety

- **Decorations write nothing into the save.** The part's `wgos`, `wsos`, `worldZones`, `wgoUniqueIds` and
  `wsoUniqueIds` are all empty (read from the bundle). `AddDataToSceneFromContentData` copies only those three lists,
  and the part is a plain `SceneWgoContentPart`, not a `FightingLevel`. The instance is plain GameObjects kept in
  `GameSceneConfig.contentDataRefInstances` (`keepLoaded = 1`) and released on `UnPrepareFromGame` (back to the menu).
- **The ghost's position and walk are saved** like any NPC's: `WgoData.movementComponent` serializes the path,
  status and `onPathCompleteEvent`; `MovementComponent.Init` resumes a `Moving`/`PathBuilding` walk after load and
  `WgoData.OnPathComplete` fires the saved event. So a save made during a village walk loads with the walk going on,
  its `roll_action` fires at the end, and with Halloween off the graph teleports the ghost home.
- **Exception, the walk home (`go_home`):** that `Flow_GoTo` has no `fireEventOnFinish`; its next step (teleport to
  `RT_spot`) is the flow's C# callback (`onFinishPath`), which is not saved. A save made during the morning walk
  home loads with the ghost walking to `village_brothers_jeffry_02`, where it then **stays** while the gate is off
  (in the vanilla game too, after 7 Nov). The mod fixes it (design point 5).
- **Head swap is display only:** all five `Resolve` sites only read it; `SkinPresetGK2.GetHeadPartForDisplay`
  builds a separate override part; the preset's head id (1051) is never changed.
- Forcing Halloween on or off and later removing the mod leaves nothing behind, apart from the ghost case above
  (which the vanilla game has as well).

## Mod design (proposal)

A new plugin (own project, own GUID `dreas.gk2.eventdecorations`, built as GK2 Event Decorations, the optional Mods-menu link like the other QOL mods).

1. **The gate.** A Harmony prefix on both `TemporaryEventDates.IsHalloweenEvent()` and `IsHalloweenEvent(DateTime)`,
   applied in `Awake` (before the game JITs its callers). Setting per event: `Auto` (the game's dates) / `On` / `Off`.
   The method is tiny, so Mono could inline it into callers; the probe must show that all four pieces see the forced
   value (our earlier `HalloweenSwap` patched `HalloweenHeadSkinOverride.Resolve`, never the gate itself).
2. **Optional per-piece switches** (if wanted): decorations via `HalloweenConditionalLoadContentPart.ShouldLoad`,
   head swap via `HalloweenHeadSkinOverride.Resolve`, ghost via the gate only (its two triggers and its graph use
   nothing else).
3. **Discovery, so future events are covered** (built 2026-11-11, **taken out 2026-11-18** at the user's word: an
   event nobody has read gets no switch; the mod names it in the log and leaves it to the game, and
   `tests/CecilScan-EventDecorations` names it at the game update. What follows is the proposal as it was)**:**
   - every public static `bool Is*Event()` on `TemporaryEventDates` becomes an event row (name from the method);
   - every concrete `ConditionalLoadContentPart` subclass, and the `conditionalLoads` entries of
     `MainGame.Instance.gameSceneConfigs`, are listed at start (logged; a new one we don't know gets a warning, as
     with our dependency check).
   No hard-coded index 29 or content name.
4. **Live switching (stage 2, after the probe):** off → `GameSceneConfig.TryUnloadSceneDataContent(name)` for each
   loaded conditional part; on → `TryLoadSceneDataContentByName(name, out _)` (the name is the asset name without
   `Data`, here `RuinedTemple_ContentPart_6_Halloween`; the gate must already answer `true`, because the load checks
   `ShouldLoad` again). Head swap live needs the views redrawn; the ghost needs nothing beyond point 5.
5. **The ghost home:** after a save loads (and after a live switch to off), if `npc_ghost` is not at `RT_spot` and
   not walking, fire `roll_action` on it (`Flow_FireEventOnWgoData`'s path); its graph's "no" branch teleports it
   home. Applies only while the gate is off.

### Open points (one probe run, batched)

1. All four pieces see the forced gate (inlining).
2. A live load shows the decorations where they belong, and a live unload leaves a clean Player.log.
3. Navigation after a live switch. The `Obstacle` colliders are physics colliders, so the keeper is blocked or freed
   immediately. Whether the Recast scan (`GlobalNavigationManager.InitRecastGraphAsync`, at scene load) also
   rasterizes them depends on the scene's A* layer settings, which are not in code. If it does, NPC paths stay as
   scanned until the next load (cosmetic: NPCs walking through new pumpkins or around removed ones).
4. Chunks: the part's 151 `ChunkableObjectComponent`s are not registered with `ChunkManager`. Every caller of
   `RegisterChunks`/`RegisterDynamicChunkableObject` was read: `GameScene` (its baked `chunkComponents`,
   constructor parts, baked data, terrain, WGO and WSO views), `LazyTerrain`, `Wgo`, `Wso`, `DropView`, fighting
   levels and stages; none takes a content part's components, and `KeepLoaded` is read only in `WorldData`. So
   releasing the instance leaves no dangling references. In game: the decorations stay visible (no culling) and
   nothing is logged on release.
5. The ghost: a save made during its walk home, loaded with Halloween off, and the fix of design point 5.

### "From script"

The request ("enable/disable seasonal things with script") is read as: **the mod's own setting decides** (config file
and the Mods menu, per event `Auto` / `On` / `Off`), applied at save load, with a live switch as stage 2. The user went
ahead with that (2026-11-11, "GK2 Event Decorations"); a console command, a key or an API for other mods would only
change stage 2.

## Game 1.012 (Steam 26624074, checked 2026-11-13)

- The event code is identical (`TemporaryEventDates`, both Halloween classes, the flow node, `ConditionalLoadContentPart`,
  `SceneWgoContentPart`, `WorldData`, `GameSceneConfig`, `Flow_GoTo`); `MovementComponent` changed (`StartPath`),
  the mod reads only `IsMoving`. The mod's manifest: 22 entries identical; CecilScan: Halloween still the only gate.
- Bundle names changed (the decorations are `fb04211b70682a76847719661160c285.bundle` now, the scene config
  `246733e8bf82936b8fa172c5120b23c7.bundle`); the ghost graph and the two config assets kept theirs.
- **New: six seasonal food items, with no source** (corrected 2026-11-13; first read as recipes). GameBalance
  `itemDefs` (sort order 144-149): `cook_halloween_cookie` (icon `i_halloween_pechenie`), `cook_halloween_pie`,
  `cook_christmas_cookie` (`i_christmas_pechenie`), `cook_christmas_pie`, `cook_easter_egg`, `cook_easter_rabbit`:
  food (groups `u_bag`, `food_bag`, stack 30, energy `25+3*PPar("food_eng_buff")`, price 0, no tech points). Each
  id occurs **once** in GameBalance, its own ItemDef (a real dish, `cook_golden_carp`, 9 times: recipe at
  `kitchen_oven`, survey, tech); `food_bag`/`u_bag` occur nowhere else; no vendor, drop, quest, script or code names
  them. Icons new in the `Atlases/Icons.spriteatlasv2` (`c95ef5e2…`) and `EasySpritesCollection` (`6c0e0e03…`);
  names in English, Russian translated ("Новогодний" = New Year's for Christmas), the other nine languages still
  English. Nothing gates them (no date, event, tech, hidden flag) because nothing gives them: groundwork for events
  to come, with no Christmas or Easter gate yet. (Subagent report, verified by the hit counts.)
- **Zombie pictures for the Halloween head are addressable now:** `drops_item_zombie_pose1_t1060` / `pose2_t1060`
  (`a928b433…`, `4e996cbd…`), picked by `DropViewAtomMesh` through `ResolveDropIconId` at `:214` and in the new pooled
  path `ActivatePooledZombie` `:494`. (In 1.011.1 the two existed only embedded in `BigItem.prefab`: GK2DonkeyAPI
  `docs/game-update-26404424.md`.) The mod's ZombieHead switch reaches both paths (it answers `Resolve`).
- Checked unchanged: the head table, the ghost graph, the condition asset (identical bundles); all six scene configs
  (only RuinedTemple index 29 is conditional); the GameBalance diff 1.011.1 → 1.012 has no other seasonal token; the
  1.012 save fixer names nothing of the events. New 1.012 types (BigDropViewPools, MapDayType with the sin days, the
  PR machine, the rest bench, …) are not event-related.
- **The decorations' layout** (`tools/game-bundles.py tree fb04211b70682a76847719661160c285.bundle 3 --names`), what
  the mod's areas, kinds, glow and walk-through switch:
  `RuinedTemple_ContentPart_6_HalloweenData` → `HalloweenDecorationsStatic` → 12 groups: `HalloweenVillage` (50
  items), `Port` (16), `PlayerZone` (22), `PlayerInterior` (3), `TavernInterior` (18), `RestaurantInterior` (9),
  `Barracks` (8), `BarracksInterior` (7), `AstrologerInterior` (1), `WarehouseInterior` (3), `Dungeons` (3), `FX` (14:
  3 `VFXBats` with `SPBats` + `BirdsTriggerZone`, 9 `glowing_eyes`, 2 `bats_circling_vfx`). Items by name:
  `common_hw_pumpkin_01..06` (69), `common_hw_flags` (30), `common_hw_01..06` (28), `common_hw_pie_01/02` (8),
  `common_hw_scarecrow` (2), `common_hw_plants_01/02` (3). Inside an item: the picture, its glow (`DecorLight` or
  `Decor Light (astrologers_candles_v2)`, 44 in all) and its blockers (`Obstacle`, 104 objects, 107 colliders with the
  three bat zones). Every collider has `m_IsTrigger` false, the bats' `BirdsTriggerZone` too, so walk-through goes by
  the `Obstacle` name. Scripts on the objects: RoundPos, ChunkableObjectComponent, Object3D(Mesh), Horizontal/Vertical/
  DayNightSprite, WorldFxColliderTriggerComponent, ChunkBoundsContributor: none calls SetActive or registers itself
  (no LightFaker), so the mod's SetActive stays.

## Evidence

| What | Where |
|---|---|
| Gate | `TemporaryEventDates` (consts 10/24 to 11/7, `IsMonthDayInInclusiveRange`) |
| Callers | `Flow_IsHalloweenEvent`, `LazyExpression.cs:368`, `HalloweenConditionalLoadContentPart.ShouldLoad`, `HalloweenHeadSkinOverride.Resolve` |
| Load path | `MainGame.StartGame` → `GameSave.PrepareForGame` → `WorldData.PrepareForGame` → `AddMissingConditionalContent` → `GameSceneConfig.TryLoadConditionalSceneDataContent` |
| Decorations bundle | `1f3ae3be25acf834e216442936db9f9d.bundle` (`SceneContents/WgoContentsData/RuinedTemple_ContentPart_6_HalloweenData.prefab`) |
| Condition asset | `d4788959c2e122842e5bc89f431d811e.bundle` (`Configurations/HalloweenConditionalLoad.asset`) |
| Scene config | `1e0df410f70e7642d960762107e0da3a.bundle` (`_GameSceneConfigs/RuinedTemple.asset`): 30 contents, index 29 guid `9567518dfed654149a3629e2612cabd8` the only conditional one |
| Head table | `0a67b27bd1b0643b072c707251d5f8be.bundle` (`Configurations/HalloweenHeadSkinOverride.asset`) |
| Ghost graph | `02b9106cc3e735ca715d7ae0de995003.bundle` (`VisualScripts/WGODataScripts/npc_ghost.asset`); prefab `8dc8830bc7ac19c4dc5ae4f94dad62e8.bundle` |
| Ghost triggers | `resources.assets`, `GameBalance` → `gameLogicsDefs` rows `halloween_npc_ghost_start_action`, `halloween_npc_ghost_go_home` |
| VFX | `WorldFxs/Halloween/`: `16a25be3…` (bats right), `5908684b…` (bats left), `5dc17795…` (bats circling), `dd48c926…` (glowing eyes) |

Reproduce with `tools/game-bundles.py` (UnityPy; reads the game folder from `Directory.Build.props`):

```
python tools/game-bundles.py words out.tsv alloween christmas xmas
python tools/game-bundles.py sceneconfig 1e0df410f70e7642d960762107e0da3a.bundle
python tools/game-bundles.py part 1f3ae3be25acf834e216442936db9f9d.bundle
python tools/game-bundles.py graph 02b9106cc3e735ca715d7ae0de995003.bundle
python tools/game-bundles.py loose resources.assets halloween       # GameBalance rows (the ghost's triggers)
python tools/game-bundles.py loose resources.assets IsHalloweenEvent
grep -a -c -i -E "halloween|TemporaryEvent|christmas|xmas" "<game>/GraveyardKeeper2_Data/Managed/"*.dll
grep -a -c -i -E "#now#|#today#|monthof|dayof\(|yearof|daysbetween" "<game>/GraveyardKeeper2_Data/resources.assets"
```

On a game update, re-run all of these. A new event shows up as a new `TemporaryEventDates` method, a new
`ConditionalLoadContentPart` subclass, a new `cond=` entry in a scene config, or a new GameBalance row that calls an
`Is*Event()` expression (the `words` scan alone misses GameBalance, which is a loose file, not a bundle).
