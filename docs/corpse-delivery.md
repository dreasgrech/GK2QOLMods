# Corpse delivery: leaving the bodies at the crossroads

Investigation of how Graveyard Keeper 2 delivers corpses, and a design for a setting that makes the donkey leave
them where it appears (the crossroads by the nun) so the keeper has to haul them to the morgue. Game: Steam build
25752683 (1.009 hotfix), investigated 2026-10-09. Implemented as **GK2 Corpse Pickup** (`src/GK2CorpsePickup`, GUID
`dreas.gk2.corpsepickup`) the same day: Option A's pallet redirect plus, on top of it, the walk replaced by the
arrival event (a lighter form of Option B, see the mod's README), switchable; in-game test pending.

Sources: the 1.009 decompile (`Assembly-CSharp`), the game's balance tables (the `GameBalance` object inside
`GraveyardKeeper2_Data/resources.assets`) and the donkey's visual script, which is not C# at all but a FlowCanvas graph
stored in an addressables bundle. The graph is printed in full in [npc_donkey-graph.txt](npc_donkey-graph.txt).

## How the game does it today

**Schedule.** A balance row of type `GameLogicDef` named `donkey` (start type Period, `start_time` 0.55,
`period_time` 2) fires every second day at 0.55 of the day (the game counts day as 0.25–0.75, so early afternoon).
Its condition is `PPar("donkey_ready")`, a player parameter, evaluated as a chance (`LazyExpression.EvaluateChance`:
the value against a 0..1 roll, so 1 means always and 0 never); its action is `WGOFireEvent("npc_donkey", "full_cycle")`,
which sends the event `full_cycle` to the donkey's own script. `GameLogicsSystem` / `GameLogicData` drive it
(`UpdateTimer` adds the period to the exec time, so every two days). Quests flip the parameter (balance quest rows):
`6_intro_guards_burial` sets `donkey_ready` 1 in the intro; `21_base_donkey_start` 0 and `21_base_donkey_complete`
1 around the first meeting; `33_base_complain_go` sets it 0 and `nun_body_drop_ready` 1, and
`55_base_strike_donkey_corpses` sets them back to 1 / 0. A sibling row `nun_body_drop` (same timing, condition
`PPar("nun_body_drop_ready")`) runs the global script `nun_body_drop_roll`: a body simply appears at the GD point
`nun_body_drop_point` next to the nun, with the same notification and count. That is the **donkey strike** stretch of
the story (quests 33 to 55), not the early game: the bodies you saw appear by the nun come from that script.

**The donkey's script** (`npc_donkey`, 178 nodes; WGO id `npc_donkey`, its `attached_script`). On `full_cycle`:

