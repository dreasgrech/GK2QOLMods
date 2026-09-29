# GK2 Mounts

BepInEx plugin for Graveyard Keeper 2: press a key and the keeper rides a donkey. You walk 1.75× as fast while
mounted (configurable), the donkey follows your every step, faces the way you walk and animates when you do. Press
the key again to get off. Two looks: **Ride** (on the donkey's back) and **Cart** (you ride the donkey while it pulls
its cart).

Version 0.1.0 (first playable build), built and audited against game build 25533739 (Assembly-CSharp MVID `97f92f0d-…`).
Design notes: [`FEASIBILITY.md`](FEASIBILITY.md); runtime facts the design rests on: [`PROBE-FINDINGS.md`](PROBE-FINDINGS.md).

The game has no mount of its own. The donkey you ride is the corpse-delivery donkey's *looks* only; the real donkey
NPC and its schedule are untouched.

## Settings (`BepInEx\config\dreas.gk2.mounts.cfg`, read at game start)

### `[General]`: behaviour

| Key | Default | Meaning |
|---|---|---|
| `Enabled` | true | Master switch. |
| `MountKey` | `H` | Mount / dismount. Pick a key the game does not use. |
| `BellKey` | `None` | While mounted: ring the donkey's bell. None = no key; the Sounds tab can set one (click, press a key, Escape clears) and has a play button. |
| (cargo) | | Cart style, no key of its own. Press the game's interact key with nothing in front of you to set the top cart item down on the ground in front of you, one item per press, as the game drops a held item on foot. Loading needs no key either: while you ride the cart there is no head storage, so anything you pick up (a log, stone, ore, crate or corpse from the ground, a pile, a pallet or a table) goes straight onto the cart, and piles, pallets, tables, graves and quest hand-ins take their items from the cart as if you held them. A full cart puts a ground pickup back down in front of you ("The cart is full.") and piles simply offer no take. The cart holds two items, like the game's own delivery cart. Zombies are refused and stay on your head. Cargo is handed back automatically at every dismount and before every save (it lives in a temporary inventory that no save contains) and goes back onto the cart after the save; through doors it stays on the cart. |
| `Style` | `Ride` | `Ride` (on the donkey's back) or `Cart` (you ride the donkey while it pulls its cart). Each style has its own section below. |
| `MountWgoId` | `npc_donkey` | Which world object's looks to use. Only the donkey is tested. |
| `StayMountedThroughDoors` | true | After a scene change the donkey is re-summoned and you keep riding. Off: dismount at every door. |
| `DismountIndoors` | true | Dismount automatically when entering a building, church or dungeon (the game's indoor lighting presets), and refuse to mount indoors. The cart is a 3D object that would cut through furniture and door frames inside. |
| `ShowMessages` | true | Thought bubble when you cannot mount ("I'm busy.", "Not in a co-op game."). |
| `SideViewOnly` | false | While mounted the keeper only faces left or right (the donkey has no up/down art). Forced on for poses that only have left/right frames (`FishingIdle`). |

### `[Ride]` and `[Cart]`: position, pose, animation, legs, speed

Both sections hold the same keys; the values differ because the two looks differ. The tuning panel edits the
section of the style you are currently mounted on. The table shows the values shipped at the time of writing,
Ride / Cart.

**Your settings and the mod defaults.** There are two kinds of values, and only two:

- *Your settings* are in `dreas.gk2.mounts.cfg`. BepInEx writes that file the moment a value changes, so there is
  no save button: what you set on the panel is kept.
- *Mod defaults* are what a fresh install starts with, and what right-click and the reset buttons go back to. They
  come from `defaults.cfg` next to the sources, embedded into the DLL at build time.

A panel row shown in yellow differs from its mod default; the footer counts them and the Defaults tab lists them
(yours and the default). On the machine with the sources (the build records the source folder), the mod reads
`defaults.cfg` from the repo itself, and **Make these the mod defaults** (click twice to confirm) writes every
current value of both styles into it. The yellow marks clear at once, and a rebuild ships the new defaults in the
DLL. On any other machine the built-in copy is used and that button is not shown. A new default never overwrites
a value already in someone's `dreas.gk2.mounts.cfg`: that row then shows yellow until it is reset.

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
| `IdleFidgetEverySeconds` / `IdleFidgetSpeed` / `IdleCrawlSpeed` | 7 / 0.45 / 0.05 | 7 / 0.45 / 0.05 | With the freeze on: instead of a dead stop, the pose plays for about a second every 7 s (randomised by half either way) at 0.45× speed, so the rider shifts now and then, and runs at a 0.05× crawl in between so the keeper's own breathing layer keeps a barely visible life. Interval 0 = the hard freeze; crawl 0 = fully still between fidgets. The freeze stops the whole animator, breathing included, which is why a frozen rider looked like a still image. |
| `PoseSpeed` | 1 | 1 | Playback speed of the pose animation while the donkey moves (1 = the clip's own speed). |
| `PoseSpeedFollowsDonkey` | true | true | Also multiply the pose speed by the riding speed multiplier, so it keeps pace with the donkey's walk animation. |
| `NearLeg` | false | false | Draw the rider's legs in front of the donkey while his own (baked-in) legs are hidden inside it. |
| `LegSource` | `Body` | `Body` | `Body`: his own trousers and boots re-sliced from the current body frame (outfit-exact, follows the pose). `Drawn`: a hand-drawn bent leg in outfit colours. |
| `LegHeightPx` | 15 | 10 | With `Body`: how many bottom rows of the body frame are legs (feet to belt). |
| `LegAdjustXPx` / `LegAdjustYPx` | 3 / 1 | 0 / 0 | Legs along the donkey relative to the torso (positive = towards the rear) and up/down relative to the back line (positive = higher). |
| `SnapToGround` | true | true | Place the donkey at the ground height under its own position, as the game does for walking objects. Off: it takes the keeper's height and digs into slopes when beside or behind him. |
| `CartFollowsGround` | true | true | Cart style only: the whole cart (wheels, cart body, lantern) is raised or lowered every frame, as one, by the difference between the ground under the cart body and the ground under the donkey, so the rigid 3D cart no longer sinks into a slope behind the donkey. The bars between donkey and cart take half of that shift. Sprites stay upright; nothing rotates. |
| `CartGroundMaxShift` | 0.8 | 0.8 | Largest shift the cart may get, in world units, either way. |
| `CargoDrawCorpse` | true | true | Cart style only: the mod draws every corpse itself with the game's corpse mesh, so corpses go through the same packer as any item, and the game's loaded-cart body stays off. Off: the first corpse is the game's own loaded cart body with the shroud (not movable); further items ride on top of it. |
| `CargoSwaysWithCart` | true | true | Cart style only: the cargo rocks with the cart while it rolls, as the game's own loaded corpse does. The game's corpse hangs under a cart node that the walk animation rotates about the axle by up to 3°; the mod's cargo holder copies that rotation about the same pivot every frame instead of being reparented, because the game's drawer switches those nodes on and off. Off: the cargo stays level. |
| `CargoBedDepth` | -0.26 | -0.26 | Cart style only: the cargo floor relative to the measured top of the empty cart body (negative = below the rails, on the bed floor). Every item's measured bottom is put on this floor. |
| `CargoSideGap` | 0.02 | 0.02 | Cart style only: gap between two items lying side by side across the bed. |
| `CargoOverhang` | 0.12 | 0.12 | Cart style only: how far two side-by-side items may together exceed the bed's measured width (0.75) before the second stacks on the first instead. |
| `CargoNestle` | 0.03 | 0.03 | Cart style only: how far a stacked item sinks into the one below. |
| `LanternAlong` / `LanternUp` / `LanternAcross` | 0 / 0 / 0 | 0 / 0 / 0 | Cart style only: move the lantern pole with its lamp and light, in world units: along the cart (positive = towards the back, away from the donkey), up, and across (positive = towards the camera). The prefab has the pole at the front rail, where the rider's head hides it. |
| `LanternTilt` / `LanternLean` | 0 / 0 | 0 / 0 | Cart style only: turn the pole about its own foot, in degrees: tilt in the screen plane (positive = top towards the back of the cart) and lean towards the camera (positive) or away. The lamp and its light follow the top; the lamp's sprites stay upright; the cart's walking sway (the game rocks the whole cart by up to 3° while it rolls) is kept. |
| `LanternSwayDegrees` / `LanternSwaySeconds` | 1.5 / 2.6 | 1.5 / 2.6 | Cart style only: a slow pendulum sway of the pole about its foot (degrees either way, 0 = off; period in seconds), with a slowly drifting amplitude, scaled by the game's wind value (about half in still air, up to one and a half times in a gale). The lamp and its lights follow, so the light pool shifts. The game itself never sways the pole except by rocking the whole cart while it rolls; this adds to that. |
| `LanternShow` / `LanternScale` | true / 1 | true / 1 | Cart style only: show the pole and lamp at all; size of the pole, scaled about its foot (the lamp moves to the scaled top, its pixel art is not scaled). |
| `LanternLightOn` / `LanternLightIntensity` / `LanternLightRange` / `LanternLightShadows` | true / 1 / 1 / true | same | Cart style only: the lamp's main point light. The game's values (range 4.5, intensity 3, soft shadows) are kept and the numbers multiply them, so a prefab change in a game update still shows through. |
| `LanternLightColor` | `FF9600` | `FF9600` | Cart style only: colour of both lights as hex RRGGBB (the game's orange). Three sliders on the Lantern tab edit it. Empty = the game's colour. |
| `LanternLightDayDim` | 1 | 1 | Cart style only: brightness of the lights and glow in full daylight, as a fraction of the night value. The game lights the cart lantern around the clock (1); its own hand lantern goes out by day (0). Follows the game's sun value through dusk and dawn. |
| `LanternSelfLightOn` / `LanternSelfLightIntensity` / `LanternSelfLightRange` | true / 1 / 1 | same | Cart style only: the lamp's second light, which lights only characters and sprites nearby (the game's: range 2, intensity 10). |
| `LanternGlowOn` / `LanternGlowAlpha` / `LanternGlowScale` / `LanternGlassOn` | true / 1 / 1 / true | same | Cart style only: the additive glow sprite around the lamp (switch, opacity and size multipliers) and the lit lantern-glass sprite. |
| `LanternFlip` | false | false | Cart style only: mirror the pole in the plane through its foot, so the slanted pole leans out over the back of the cart instead of forward over the donkey; the foot stays put. The lamp, glow and lights are mirrored with it (a mirrored sprite is how the game itself draws the donkey facing the other way). |

**How cargo is placed.** The game never arranges items on a pile (every pile in it is hand-made art switched by item count), so the mod packs the cargo itself. Every item's mesh box is measured at runtime; the larger footprint goes to the bottom or the far side, so the picture does not depend on load order; two items lie side by side across the bed, touching within the gap and centred as a pair, when their widths fit within the bed (0.75) plus the overhang, the density of the game's own timber stockpile; when the widths do not fit but the lengths do (two crates, 0.52 each on the 0.90-long bed), they lie end to end along the cart instead; and they stack otherwise. The 27 carryable items fall into seven size classes (log; flour sack; tall crates; bodies; 15 low crates; slabs = stone and ore; the two without a mesh, marble shown as a slab and the engineering crate as a generic crate). Corrections on top of the packer live in `cargo-layout.cfg` (embedded; `BepInEx\config\GK2Mounts.cargo-layout.cfg` overrides it when present): `[item <id>]` entries apply to the whole size class of that item, with offsets for the base role and for the on-top role; `[pair <a>+<b>]` entries force one arrangement (`side` = across, `along` = end to end, `stack`) and shift the second item. The panel edits the entries for what is on the cart. On the machine with the sources they are saved to the repo's `cargo-layout.cfg` as you edit (the layout has no separate "yours"; the file is the default and a rebuild ships it); elsewhere they last for the session. The Debug tab's cargo sheet (`GK2-cargo-sheet\index.html`, every pair) and shapes page (`shapes.html`, every item by size class) are the visual checks.

### Sounds (`[Ride]` and `[Cart]`, the Sounds tab)

The game's donkey walks silently (its walk animation has no step events) and the game has no cart sound, so the mod
plays these itself through the game's own sound player: game sound ids, 3D at the donkey, in the game's mixer
groups, so the in-game volume sliders apply. No audio files ship with the mod. Volumes and pitches multiply the
game's own values for that sound. Every sound id is editable (any id from the game's list; an unknown id plays
nothing, is flagged on the tab and logged once), and each has a play button on the tab. Defaults below are Ride / Cart.

| Key | Default | Meaning |
|---|---|---|
| `SoundsOn` / `SoundVolume` | true / 1 | Master switch and volume for everything below. Off: the game's own behaviour. |
| `HoofOn` / `HoofSoundId` / `HoofSurfaceAware` | true / empty / true | Hoofsteps on each footfall of the donkey's walk cycle. Empty id: the game's footstep for the ground under the donkey (grass, dirt, wood, stone; dirt where the ground is untagged). |
| `HoofVolume` / `HoofPitch` / `HoofPitchVariation` | 1.3 / 0.8 / 0.06 | Louder and lower than the keeper's steps; a small random pitch change per step. |
| `HoofStepsPerCycle` / `HoofPhase` | 4 / 0 | Footfalls per walk cycle and their timing within it (fraction of a step). |
| `GrazeOn` / `GrazeSoundId` / `GrazeVolume` | true / `donkey_eating_grass` / 1 | Chewing when the donkey grazes. The game has this sound, exactly as long as the graze animation, and never plays it. Stops if the donkey sets off mid-graze. |
| `LipsVolume` | 1 | The game's own lip-smacking on the lips animation (0 = silent). |
| `BraySoundId` / `BrayOnMountChance` / `BrayIdleEverySeconds` / `BrayWhileWalking` | `voice_donkey` / 0.5 / 40 / false | Brays (the donkey's dialogue voice): a chance at mounting, and on average one per interval while standing (randomised by half either way; 0 = never). |
| `BrayVolume` / `BrayPitch` / `BrayInSfxChannel` | 1 / 1 / true | The game plays this sound in the dialogue-voice channel (speech volume); on routes it through the SFX volume. |
| `WheelOn` | false / true | Cart: a wooden wheel rumble once per walk cycle while the cart moves (a borrowed machine wheel; its samples last one walk cycle). `conveyor_assembly_wheel` is a chain-and-rev alternative. |
| `WheelSoundId` | `conveyor_furnace_wheel` | The wheel sound. |
| `WheelVolume` / `WheelPitch` / `WheelPitchFollowsSpeed` / `WheelEveryCycles` / `WheelPhase` | 0.6 / 1 / false / 1 / 0 | Wheel tuning. |
| `CargoThumpOn` / `CargoThumpSoundId` / `CargoThumpVolume` / `CargoThumpPitch` | true / `oh_wood_container_drop` / 1 / 1 | Cart: a thump when an item lands in the cart, on top of the game's own pickup sound. |
| `BellSoundId` / `BellVolume` / `BellPitch` | `donkey_bell` / 0.7 / 1 | The donkey delivery's bell. |
| `BellOnSetOff` | false / true | Ring the bell when setting off; see the next row. |
| `BellOnlyWithCargo` / `BellSetOffIdleSeconds` | true / 4 | Ring when the donkey sets off after standing at least that long (optionally only with cargo). |

### `[Debug]` and `[Safety]`

| Key | Default | Meaning |
|---|---|---|
| `TuningWindow` | true | While mounted, a panel in the top-right corner with tabs General / Riding / Lantern / Sounds / Debug / Defaults. Changes apply live and are saved as you make them; yellow rows differ from the mod default; right-click a row to put it back to the mod default. `TuningWindowScale` (1.5) sets its overall size, `TuningWindowFontScale` (1) the text size inside it, `TuningWindowWidth` (470) its width; all three have - / + steppers on the Debug tab and the rows scale with the width. Turn off for normal play. |
| `TuningKeys` | true | PageUp/PageDown torso, Home/End legs sideways, Insert/Delete legs up/down, one art pixel per press, editing the current style's values. Turn off for normal play. |
| (Debug tab buttons) | | "Start a real delivery now" fires the game donkey's own `full_cycle` script event with its body roll forced to pass (a Harmony prefix on `Flow_Body.DoAction`, active for two frames after the press, drop chance untouched), so the real cart can be watched on the same ground as ours. "Dump the game donkey's script" writes the `npc_donkey` flow graph to `BepInEx/GK2-script-npc_donkey.json`; the text field sends any of its events. See `PROBE-FINDINGS.md` for the routine. "Dump carry facts" writes `BepInEx/GK2-carry-facts.txt` (the donkey prefab's drawer rules, its inventory filters, every big item, the overhead mesh elements) and "Put in cart inventory" / "Take first out" add or remove a plain test item on the mounted donkey's own 2-slot inventory to watch the game's loaded-cart mesh react (Round 7 of the findings; groundwork for carrying cargo). "Drop it here" spawns any big item in front of the keeper with the game's own drop routine (a normal, saved world drop; `<` `>` cycle through all of them; a corpse comes from the delivery script's body definition; zombies cannot be spawned from an id). "Photograph all cargo combinations" puts every distinct item shape alone and every ordered pair on the mounted cart in turn, screenshots and crops each, and writes `BepInEx/GK2-cargo-sheet/index.html`, a grid of all pictures (rows: first item, columns: second) with the settings used; the cart must be empty to start (a dismount mid-run aborts the sheet and discards its test copies). |
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

- **No save changes.** The donkey is a temp object outside `WorldData` (never added to a scene list, so the save's
  object graph cannot reach it or the cart inventory); the speed multiplier lives on a MonoBehaviour; the seat offset
  on a transform. Nothing new is serialized. Saves made while mounted load without the mod exactly as they would
  otherwise (you are simply on foot). Round 11 of `PROBE-FINDINGS.md` is the full audit: every save path (the only
  one is the end of sleep), every item hand-over and its GUID bookkeeping, and the menu, load, death and door cases.
- **Cargo is always handed back before a save**: the state change that leads to sleep dismounts first, and a prefix
  on `SaveSystem.Save` is the second line. What the game refuses to drop goes to your hands, and anything the mod
  cannot place is named in the log as LOST (never seen so far).
- **`MountWgoId` is checked** before anything is built: definitions with town quality, an NPC life-sim group or
  start items would leave a mark in the save when constructed, so they are refused with a message.
- **Worst case** if the plugin crashes mid-ride: `ForceCleanup` hands the cargo back and restores the keeper; at most
  a donkey view lingers until the scene unloads. Nothing persists. Setting `Enabled=false` mid-ride does the same.
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
9. Set `Style = Cart`, restart, mount: keeper on the donkey, the donkey pulling its cart with the lantern.
10. Quit to menu while mounted, load again: on foot, normal speed, no errors in the log.
11. Sounds: hoofsteps while walking (on grass, a road, a wooden floor), none of the keeper's steps while riding;
    graze chewing when the donkey grazes; brays now and then; in the cart the wheel rumble, a thump on loading and
    the bell when setting off with cargo. Game SFX slider at 0 silences all of them.

## Probe (`tools/GK2MountProbe/`)

Development-only plugin used to gather the facts above; it lives outside `src/` with its archived reports and logs.
Not part of `dist/` and not installed. What it dumps and its hotkeys: [`tools/GK2MountProbe/README.md`](../../tools/GK2MountProbe/README.md).
