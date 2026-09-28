# GK2 Mounts

BepInEx plugin for Graveyard Keeper 2: press a key and the keeper rides a donkey. You walk 1.75× as fast while
mounted (configurable), the donkey follows your every step, faces the way you walk and animates when you do. Press
the key again to get off. Two looks: **Ride** (on the donkey's back) and **Cart** (the donkey pulls its cart and you
stand in it).

Version 0.1.0 (first playable build), built and audited against game build 25533739 (Assembly-CSharp MVID `97f92f0d-…`).
Design notes: [`FEASIBILITY.md`](FEASIBILITY.md); runtime facts the design rests on: [`PROBE-FINDINGS.md`](PROBE-FINDINGS.md).

The game has no mount of its own. The donkey you ride is the corpse-delivery donkey's *looks* only; the real donkey
NPC and its schedule are untouched.

## Settings (`BepInEx\config\anvil.gk2.mounts.cfg`, read at game start)

| Key | Default | Meaning |
|---|---|---|
| `General.Enabled` | true | Master switch. |
| `General.MountKey` | `H` | Mount / dismount. Pick a key the game does not use. |
| `General.Style` | `Ride` | `Ride` or `Cart`. Applies on the next mount. |
| `General.SpeedMultiplier` | 1.75 | Walking speed while mounted, times normal. Measured to scale exactly linearly. |
| `General.MountWgoId` | `npc_donkey` | Which world object's looks to use. Only the donkey is tested. |
| `General.StayMountedThroughDoors` | true | After a scene change the donkey is re-summoned and you keep riding. |
| `General.SideViewOnly` | false | The donkey only has left/right art, so while mounted the keeper also faces only left or right; walking up or down keeps the last horizontal facing. Off: the keeper turns as usual and the donkey shows its side.  Forced on while the pose is `FishingIdle`, which has no up/down frames. |
| `General.ShowMessages` | true | Thought bubble when you cannot mount ("I'm busy.", "Not in a co-op game."). |
| `Ride.Pose` | `FishingIdle` | Keeper's body pose while riding. `FishingIdle` (default): hands forward as if holding reins, legs still, no rod. `Idle`: standing still. `FocusedWalk`: the combat-focus walk. `Walk`: unchanged (arms swing). Tool, work and planting states are not offered: they take player control and force Idle on exit, which would loop. |
| `Ride.RiderOffsetPx` | -4 | Where the keeper sits along the donkey, in art pixels: positive = closer to the head, negative = closer to the rear. |
| `Ride.TorsoOffsetPx` | -3 | Shifts only the upper body along the donkey (positive = towards the head); the legs stay put. |
| `Ride.AutoSeat` | false | Seat height computed from the sprites: the rider's hip lands on the donkey's back line, so the real legs hide inside the donkey's body. Off: use `Ride.SeatOffset`. |
| `Ride.SeatAdjustPx` | -6 | Fine adjustment of the automatic seat in art pixels (positive = higher). Default from in-game tuning. |
| `Ride.NearLeg` | false | Draws the rider's legs in front of the donkey while his real (baked-in) legs are hidden inside it. |
| `Ride.LegSource` | `Body` | `Body`: the keeper's own trousers and boots, re-sliced from his current body frame, so they are outfit-exact and follow the pose. `Drawn`: a hand-drawn bent leg in outfit colours. |
| `Ride.LegHeightPx` | 15 | With `Body`: how many bottom rows of the body frame are legs (feet to belt). |
| `Ride.LegAdjustXPx` / `LegAdjustYPx` | 3 / 1 | Fine adjustment of the legs, in art pixels: X relative to the torso (positive = towards the rear), Y relative to the donkey's back line (positive = higher). |
| `Ride.SeatOffset` | 0.32 | Torso height when `AutoSeat` is off (0 = standing on the ground). |
| `Ride.Bob` | true | Move the rider up and down with the donkey's back through its walk cycle (1 px). |
| `Ride.FreezePoseWhenIdle` | true | Pause the keeper's animation while standing still on the donkey so the arms stop moving; resumes when moving. |
| `Debug.TuningKeys` | true | While mounted: PageUp/PageDown seat, Home/End leg sideways, Insert/Delete leg up/down, one pixel per press, values logged. Turn off for normal play. |
| `Debug.TuningWindow` | true | While mounted, a panel in the top-right corner with sliders for speed, depth, centering, fore/aft, seat, pose, legs and side view. Changes apply live; "Save to cfg" writes them to the config file. `Debug.TuningWindowScale` (1.5) sets its size. Turn off for normal play. |
| `Ride.Depth` | 0.32 | Donkey shift towards (negative) or away from the camera. |
| `Ride.CenterOffset` | 0.48 | Above 0: keep the donkey centred under the rider (the sprite's built-in sideways offset is measured and cancelled every frame). 0: leave the donkey where the game draws it. Shown as a toggle in the panel. |
| `Cart.SeatOffset` / `Cart.Depth` / `Cart.CenterOffset` | 0 / 0 / 0 | Same three for cart style. With centering 0 the donkey walks ahead and the keeper stands in the cart. |
| `Safety.DisableOnGameUpdate` | true | Do nothing if the game's code changed since the audit. |

## How it works

- **Speed**: `PlayerPhysicalBody.SpeedMultiplier` is a public property the game itself uses (the bow and sword states
  set it to 0.5 while aiming). The mod sets it to the configured value while mounted and to 1 on dismount. Movement
  is force-based with linear damping, and the probe measured top speed scaling exactly with the multiplier.
- **The donkey** is a temporary world object created the same way as the game's build preview:
  `new WgoData("npc_donkey", …) { isTempObject = true }` spawned with `Wgo.Spawn`. Temp objects are never added
  to `WorldData` or to any zone list, so they never reach the save file. Its colliders are disabled (as the game does
  for the carried fight flag and for the preview), and the cart parts are shown or hidden by name per style.
- **Following**: every frame the donkey's `Position` is the keeper's position plus the depth and centering offsets,
  its `MovableDirection` is the keeper's facing, and its animator state mirrors the keeper's walk/idle. The prefab's
  body sprite is drawn 0.48 units to its facing side, which the centering cancels.
- **Rider**: the keeper's sprites hang under one transform (the one carrying the Animator); its local height is
  shifted by `SeatOffset` while mounted and restored on dismount.
- **Side view** (second Harmony patch): the donkey's own clips are only `static/walk/graze/ear/lips` left and right,
  and its rotation preset knows only Left and Right; there is no up/down donkey art. Every path that sets the
  keeper's sprite facing ends in `AnimationComponentBase.SetDirection(Vector2)`, so a prefix limited to the
  keeper's `PlayerAnimation` turns any facing with a horizontal component into pure left/right and keeps the last
  horizontal facing for vertical movement. The donkey is given the same facing. Side effect: the game places the
  interaction box on the side the keeper faces, so while riding up or down you interact with things beside you,
  not above or below. Dismount (or set `SideViewOnly = false`) for the normal behaviour.
- **Attached script suppression** (the one Harmony patch): `WgoData`'s constructor starts the definition's attached
  flow script, and the donkey's script would teleport it to its delivery point and walk its route. A prefix on
  `WgoDataScriptsManager.CreateScript` skips creation only while the mod is constructing its own `WgoData`.
- **Dismount rules**: anything that takes the keeper out of free walking dismounts automatically: work, ladders,
  building, planting, fishing, drawing a weapon, sleep, death, cutscenes, co-op. Opening chests or the inventory does
  not, and doors do not (with `StayMountedThroughDoors` the donkey is re-summoned in the new scene).

## Safety

- **No save changes.** The donkey is a temp object outside `WorldData`; the speed multiplier lives on a
  MonoBehaviour; the seat offset on a transform. Nothing new is serialized. Saves made while mounted load without
  the mod exactly as they would otherwise (you are simply on foot).
- **Worst case** if the plugin crashes mid-ride: a donkey view that lingers until the scene unloads and a faster walk
  until the next dismount or restart. Nothing persists.
- The real `npc_donkey` NPC is never touched; the mod only reads its prefab.
- MVID guard, `Access.Verify()` of every game member used, and all-or-nothing patching as in the other mods.
  Refuses in co-op.

## In-game test list

1. Log shows `GK2 Mounts 0.1.0 loaded.`; no errors.
2. Press `H` in the open: donkey appears under you, you sit on it (legs hidden), thought bubble none. Walk all four
   directions: donkey faces left/right with you, walk animation runs, speed clearly higher.
3. Press `H` again: donkey gone, normal speed, keeper at normal height.
4. Open a chest and your inventory while mounted: still mounted afterwards.
5. Walk through a door to another scene: donkey re-appears, still fast. (`StayMountedThroughDoors`)
6. Use a workbench / climb a ladder / draw a weapon while mounted: automatic dismount, normal speed after.
7. Try to mount while working or in build mode: refused with a bubble.
8. Sleep while mounted: dismounted; next morning normal.
9. Set `Style = Cart`, restart, mount: donkey ahead with cart and lantern, keeper standing in the cart.
10. Quit to menu while mounted, load again: on foot, normal speed, no errors in the log.

## Probe (`tools/GK2MountProbe/`)

Development-only plugin used to gather the facts above; it lives outside `src/` with its archived reports and logs.
Not part of `dist/` and not installed. What it dumps and its hotkeys: [`tools/GK2MountProbe/README.md`](../../tools/GK2MountProbe/README.md).