1. `global_lock_on_drop_bodies` set (the nun's "no corpses" answer) → nothing.
2. Quest `33_base_complain_go` in progress → a scripted visit (`move_to_morgue`, no bodies).
3. Otherwise **Body Roll** (`Flow_Body`): skipped when `cur_bodies_count` has reached the morgue's total quality
   (its capacity), else succeeds when `donkey_body_drop_chance` ≥ a 0..99 roll. The chance starts at 100 and moves by
   the constants `donkey_drop_chance_reduce_after_success` 15, `..increase_after_fail` 50, `..increase_after_skip`
   5 (with the nun's "many" answer, `nun_many_body_drop`: 5 / 85 / 15 and `donkey_drop_additional` = 1 extra body).
4. On success: puff FX, a dummy `body_0_1` item is put in the donkey's inventory (the body you see on it), the donkey
   is teleported to GD point `donkey_01`, walks the GD graph to `donkey_05` and fires the quest trigger
   `DonkeyStart` (41). Arrival fires the WGO event `full_cycle_back`.
5. `full_cycle_back`: the dummy body is removed, **Get Corpse Drop Amount** = min(1 + extra, max(1, capacity −
   `cur_bodies_count`)); if the WGO `morgue_entrance_door` exists its animator plays `action` and a puff appears at
   `morgue_entrance_fx_spawn`. Then, per body: **Generate Body** (a random `BodyDef` of the player's `bodies_tier`,
   `BodyDef.GenerateItem` fills parts, pockets, burial reward and zombie skin) → **Try To Add Corpse To Pallet**
   (`WgoData.Inventory.AddItemToInventory` on any WGO in group `morgue_pallets`) → yes: **Show New Body
   Notification** → `cur_bodies_count` + 1. No pallet space: **Drop Body** at `RT_morgue_body_spawn` (inside the
   morgue) and the same notification and count. No morgue door at all: drop at `RT_body_spawn` (beside the donkey at
   the morgue entrance).
6. Then the bell (`donkey_bell`), and the quest trigger `DonkeyMorgue` (42). The donkey stays at the morgue,
   interactable, when `21_base_donkey_nun` is Awaiting or one of `137_base_resurrection_donkey`,
   `165_base_donkey1_speak`, `166_base_donkey2_speak`, `167_base_donkey3_speak` is InProgress (scripted talks that
   also walk the player to `intro_graveyard_player_01`); otherwise it walks back to `donkey_01` and on arrival the
   event `Disable` teleports it to `RT_spot`, off-map.

So the bodies are **generated at the morgue**, not at the crossroads. What spawns at the crossroads is the donkey
with one dummy body. The notification and the body count are graph nodes that follow the pallet step.

**The notification.** `Flow_ShowNewBodyNotification` → `UINotificator.ShowNewBodyNotification("body_0_1")` → a pooled
`UINewBodyNotification` whose only serialized field is the icon (`ItemDef.iconId` of the body item, fallback
`i_body`). Its text is the prefab's localized label `ui_notification_new_body` = **"New Body"** (read from the
language tables in resources.assets). The same node is used by `nun_body_drop_roll`.

**Bodies on the ground.** A dropped `body_corpse` gets `DropData.TryStartAutoDestroyTimer` =
`corpse_auto_destroy_timer` **4500**, ticked by `DropSystem.CustomUpdate` on a 0.2 s scheduled update
(`UpdateManager` accumulates `Time.deltaTime` × the game's speed multiplier and passes 0.2 per tick, so real seconds at
normal speed) and paused while the drop is on screen (`CanNotBeAutoDestroyedReason.WhenVisibleOnScreen`). About 75
minutes of off-screen play. On expiry the body becomes a `body_drop_grave_object` WGO on the ground under it (only if
its scene is loaded; otherwise it just vanishes) and `cur_bodies_count` − 1. Items with
`NeverAutoDestroyDropSerializedItemProperty` never rot; the graph's quest path `add_body` generates its body with that
property, so a "doesn't rot" option has a vanilla precedent. A body on the ground is picked up with the interaction key
(`ZombieDropInteractionHandler` → `PlayerData.AddOverheadItem`) and carried overhead to a pallet. `body_corpse` has
`stack_count` 1, so neither queued drops (`GameSceneData.AddDropToQueue`) nor nearby-drop merging can ever fold two
bodies into one. Saddle Up's cart takes it too (`Cargo.Allowed` only filters zombies), so bodies can be hauled by cart.

**Pallets.** `pallet_corpse_1` / `pallet_corpse_2` are in WGO group `morgue_pallets`, accept item group `corpse`,
hold one body each and carry quality 1 (decoded from the binary balance by field position, so treat the numbers as
likely rather than certain). The morgue's capacity is the sum of the qualities of the zone's quality WGOs.

**The dummy body.** Nothing redraws an NPC from its inventory on change; `WgoPart.UpdateDropViewFromInventory` shows
the first inventory item's definition in the part's drop view slot when the view is created (and conveyor code calls
it explicitly). The donkey's view is created after the body is added, so that is most likely what you see on it;
whether it disappears at the morgue before the view is rebuilt is a runtime check.

**Counting.** `cur_bodies_count` counts bodies anywhere: drops, pallets and tables, the keeper's shoulders
(`SaveCodeFixes_Helper.RecalculateCorpseCount`). Bodies lying at the crossroads therefore count against the morgue's
capacity, and deliveries fall to one per visit until the keeper hauls them. Vanilla-consistent, but worth knowing.

## Geography (scene `RuinedTemple`, all at y 3.6)

| GD point | x, z | What |
|---|---|---|
| `donkey_01` | 77.6, 14.65 | Where the donkey appears (puff) and later leaves from. |
| `donkey_01_01` … `donkey_04` | 74.6 … 62.5, 15.6 | Its road west. |
| `donkey_05` | 24.96, 15.6 | The morgue entrance, where bodies are made. |
| `RT_body_spawn` | 24.96, 16.5 | Fallback drop when there is no morgue door (beside the donkey). |
| `RT_morgue_body_spawn` | −74.9, −385.2 (y 0) | Fallback drop inside the morgue when the pallets are full. |
| `morgue_entrance_fx_spawn` | 22.7, 20.9 | Puff at the door. |
| `tp_point_tp_RT_morgue_enter` | 24.0, 20.25 | The morgue door. |
| `RT_crossroad_nun_spot` | 73.8, 12.9 | The nun. |
| `crossroad_nun_corpses` | 74.3, 13.3 | A cutscene spot beside the nun named for corpses. |
| `nun_body_drop_point` | 74.3, 10.2 | Where the early-game nun script drops a body. |
| `RT_spot` | −199.7, 0 (y 0) | Off-map parking for the hidden donkey. |

The crossroads and the morgue entrance are ~53 units apart along one road, in one game scene.

## Where to hook

`Flow_TryToAddCorpseToPallet` is used by exactly three graphs in the whole game: `npc_donkey`, and the dev graphs
`Test 1` and `test_donkey` (a scan of all 25,265 bundles for the type name). A Harmony prefix on its private
`TryToAddCorpseToPallet(Flow)` is therefore effectively donkey-only. `Flow_ShowNewBodyNotification` is shared with
`nun_body_drop_roll`, so any notification change must be gated by a flag the pallet prefix sets, not applied blindly.
Relevant FlowCanvas facts (from `FlowCanvas.dll`): `ValueInput<T>.value` runs the bound getter on every read, so the
vanilla `Any(...)` lambda generates a fresh body per pallet attempt; `FlowOutput.Call(Flow)` is public.

## Design

**Option A, recommended for v1: redirect at the pallet.** Prefix on `Flow_TryToAddCorpseToPallet.TryToAddCorpseToPallet`
(patched by name; `___item`, `___yes` injected; the plugin references `FlowCanvas.dll`). When the setting is on:

1. Read `___item.value` **once**; that is the body (one instance, with its inventory).
2. Find the drop point `worldData.gdPointsData.GetGDPointDataById(DropPoint)`; if missing, or anything throws, return
   `true` and let the original run unchanged (the unused instance never enters the world).
3. `MainGame.Instance.dropSystem.DropItem(body, point.GameSceneDataId, point.Position)`, optionally after adding
   `NeverAutoDestroyDropSerializedItemProperty` (setting). The drop system handles loaded vs. unloaded scenes and
   separates overlapping big drops.
4. Set a "redirected" flag, call `___yes.Call(flow)`, return `false`. The graph then shows the notification and adds
   the count exactly as vanilla; the morgue door still animates, the bell still rings, `DonkeyStart` / `DonkeyMorgue`
   and the quest talks are untouched.
5. Prefix on `UINotificator.ShowNewBodyNotification(string)`: when the flag is set, clear it and show
   `ShowSimpleTextWithIconNotification(text, iconId)` instead, where the text is a literal English string (`LLBase.L`
   returns an unknown key verbatim, no error) and the icon is the body item's `iconId` (that path has no `i_body`
   fallback, so resolve it ourselves). Return `false`.

