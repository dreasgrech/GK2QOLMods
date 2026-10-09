# GK2 Corpse Pickup

BepInEx plugin for Graveyard Keeper 2: the donkey leaves the bodies it brings **at the crossroads by the nun**, where
it appears, instead of putting them on the morgue pallets. You carry them to the morgue yourself, one on your
shoulders, or several on a cart (GK2 Saddle Up!'s cart takes them).

The donkey still comes as before, every second day in the early afternoon. It appears behind the tree where the game
puts it, walks out onto the road beside the nun, the body on it goes and the bodies appear by the nun, and it walks
back behind the tree and vanishes the way it came; no walk to the morgue with nothing to deliver
there (a setting brings the walk back). A notification tells you each time: "The donkey left a body at the
crossroads", with the body's icon.

Version 1.0.0, built and audited against Steam game build 25752683, game 1.009 (Assembly-CSharp MVID `44edf1a9-…`,
LazyBearTechnology `02955186-…`). Investigation and design: [`docs/corpse-delivery.md`](../../docs/corpse-delivery.md).

- GUID: `dreas.gk2.corpsepickup`
- Needs: BepInEx 5.4.23.x, installed in the game folder.

## Using it

- Play as usual. When the donkey delivers, the bodies lie by the nun at the crossroads (east of your morgue, along
  the road). Pick one up with the interaction key and carry it to a morgue pallet, or load it on a cart.
- Bodies on the ground count toward your morgue's capacity exactly as bodies on pallets do (the game's own rule), so
  the donkey brings fewer while they lie there, and none once they fill the morgue's capacity. Haul them and deliveries
  go back to normal.
- A body left lying out of sight for a long time (about 75 minutes of play) turns into a grave mound; that is the
  game's rule for any body on the ground. The setting "Bodies never rot" switches it off for the bodies the donkey
  leaves.
- The nun's "how many corpses" answers (none, a few, lots) work as before.

## Optional: GK2 Mod Framework

