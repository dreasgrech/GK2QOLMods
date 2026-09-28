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

### `[General]`: behaviour

| Key | Default | Meaning |
|---|---|---|
| `Enabled` | true | Master switch. |
| `MountKey` | `H` | Mount / dismount. Pick a key the game does not use. |
| `CargoKey` | `L` | Cart style, proof of concept: with an item on your head (log, stone, ore, crate, corpse) puts it on the cart; with empty hands takes the last item back (into the hands, or dropped beside you when they are full). The cart holds two items, like the game's own delivery cart. Zombies are refused. Cargo is handed back automatically at every dismount and before every save (it lives in a temporary inventory that no save contains); through doors it stays on the cart. |
| `Style` | `Ride` | `Ride` (on the donkey's back) or `Cart` (the donkey pulls its cart, the keeper stands in it). Each style has its own section below. |
| `MountWgoId` | `npc_donkey` | Which world object's looks to use. Only the donkey is tested. |
| `StayMountedThroughDoors` | true | After a scene change the donkey is re-summoned and you keep riding. Off: dismount at every door. |
| `DismountIndoors` | true | Dismount automatically when entering a building, church or dungeon (the game's indoor lighting presets), and refuse to mount indoors. The cart is a 3D object that would cut through furniture and door frames inside. |
| `ShowMessages` | true | Thought bubble when you cannot mount ("I'm busy.", "Not in a co-op game."). |
| `SideViewOnly` | false | While mounted the keeper only faces left or right (the donkey has no up/down art). Forced on for poses that only have left/right frames (`FishingIdle`). |

### `[Ride]` and `[Cart]`: position, pose, animation, legs, speed

Both sections hold the same keys; the values differ because the two looks differ. The tuning panel edits the
section of the style you are currently mounted on. The defaults come from `defaults.cfg` next to the sources, embedded into the DLL at
build time; the panel's "Export current values as mod defaults" (Debug tab) writes the current values into that
file, so tuning never needs a code edit: tune, export, rebuild. The table shows the values shipped at the time of
writing, Ride / Cart.

| Key | Ride | Cart | Meaning |
|---|---|---|---|
| `SpeedMultiplier` | 1.75 | 1.5 | Walking speed while mounted, times normal (measured to scale exactly linearly). |
| `Depth` | 0.32 | 0 | Donkey towards the camera (negative, drawn in front of the keeper) or away from it (positive, behind). |
| `CenterOffset` | 0.48 | 0 | Above 0: keep the donkey centred under the rider (its sprite's sideways offset is measured and cancelled every frame). 0: leave it where the game draws it, half a body to the side. A toggle in the panel. |
| `RiderOffsetPx` | -4 | 0 | Whole rider along the donkey, in art pixels: positive towards the head, negative towards the rear. |
| `TorsoOffsetPx` | -3 | 0 | Upper body only along the donkey; the legs stay put. |
| `AutoSeat` | false | false | Torso height computed from the sprites (hip on the donkey's back line). Off: `SeatOffset`. |
| `SeatAdjustPx` | -6 | 0 | With `AutoSeat`: fine adjustment in art pixels (positive = higher). |
| `SeatOffset` | 0.32 | 0 | Without `AutoSeat`: torso height in world units (0 = standing on the ground). |
| `Bob` | true | false | Rider moves up and down with the donkey's back through its walk cycle (1 px). |
| `Pose` | `FishingIdle` | `Walk` | Keeper's body pose: `FishingIdle` (hands forward as if holding reins, legs still), `Idle`, `FocusedWalk`, `Walk` (unchanged). Tool, work and planting states are not offered: they take player control and force Idle on exit, which would loop. |
| `FreezePoseWhenIdle` | true | false | Pause the keeper's animation while standing still so the arms stop moving; resumes when moving. |
| `PoseSpeed` | 1 | 1 | Playback speed of the pose animation while the donkey moves (1 = the clip's own speed). |
| `PoseSpeedFollowsDonkey` | true | true | Also multiply the pose speed by the riding speed multiplier, so it keeps pace with the donkey's walk animation. |
| `NearLeg` | false | false | Draw the rider's legs in front of the donkey while his own (baked-in) legs are hidden inside it. |
| `LegSource` | `Body` | `Body` | `Body`: his own trousers and boots re-sliced from the current body frame (outfit-exact, follows the pose). `Drawn`: a hand-drawn bent leg in outfit colours. |
| `LegHeightPx` | 15 | 10 | With `Body`: how many bottom rows of the body frame are legs (feet to belt). |
| `LegAdjustXPx` / `LegAdjustYPx` | 3 / 1 | 0 / 0 | Legs along the donkey relative to the torso (positive = towards the rear) and up/down relative to the back line (positive = higher). |
| `SnapToGround` | true | true | Place the donkey at the ground height under its own position, as the game does for walking objects. Off: it takes the keeper's height and digs into slopes when beside or behind him. |
| `CartFollowsGround` | true | true | Cart style only: the whole cart (wheels, cart body, lantern) is raised or lowered every frame, as one, by the difference between the ground under the cart body and the ground under the donkey, so the rigid 3D cart no longer sinks into a slope behind the donkey. The bars between donkey and cart take half of that shift. Sprites stay upright; nothing rotates. |
| `CartGroundMaxShift` | 0.8 | 0.8 | Largest shift the cart may get, in world units, either way. |
| `CargoAutoFit` | true | true | Cart style only: place each cargo mesh by its measured size: bottom on the bed, footprint centre on its slot (the models' pivots sit 0.02 to 0.19 off centre across), and a second item put on top of the first when the two are wider than the 0.75-wide bed together (two crates, 0.5 each; up to 0.12 of overhang is allowed, so corpse plus log lie side by side). No pushing apart: the slot values rule. Off: the model's pivot goes to the slot point. |
| `CargoDrawCorpse` | true | true | Cart style only: the mod draws every corpse itself with the game's corpse mesh, so corpses follow the same placement rule and sliders as any item, and the game's loaded-cart body stays off. Off: the first corpse is the game's own loaded cart body with the shroud (not movable by the sliders); further items ride on top of it. |
| `Cargo1Along` / `Cargo1Up` / `Cargo1Across` | 0 / 0 / 0 | 0 / 0 / 0 | Cart style only: slot of the first cargo item on the cart bed, world units: along the cart from the bed's centre, height above the measured bed top, across the cart's width (positive = away from the camera). Panel sliders under "Cart cargo", live. A corpse is drawn by the game's own loaded-cart mesh and is not moved by these; everything else is a copy of the keeper's overhead mesh atom and rides on top of the shroud while a corpse is aboard. |
| `Cargo2Along` / `Cargo2Up` / `Cargo2Across` | 0 / 0 / -0.3 | 0 / 0 / -0.3 | Same for the second cargo item. |

### `[Debug]` and `[Safety]`

| Key | Default | Meaning |
|---|---|---|
| `TuningWindow` | true | While mounted, a panel in the top-right corner with tabs General / Riding / Debug. Changes apply live; "Save to config file" writes them; right-click a row to reset it. `TuningWindowScale` (1.5) sets its size. Turn off for normal play. |
| `TuningKeys` | true | PageUp/PageDown torso, Home/End legs sideways, Insert/Delete legs up/down, one art pixel per press, editing the current style's values. Turn off for normal play. |
| (Debug tab buttons) | | "Start a real delivery now" fires the game donkey's own `full_cycle` script event with its body roll forced to pass (a Harmony prefix on `Flow_Body.DoAction`, active for two frames after the press, drop chance untouched), so the real cart can be watched on the same ground as ours. "Dump the game donkey's script" writes the `npc_donkey` flow graph to `BepInEx/GK2-script-npc_donkey.json`; the text field sends any of its events. See `PROBE-FINDINGS.md` for the routine. "Dump carry facts" writes `BepInEx/GK2-carry-facts.txt` (the donkey prefab's drawer rules, its inventory filters, every big item, the overhead mesh elements) and "Put in cart inventory" / "Take first out" add or remove a plain test item on the mounted donkey's own 2-slot inventory to watch the game's loaded-cart mesh react (Round 7 of the findings; groundwork for carrying cargo). "Drop it here" spawns any big item in front of the keeper with the game's own drop routine (a normal, saved world drop; `<` `>` cycle through all of them; a corpse comes from the delivery script's body definition; zombies cannot be spawned from an id). |
| `DisableOnGameUpdate` | true | Do nothing if the game's code changed since the audit. |

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