Cost: the donkey still walks a dummy body to the morgue and the bodies appear behind it at the crossroads. The
notification text can carry that ("The donkey left a body at the crossroads").

**Option B: intercept at the start.** Prefix on `Flow_GoTo.GoTo` when `fireEventOnFinish` is `full_cycle_back` (that
string is unique to the cycle): generate the bodies ourselves (re-implementing Get Corpse Drop Amount), drop them at
the crossroads, show notifications, add the count, remove the dummy body, fire the WGO event `Disable` and skip the
walk. Visually right (the donkey appears, drops, puffs away) but it must fall back to vanilla whenever
`21_base_donkey_nun` is Awaiting or `33_base_complain_go`, `137_base_resurrection_donkey`, `165/166/167_base_donkey*_speak`
is InProgress, it skips `DonkeyStart` / `DonkeyMorgue` (quest triggers), it duplicates the game's amount logic, and it
has far more that can go wrong. Not for v1.

**Option C: use the game's own strike mode.** The parameters above already give a "bodies appear by the nun, with
notification and count" mode: `nun_body_drop_ready` 1 and `donkey_ready` 0 run `nun_body_drop_roll` instead of the
donkey. A mod could just set them. Costs: one body per visit (that script has no Get Corpse Drop Amount and no "many"
extra), no donkey at all, `DonkeyStart` / `DonkeyMorgue` never fire so the five waiting quests stall, the stock "New
Body" text, quests 21 and 55 flip the parameters back so the mod would have to keep re-asserting them, and the
parameters live in the save: uninstalling the mod leaves the donkey switched off unless the mod restored them first.
That last point breaks the repo's "nothing in the save" rule. Listed so it can be rejected on evidence. (The built mod's
unload timing uses the game's own delayed WGO events, which are saved too, but as transient events of the game's own
kind that resolve without the mod; a persistent parameter flip is a different matter.)