With [GK2 Mod Framework](https://www.nexusmods.com/graveyardkeeper2/mods/42) (0.1.x, by SuperMan4eg) installed,
the mod appears in the game's **Mods** menu (main menu and ESC menu):
- a Status row: Active, Off, or disabled after a game update;
- "Leave bodies at the crossroads", "Where they are left", "Bodies never rot" and "Notification text".

The Debug keys and `Safety.DisableOnGameUpdate` stay in the cfg; a read-only row shows the last self-test result. Without the framework, the same DLL works
exactly as before. Details: [`docs/gk2-mod-framework.md`](../../docs/gk2-mod-framework.md).

## Settings (`BepInEx\config\dreas.gk2.corpsepickup.cfg`)

Read at game start; the General ones are read at the moment a body is delivered, so a change in the Mods menu applies
to the next delivery.

| Key | Default | Meaning |
|---|---|---|
| `General.Enabled` | true | Leave the bodies at the crossroads. Off: the game's own delivery to the pallets. |
| `General.UnloadAtCrossroads` | true | The donkey unloads where it appears and vanishes. Off: it walks to the morgue and back as in the game, and the bodies appear at the crossroads when it arrives. |
| `General.StandPoint` | `donkey_01_01` | Where the donkey walks to and unloads: the road beside the nun, `crossroad_nun_corpses` (beside her, off the road), `donkey_02` (further west), or `donkey_01` (the game's spawn point itself: it unloads on the spot). |
| `General.DropPoint` | `nun_body_drop_point` | Where: the game's own spot by the nun, `crossroad_nun_corpses` (right beside her) or `donkey_01` (where the donkey appears). |
| `General.BodiesNeverRot` | false | Bodies the donkey leaves never turn into a grave mound. |
| `Timing.UnloadDelay` | 1.5 | Only when it unloads on the spot (`StandPoint = donkey_01`): seconds before the bodies appear. |
| `Timing.LeaveDelay` | 1.0 | Only when it unloads on the spot: seconds before it vanishes. |
| `General.NotificationText` | The donkey left a body at the crossroads | Shown instead of the game's "New Body", with the body's icon. Empty: the game's own. |
| `Debug.TestDeliveryKey` | None | Starts a delivery now (the same event the game's schedule sends; the game's roll still decides whether the donkey comes). Ignored while the donkey is already out. |
| `Debug.SelfTestKey` | None | Runs the self-tests (below) in the loaded game. |
| `Debug.SelfTestLeaveBodies` | false | The self-tests leave their bodies lying instead of removing them. |
| `Safety.DisableOnGameUpdate` | true | Do nothing if the game's code changed since the audit. |

## How it works

The delivery is not C# but the donkey's visual script (a FlowCanvas graph, `npc_donkey`). It walks the donkey from
the crossroads to the morgue entrance and there, per body: "Generate Body" → "Try To Add Corpse To Pallet" →
"Show New Body Notification" → body count + 1. The bodies are made at the morgue; the donkey carries a dummy.

- **Prefix on the pallet node's handler** (`Flow_TryToAddCorpseToPallet.TryToAddCorpseToPallet`): reads the
  generated body once, drops it with the game's own drop system at the chosen GD point, then calls the node's "yes"
  output and skips the pallet code. The graph goes on exactly as after a pallet delivery: notification, count, the
  bell, the quest triggers. If anything is off (no such point, an odd item, an exception) nothing is touched and the
  game's own code runs.
- **Prefix on the graph's walk node** (`Flow_GoTo.GoTo`), only for the donkey and only for its two walks, told
  apart by the event each fires on arrival. The delivery walk (`full_cycle_back`, start → morgue) is started by us
  instead with the node's own movement call, same navigation, speed and arrival event, but to the stand point a few
  units out on the road; on arrival the game's movement fires the event and the graph unloads there (its dummy body
  is removed, the bodies made, our pallet prefix drops them). The walk back (`Disable`) is then the game's own, back
  to the spawn point, where it vanishes with its puff as usual. Only when the stand point is the spawn point itself
  (or no walk is possible) is the arrival event fired after a delay instead, and then the walk back, being a walk to
  where it already stands, which the movement code ends without firing the event, is given its `Disable` by us after
  a beat. The morgue door still plays its animation far away; harmless. While one of the five quests that keep the donkey at the morgue for a talk is waiting
  (`21_base_donkey_nun`, `137_base_resurrection_donkey`, `165/166/167_base_donkey*_speak`), the walk is the game's.
  Not fired for the skipped walk: the game's generic `WgoGoToFinished` trigger for the donkey (nothing found listening
  for it; the donkey's own triggers DonkeyStart and DonkeyMorgue still fire in their usual order).
- **Prefix on the graph's own drop node** (`Flow_DropItem.Drop`), for the two fallbacks of the arrival chain that
  bypass the pallet node: no `morgue_entrance_door` in the save yet (the game drops the body beside the morgue
  entrance) and every pallet full (inside the morgue). Both go to the crossroads too, only for the donkey's graph,
  only those two points, only while it is unloading there.
- **Prefix on `UINotificator.ShowNewBodyNotification(string)`**: for a body this mod just redirected (same frame),
  shows a text-and-icon notification with your text instead; otherwise untouched (the game uses the same node when
  the nun drops bodies during the donkey's strike).
- The pallet node is used by the donkey's graph and two developer test graphs only (every game bundle scanned).

## Safety

- The body is the game's own generated instance, dropped by the game's own drop system; nothing of the mod's own in
  the save. A body on the ground is exactly what the game produces itself when the pallets are full. Saves made with
  the mod load without it.
- One qualification: the unload and the leave are timed with the game's own delayed WGO events, which the game
  saves. A save taken inside those 1.5 + 1 seconds holds one such event on the donkey, of the game's own kind. Loaded
  without the mod it still resolves: `full_cycle_back` runs the game's arrival (the bodies go to the pallets as in
  the unmodified game), `Disable` makes the donkey vanish; after a `full_cycle_back` without the mod the donkey
  stands at the crossroads until its next delivery, which starts with a teleport anyway. No item is ever lost.
- The prefix only takes a body with count 1 (bodies always are): the drop system's multi-count path rebuilds items
  by id and would lose a body's parts and pockets.
- Bodies have stack size 1, so no drop path can merge two into one.
- Errors are caught and logged once; the delivery then runs as vanilla.
- No per-frame work (the debug key check only, when a key is set).
- MVID guard and all-or-nothing patching as in the other mods.

## Self-tests (`Debug.SelfTestKey`)

Run in the loaded game, against the real save, in about three seconds; results in the log as `[test]` lines and in
the Mods menu's Self-test row. They call the mod's drop step directly (not the donkey's graph), so the game's body
count is never changed, and every test body is removed again at the end unless `Debug.SelfTestLeaveBodies` is on.

| Scenario | Checks |
|---|---|
| drop points | The three choices and `RT_spot` exist, all three by the nun in one scene. |
| one body redirected | A generated body (parts, pockets, reward inside) is taken, lands as a plain item drop at the point, the same instance with its inside intact, the game's rot timer started, one notification pending for it, a view appears. |
| notification swapped once | Right after a redirect the game's "New Body" call shows our text-and-icon notification and not the stock one; a frame later the stock one shows as before (the nun's script path); the icon resolves. |
| many bodies at once | Twelve bodies in one frame, as the graph's loop delivers them: twelve drops of count 1, twelve notifications pending, after settling twelve views, no two on one spot, all within 8 of the point at ground level, none merged or lost. |
| never rot | With the setting on the body carries the game's never-auto-destroy property and the drop is flagged; off, it does not. |
| unknown point steps aside | An unknown drop point: the step returns false, nothing dropped, nothing pending, the unused body is nowhere. |
| walk decisions | The walk rule with made-up positions: unload here, the game's walk while a quest talk waits, leave from here when already at the start, the game's walk back from the morgue, every other walk untouched; the donkey WGO is found; which quest, if any, holds it in this save. |
| cleanup | No test body left in the world (or all left, by setting); `cur_bodies_count` unchanged. |

Not covered by them, by design: the donkey's own walk and the graph calling our step. That is the delivery test
below (`Debug.TestDeliveryKey`).

## In-game test list

1. Log shows `GK2 Corpse Pickup 1.0.0 loaded.` **once**, no warnings or errors.
2. Set `Debug.TestDeliveryKey` (e.g. `F7`; F8 and F12 are Steam's) and press it once near the crossroads: the donkey
   appears behind the tree with a body, walks out onto the road by the nun, the body on it goes, the bodies appear at
   the nun's spot with the bell, and it walks back behind the tree and vanishes with a puff. The log has `walks out
   to donkey_01_01`, one `left at nun_body_drop_point` line per body, and `walks back from`. If the donkey doesn't come, the game's roll
   skipped it; press again (the key is ignored while the donkey is out).
2z. A save without the morgue door built yet: the same, through the graph's fallback (log: `fallback drop at
   RT_body_spawn ... went to the crossroads`). The unload line says `morgue door present/absent`.
2a. Once with the morgue in view and once facing east with it off screen: the same result both times (the arrival
   chain pokes the morgue door's animator from afar; the game guards a missing view, but see it once).
2c. Quick-save while the donkey walks out, reload: it finishes the walk (or stands), the bodies appear, it leaves. Expected cosmetic detail: the
   dummy body may still show on the donkey for that last second beside the real ones (the game only redraws it when
   the view is rebuilt).
2b. `UnloadAtCrossroads = false`: the donkey walks to the morgue, the door animates and the bell rings there, and the
   bodies appear at the nun's spot when it arrives; it walks back and vanishes.
3. The notification reads "The donkey left a body at the crossroads" with the body icon, one per body.
4. The morgue zone widget's body count went up by the number of bodies left.
5. Pick a body up (Take), carry it into the morgue and put it on a pallet. Load one on the Saddle Up cart.
6. Save and load with a body lying there: still there, still takeable.
7. With the nun's answer "Lots of corpses": two bodies, lying apart, two notifications.
8. Pallets full: the bodies still go to the crossroads (vanilla would drop them inside the morgue).
9. `Enabled = false`: the game's own delivery (pallets, "New Body").
10. `DropPoint = donkey_01` and `crossroad_nun_corpses`: the bodies land there.
11. `BodiesNeverRot = true`: a body left out of sight for well over an hour is still there (or: the log line says
    "never rots").
12. Self-tests (`Debug.SelfTestKey`): all pass, no WARN (a WARN about views means you stood too far from the
    crossroads; stand near the nun and run again). Then once with `SelfTestLeaveBodies = true`: twelve bodies lie
    there; carry and cart a few.
13. With GK2 Mod Framework installed: Status Active, the four settings; untick "Leave bodies at the crossroads" and
    Status reads Off. With it removed: a clean log.

## Verification done

- `dotnet build -c Release`: 0 errors; no warnings from this code (only the MSB3277 about the game's newer
  `System.IO.Compression`, which every mod here gets and doesn't use).
- `tests/CecilScan-CorpsePickup` (74 IL references into the game assemblies resolve; patch targets, ports and the
  movement-code premise present) and `tests/FrameworkScan GK2CorpsePickup`: ALL PASS.
- Round 1 in game (2026-10-09/10, game 1.009.1): loads clean. Self-tests 48 passed, 0 failed, 0 warnings. Findings:
  the first delivery run reached the morgue arrival but no body went through the pallet step, because the save has
  no `morgue_entrance_door` yet and the graph took its own fallback drop beside the morgue entrance; fixed by
  redirecting the graph's two fallback drops as well. Second run with that build: the donkey appeared by the nun,
  unloaded after 1.5 s (`fallback drop at RT_body_spawn (no morgue door yet) went to the crossroads instead`), the
  notification showed, the body lay by the nun, the donkey vanished. One F7 before that was skipped by the game
  itself: one pallet, two bodies counted, "morgue is full"; vanilla rule.
- Round 2 in game (2026-10-10): the user found the game's spawn point hidden behind a tree, so the delivery walk now
  goes to the road point beside the nun instead of being skipped. Seen and confirmed: the donkey walks out from
  behind the tree with its body, unloads by the nun (`walks out to donkey_01_01 ... unloads there`, `fallback drop at
  RT_body_spawn ... went to the crossroads`, `left at nun_body_drop_point`), walks back and vanishes.

## Build, install, uninstall

- **Build and deploy** (game closed): `dotnet build -c Release` in this folder.
- **Build only** (game running): `dotnet build -c Release -p:Deploy=false`.
- **Uninstall:** delete `BepInEx\plugins\GK2CorpsePickup.dll` in the game folder, and optionally
  `BepInEx\config\dreas.gk2.corpsepickup.cfg`. Bodies already lying at the crossroads stay there and can still be
  picked up; the next delivery goes to the pallets.