**Two guards for the "never lose items" rule.** `DropSystem.DropItem` keeps the instance only when `Count == 1`; its
`Count > 1` path builds fresh items by id and would discard a body's parts, pockets and burial reward. Bodies are
always 1, but the prefix checks it and falls back to vanilla otherwise. And `UINotificator.ShowNewBodyNotification`
has a parameterless overload that forwards to the string one, so the patch names the `string` overload explicitly.

**Settings (BepInEx cfg + Mods menu).**

| Key | Default | Meaning |
|---|---|---|
| `Enabled` | true | The donkey leaves its bodies at the crossroads instead of putting them on the morgue pallets. |
| `DropPoint` | `nun_body_drop_point` | GD point id to drop at; `donkey_01` and `crossroad_nun_corpses` are the alternatives. |
| `BodiesNeverRot` | false | Add the game's never-auto-destroy property, so a body left there doesn't turn into a grave mound after the 4500 s off-screen timer. |
| `NotificationText` | "The donkey left a body at the crossroads" | Shown instead of the stock new-body notification. |

**Safety.** Only the game's own drop system and item instance are used; nothing new in the save. A body on the ground
is exactly what vanilla produces when the pallets are full. If the prefix fails before `yes` is called it steps aside
and vanilla runs. The patch targets are private methods patched by name; `tests/FrameworkScan`-style checks and the
usual game-MVID guard apply. New plugin in this repo (one feature per plugin), GUID `dreas.gk2.<name>`.

**Runtime-only checks before building.** Whether the donkey view renders the dummy body during the walk and whether it
is gone before the donkey turns back (decides how much Option A's visual mismatch matters); the exact spot the drop
system lands a body at `nun_body_drop_point` (the road edge vs. the nun's feet).

## Second pass (same day)

Every claim above was re-read against the source rather than notes. What changed: the nun's body drops belong to the
donkey-strike quest line (33 → 55), not the early game; the parameter history above; the stock notification text
("New Body"); the rot clock is real seconds × game speed, now verified; pallets hold one body each; the dummy body's
likely rendering path; the cart accepts bodies. Confirmed unchanged: the graph wiring (printed appendix), the
constants (4500 / 15 / 50 / 5 / +1 extra, nun-many 5 / 85 / 15), the GD point positions (`RuinedTemple` baked data,
scene offset 0), `Flow_TryToAddCorpseToPallet` used only by `npc_donkey` and two dev graphs (no graph in
resources.assets or level0 either), `ValueInput<T>.value` re-evaluating per read, `LLBase.L` returning unknown keys
verbatim, body stack size 1, `Flow_QuestStatusEquals` = `questSystemData.IsQuestInStatus`.

**Hand test.** With the setting on, wait for the delivery (every second day at 0.55; or trigger the
`donkey` game logic early by the dev console if available): the donkey walks as before, the notification reads our
text, one body per delivered corpse lies at the drop point, `cur_bodies_count` rises by that many (the morgue zone
widget), the body can be picked up and put on a pallet, and a save/load in between keeps it. Then the same with the
nun's "many" answer (two bodies, both separated), with the setting off (vanilla), and once with the morgue pallets full.

## Files

- [npc_donkey-graph.txt](npc_donkey-graph.txt): the printed `npc_donkey` and `nun_body_drop_roll` graphs.
- Decompile nodes read: `Flow_Body`, `Flow_GetCorpseDropAmount`, `Flow_GenerateBody`, `Flow_TryToAddCorpseToPallet`,
  `Flow_ShowNewBodyNotification`, `Flow_DropItem`, `Flow_GoTo`, `Flow_FireEventTrigger`, `Flow_FireEventOnWgoData`,
  `Flow_AddItem`, `Flow_RemoveItemsFromWgoData`; `UINotificator`, `UINewBodyNotification`,
  `UISimpleTextWithIconNotification`, `DropSystem`, `DropData`, `DropView`, `GameSceneData`, `BodyDef`,
  `GameLogicDef`, `GameLogicData`, `GameLogicsSystem`, `GlobalEventsSystem`, `WgoDataScriptsManager`,
  `BigDropInteractionHandler`, `ZombieDropInteractionHandler`, `SaveCodeFixes_Helper.RecalculateCorpseCount`.
- Extraction: UnityPy 1.25.4 against the `aa/StandaloneWindows64` bundles (Unity 6000.3.9f1 given as the fallback
  version) and `resources.assets` for the balance object (path id 2455) and the `lng_*` language tables. The scripts
  (bundle scan, graph print, subgraph trace, balance byte contexts, locale lookup) are kept in
  `C:\Users\User\.claude\jobs\5184282a\tmp\graphtools\` with a README, for the re-audit after the next game update.
