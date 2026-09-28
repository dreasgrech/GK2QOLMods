# Probe findings

Runtime facts collected with the probe plugin in `tools/GK2MountProbe/`. Raw reports and logs are kept next to it
(`report-round1-RuinedTemple.txt`, `log-round2.txt`, `log-round3.txt`). Game build 25533739, MVID `97f92f0d-…`.

## Round 1 (2026-09-27, scene RuinedTemple, keeper's yard)

### What went wrong, and what it taught us

Loading the save produced an in-game exception window:
`NullReferenceException in GK2.FlowCanvasNodes.Flow_SetWGOPosition.Teleport ← FlowCanvas StartEvent ← GraphOwner.Start`.

Cause: the donkey's definition has `attachedScript = "npc_donkey"`, and **`WgoData.PrepareForGame`, called from the
`WgoData` constructor, starts the attached flow script for every new `WgoData`**, temp or not
(`WgoDataScriptsManager.CreateScript`). The donkey script's start event is
`SetDirection → AddItem (to the donkey) → SetWgoInteractableState → Teleport WGO to GD point "donkey_01" → wait a frame → walk its route`.
The point `donkey_01` exists only in the graveyard scene, so in RuinedTemple the teleport node dereferenced null and
the whole script aborted at that node.

Consequences and conclusions:

- Nothing reached the save: the script died on its first real node, the temp `WgoData` is not in `WorldData`, and
  the script registry (`runningScripts`) is a runtime dictionary on a scene MonoBehaviour, not part of `GameSave`.
- `Wgo.DeInit` → `WgoData.DeInit` does **not** destroy the attached script; only `WgoData.OnRemove` does. A temp
  object destroyed through `Object.Destroy` therefore leaks its script object until the scene unloads.
- **The mount must never start the donkey's script.** If it ran, it would fight the follow loop (teleport, walk
  cycles through `MovementComponent`) and could reach saved state through flow nodes such as `AddGameRes`. The probe
  now sets a flag around its `new WgoData(...)` and a Harmony prefix on `WgoDataScriptsManager.CreateScript` skips
  creation while the flag is set. The real mod will do the same.
- The build preview never hit this because building definitions have no attached script.

### Player

- Hierarchy: `PlayerPhysicalBody` (layer Player 10; Rigidbody, MeshCollider, RVOController, PlayerController,
  PlayerPhysicalBody, LadderClimbController, PlayerLocalAreaMovement) → `PlayerView2D` (PlayerView, PlayerAnimation,
  PlayerMovementAdjustComponent) → `main_character` at local (0, **0.6**, 0) with the `Animator` → `gfx` → the
  sprite layers `hed, brd, eys, hrs, bdy, arm, bdy_over, bdy_ovr_hor, leg`, each scaled (1, 1.667, 1).
  Siblings of `main_character`: `DeformTextureSprite` (grass deformation), shadow casters (off), `BubblePoint`
  (y 1.6), `LarryBubblePoint`, wisp targets, `Banner` (the fight flag banner, off).
- **Rider offset**: `main_character.localPosition` is a plain prefab value (0, 0.6, 0). Raising the rider can simply
  add to it while mounted, no `LateUpdate` patch needed (the probe still uses the patch; either works).
- Rigidbody: mass 100, **linear damping 10**, angular damping 10, gravity off (custom gravity in code),
  interpolate, continuous-dynamic collision detection, rotation frozen. Movement adds a velocity change of
  `speed × multiplier × fixedDeltaTime` per fixed step with `PlayerPhysicsConfig.speed = 50` (the asset value; the
  code default of 5 is not what ships), i.e. 1 unit/s per step at 50 Hz. With damping 10 the terminal speed is about
  `50 × multiplier / 10 = 5 × multiplier` units/s, so **`SpeedMultiplier` scales top speed linearly**
  (1.75 → ~8.75 u/s). Other config: jumpForce 333, gravityFallScale 0.3, slopeGravityMovementSlowdown 0.2,
  attack dash force 15 / 0.2 s / 0.5 s cooldown.
- MeshCollider `player_mesh_collider`, convex, bounds (0.50, 1.07, 0.30), physics material `PlayerPhysMaterial`.
  Interaction trigger: BoxCollider (0.4, 1, 0.5) at local (0.4, 0.5, 0) on `InteractionComponent`.
- Animator `MainCharacterController` (override of `BaseCharacterController`), 391 clips, 44 parameters
  (`State` int, `Direction` float, `MovementDirection` float, `walk_speed` float, many triggers), 19 layers (Base,
  Lighting, Breath, Overhead, Backpack, OverheadInteracting, …). Walk clips 0.73 s at 100 fps, four facings.
- Sprites: 96×96 px at 50 ppu, shader `Custom/3D Sprite` ("VerticalSprite"), sorting layer Default, order 0 for
  all layers. Ordering between characters therefore comes from **world depth**, not sorting order.

### Camera

Orthographic, size 7.2, pitched 53.13° (forward (0, −0.8, 0.6)), so "away from the camera along the ground" is
**+Z**. `VisualConsts.CAMERA_ANGLE` 36.87° is the complementary angle; `Y_TO_Z` 0.75. Pixel size 2 (render type
Native). Transparency sort mode CustomAxis (0,0,0), i.e. the game relies on the depth-writing sprite shader.

### Donkey (`npc_donkey`)

- Definition: `interactionType = Script`, `attachedScript = "npc_donkey"`, `isMovable = true`, `inventorySize = 2`,
  `voiceId` set, portrait `portrait_icon_donkey`, quality hidden, navigation hole `InsideWorldZone`.
  `npc_donkey_test` is the same with `customAssetId → npc_donkey` and its own script. **There is no `cow` WGODef**
  (the `cow` assets are something else, probably scene decoration), so the cow is out as an alternative mount.
- One real instance in the save: uid `9efee165-…`, parked at (−199.68, 0, 0) in RuinedTemple with an inactive
  view, custom tag `npc_donkey`, no zone.
- Prefab `npc_donkey`: `WgoPart` → `Base` (Animator `CharacterNPC_Donkey`, override of `BaseCharacterController`,
  same 44 parameters as the player; **local scale (−1, 1, 1)**, i.e. mirrored) → `gfx` → sprites `bdy` (order 0)
  and `hed` (order 1) at local (−0.48, 0.6, −0.225), scale (1, 1.667, 1), layer CustomLightingObject 20, same
  `Custom/3D Sprite` shader, skin preset `0301_donkey`; plus `wheel_left`/`wheel_right` sprites and `light_sway`
  (lantern light). 3D `Object3D` parts: `donkey_cart_wheel_f`, `donkey_cart_wheel_b`, `donkey_cart_bars`,
  `donkey_cart_empty_sway`, `donkey_cart_body_sway` (off; holds the corpse shroud), `donkey_cart_lantern_sway`.
  Also `gfx (90 rotated)` shadow caster sprite, `Cylinder Shadowcaster`, `DeformTextureSprite`, an `Obstacle`
  capsule (r 0.5, h 1.85), an `Interactable` trigger capsule, two `NavMeshCutSphereCustom`, dock point `[DP] Right`,
  `BubblePoint` at (−0.78, 1.33, −0.225).
- **No `WgoPartState` variations** (list empty), so there is no "without cart" variant to switch to. Hiding the
  cart means deactivating the parts listed above by name, which the probe's `F12` now does.
- Animator clips of interest: `static_left/right` (idle), `walk_left/right` (0.97 s, loop), `graze_left/right`,
  `ear_left/right`, `lips_left/right`, `turning_head_to/away_from_camera_*`. Only left and right facings exist,
  matching `DonkeySteppedRotationPreset`. `MovementDirection` float is −90 at rest.
- The donkey body sprite sits at local y 0.6 like the player's, so the rider's seat will need roughly +0.4 to +0.6
  on `main_character` to sit on its back; to be tuned with `PageUp`/`PageDown`.

## Round 2 (2026-09-27, log in `tools/GK2MountProbe/log-round2.txt`)

- **Script suppression works.** Both temp donkeys logged `Skipped attached script`, no exception window, no errors
  in the log.
- **Follow experiment works end to end**: temp donkey spawned in a different scene (the player had moved on), parts
  loaded, colliders disabled, cart hidden (12 parts) and shown again (11 parts; the corpse-shroud body stays off),
  depth offsets cycled through all five steps, rider offset swept from −0.55 to +0.60.
- **Speed**: with the 1.75× multiplier the measured walking speed was 5.2–6.2 units/s (rigidbody velocity peaked at
  6.8). The probe did not record a 1× baseline in this round (measurement only ran while the boost was on; fixed
  for round 3). Working through Unity's per-step damping (`v *= 1 − damping·dt` = 0.8 per step) gives an
  equilibrium of `4 × Δv`: about 4.0 u/s along X and 3.2 u/s along Z at 1×, 7.0 / 5.6 u/s at 1.75× (the game scales
  the Z input by 0.8). The measurements sit in that band, so the multiplier does scale speed roughly linearly; the
  baseline run will confirm it.
- The SSM type stayed `FreePlayerState` throughout: opening UI, working and sleeping don't switch the state type,
  they flip control flags (`TakenControlType`) and make the state inactive. Round 3 logs those flags.
- Visual judgement (sorting at the depth steps, mirrored `Base` when facing flips, cart-less look, seat height) needs
  the screenshots from this run.

## Round 3 (2026-09-27, log in `tools/GK2MountProbe/log-round3.txt`, screenshots 22:24–22:26 in the user's Captures folder)

**Speed is linear in the multiplier.** Steady-state walking speed, measured over 1 s windows:

| Direction | 1.0× | 1.75× | ratio |
|---|---|---|---|
| left/right (X) | 3.9 u/s | 6.9 u/s | 1.77 |
| up/down (Z) | 3.1 u/s | 5.5 u/s | 1.77 |

Z is slower because the game scales the Z input by 0.8. The per-step damping model from round 2 predicted 4.0 / 7.0
and 3.2 / 5.6, so it holds. A mount multiplier is therefore a direct "times N" on walking speed.

**Control flags.** Opening a chest or the inventory flips `TakenControlType.ByUI`; walking through a scene door
flips `ByTeleport` (the follower survived both teleports, so they were within the loaded scene). The SSM stayed in
`FreePlayerState`. No bench, ladder or weapon was used this round, so `ByWork`/`ByLadder`/`ByAttack` remain
unobserved but their handling is the same flag mechanism.

**Visuals (16 screenshots, cropped and inspected):**

- **The donkey draws about 0.48 units to the side of its data position**, to the right when facing right and to
  the left when facing left. That is the prefab: the body sprite sits at local x −0.48 under the mirrored `Base`
  root, so the visual centre flips with facing. A mount must pull the data position back by `0.48 × facingSign`
  (the probe now does this, `F7` toggles it).
- **Sorting is clean at every depth step.** No flicker. At depth 0 the donkey already draws over the rider's arm,
  so a slightly negative depth (towards the camera) puts the donkey in front of the rider's legs while the raised
  torso stays visible above its back: the classic riding silhouette. New probe defaults: depth −0.15, seat +0.5.
- **Facing flips correctly** when walking left/right (Base mirrors). Walking up/down shows the side view facing the
  last horizontal direction; acceptable for a sprite with two facings.
- **Cart style works too**: with the cart shown the keeper stands where the driver would be, cart behind, donkey in
  front, lantern lit at night. Worth offering as a second style (`Ride` / `Cart`) in the mod.
- Cart parts toggled: 12 hidden, 11 shown (the corpse-shroud body stays off).

## Round 4 (2026-09-27): first mod build in play

- Centering had to read the donkey's real facing: straight up and straight down both resolve to the left-facing
  sprite, so predicting from the last horizontal direction doubled the offset when walking down. Fixed by measuring
  the body sprite's offset from the root each frame in LateUpdate (also fixes the one-frame flash on a facing flip,
  since the animator mirrors one evaluation after the direction changes).
- Following `PlayerData.position` (50 Hz physics value) made the donkey stutter against the Rigidbody-interpolated
  keeper; following the body transform in LateUpdate and snapping to the same pixel grid fixed it.
- The donkey has no up/down art, so the keeper is locked to a side view while mounted (`SideViewOnly`).

## Round 5 (2026-09-27): sprite sheets, for the seated pose

The probe now dumps the character atlases (three 2048² textures: `MainCharacter`, `Characters` ×2) and a CSV of all
9,734 sprites on them (`tools/GK2MountProbe/sprites-round5.csv`; the next dump also has the trimmed-rect offsets).
What the pixels say:

- **Keeper art is palette-indexed.** `9008_bdy_static_left` is 20×37 px of flat index colours: tunic `5bab01` /
  `376701` / `132301`, trousers `b801c8` / `eb07ff` / `3e0144`, boots `0e01c9`, belt and soles `533320`, skin
  `b26845`. The `Custom/3D Sprite` material maps them to the outfit palette (32×2 texture, `USE_PALETTE_COLOR_REPLACE`).
  Anything drawn in those index colours and rendered with the keeper's body material recolours itself to the outfit.
- **The keeper's legs are baked into the body sprite** (rows 27–34 of 37, only 8 px tall in side view). The separate
  `leg` layer (`9019_leg_*`) is the armour set and is off in normal play. The keeper has **no right-facing frames**
  at all: facing right is a mirrored parent transform, like the donkey's `Base`.
- **No seated keeper pose exists.** `bath` is a naked front-facing tub pose; `organ`/`choir` clips are zombie church
  animations; `custom_loop_*` are cutscene placeholders. One NPC (`0920`) has a side-view `sitting_left` frame, but it
  is a pure black silhouette (shadow character), useful only as a pose reference.
- **Donkey geometry** (`0301_bdy_static_left`, 50×47 with a 2 px transparent border on every frame): ground at row 44,
  back line at row 20 (24 px above the ground), body 18 px thick (rows 20–38), legs 6 px. Walk frames bob the back by
  1–2 px. Head is a separate `hed_static_down` sprite; only left-facing frames exist.

**Seated design that follows from this** (mock-ups from the real frames in `tools/GK2MountProbe/frames/`):

1. Raise the keeper so the hip (7 px above the feet) sits on the donkey's back line. With the donkey drawn in front,
   the 8 px standing legs disappear inside the donkey's 18 px body. The mod computes this from both sprites'
   trimmed-rect offsets along the camera's screen-up axis (`Ride.AutoSeat`), instead of a guessed constant.
2. Draw a **near leg**: a 12×14 px sprite, thigh along the back, knee forward, shin down the flank, boot at the belly
   line, in the trouser/boot index colours, rendered with the keeper's body material. It is parented under the
   donkey's body sprite so it mirrors with the donkey's facing, sits on the donkey's depth plane (drawn over the
   donkey, in front of the hidden real legs), and its hip is placed at "back line, frame centre + 2 px to the rear"
   every frame from the current frame's offset.
3. Cart style keeps the standing keeper (no leg, no auto seat).

Round 6 will tune the seat and leg by a pixel or two in game (`Debug.TuningKeys`).

## Round 6 (2026-09-28): the game donkey's own delivery routine (`npc_donkey` flow script)

Dumped with the Debug tab's "Dump the game donkey's script" button to `BepInEx/GK2-script-npc_donkey.json`
(65 kB, 178 nodes, 17 custom events). Read with a small graph printer (nodes + `BinderConnection` edges).

Events and what they do (`->` is the flow order; `GoTo` is the game's `Flow_GoTo` with `MovementType` GDGraph):

| Event | Chain |
|---|---|
| `full_cycle` | GetGameRes `global_lock_on_drop_bodies` (player res; true = stop) -> quest `33_base_complain_go` InProgress? yes: fire `move_to_morgue` on itself; no: **Body Roll** -> FX `puff_npc` at `donkey_01` -> SetDirection -> AddItem body (`body_0_1`, never auto-destroy) -> SetWgoInteractableState -> SetWGOPosition to GD point `donkey_01` -> GoTo GD point `donkey_05`, on finish `full_cycle_back` -> FireEventTrigger 41 |
| `full_cycle_back` | loop over `Flow_GetCorpseDropAmount` bodies: `morgue_entrance_door` exists? -> trigger `action` on it, wait 0.25 s, `puff` FX at `morgue_entrance_fx_spawn` -> per body: TryToAddCorpseToPallet (GenerateBody at `RT_morgue_body_spawn`), else DropItem at `RT_morgue_body_spawn` / `RT_body_spawn`; `cur_bodies_count += 1` -> sound `donkey_bell` -> quest checks (`21_base_donkey_nun` Available, `137_base_resurrection_donkey` InProgress, `165/166/167_base_donkey*_speak` InProgress) -> either stay (dialogue events) or GoTo `donkey_01`, on finish `Disable` -> FireEventTrigger 42 |
| `move_to_morgue` | SetDirection -> SetWgoInteractableState -> teleport to `donkey_01` -> GoTo `donkey_05`, on finish `stay_morgue` |
| `stay_morgue` | interactable on, direction, `donkey_bell`, FireEventTrigger 42 |
| `move_from_morgue` | EnableLazyAnimationController -> interactable off -> GoTo `donkey_01`, on finish `Disable` |
| `Disable` | teleport to GD point `RT_spot` (its parking place, off screen) |
| `add_body` / `drop_body` | put a `body_corpse` on the cart / drop every carried body at `RT_body_spawn` and count it |
| `SetInteractive` / `SetNonInteractive` / `interaction` | interactable flag; `interaction` opens the multi-answer dialogue |
| `21_base_donkey_1`, `137_base_resurrection_donkey_1`, `165/166/167_base_donkey*_1` | quest dialogues: StopGoTo, face the player, dialogue lines, then `move_from_morgue` |

**Body Roll** (`GK2.FlowCanvasNodes.Flow_Body.DoAction`): fails when `cur_bodies_count >= morgue.GetTotalQuality()`
(morgue full) or when `donkey_body_drop_chance` (player res) loses against `Random.Range(0,100)`; each outcome
moves the drop chance by a `ConstDef` (`donkey_drop_chance_*`, or `nun_many_drop_chance_*` when
`nun_many_body_drop` is 1). Only the Success exit drives the cart.

So the route the user sees is **`donkey_01` -> `donkey_05`** (GDGraph navigation), then back. The view height along
it comes from `WgoMovementAdjustComponent.UpdatePos` (root sampling), see round 5 notes and `Mount.GroundHeight`.

**Cross-check with the Nexus mod "Trigger Donkey Delivery" (nizy.gk2.triggerdonkeydelivery 1.0.0, decompiled from
the DLL in the user's Downloads):** it does the same thing on a key press: `GetWgoDataByCustomTag("npc_donkey")`
-> `FireEvent("full_cycle")`, gated on the player res `donkey_ready > 0`, plus a Harmony prefix on
`Flow_Body.DoAction` that repeats the roll without the morgue-capacity check. Our button differs in three ways:
it looks the donkey up by wgo id first (custom tag as fallback), it forces the Success exit outright and leaves
`donkey_body_drop_chance` alone (debug tool, no economy side effect), and it logs the preconditions
(`global_lock_on_drop_bodies`, the complain quest, body count vs morgue capacity, `donkey_ready`) so a no-show is
explained. `donkey_ready` is not read by the script itself; the Nexus mod treats it as "the donkey has been
unlocked", which is a sensible reading and is why we print it.

How to use it for the slope comparison: stand where the cart clips, press "Start a real delivery now", watch the
game's cart cross the same ground, and press "Write geometry to log" while it is on the slope so the `REAL DONKEY:`
line (root, body sprite, cart parts, ground samples) can be set against ours.

### Why the donkey alone is fine on slopes and the cart is not (2026-09-28)

From the round-1 hierarchy dump of the `npc_donkey` prefab:

- The donkey itself is flat art: upright `SpriteRenderer`s (`bdy`, `hed`, ...) under `gfx`, scale (1, 1.667, 1),
  drawn 0.48 to the facing side of the root. A vertical sprite touches the ground along its bottom pixel row only, so
  once that row sits at the ground height under the body (SnapToGround samples exactly there) the terrain can never
  cut into it, whatever the slope.
- The cart is **3D geometry**, not sprites: `donkey_cart_wheel_f`, `donkey_cart_wheel_b`, `donkey_cart_bars`,
  `donkey_cart_empty_sway/donkey_cart_empty/donkey_cart_body` (and the off-by-default loaded body) are `Object3D`
  components with `-MERGE S_OBJ` meshes (wheel 0.52×0.5×0.2, bars 0.76×0.17×0.88, body 0.9×0.3×0.75), laid out
  0.3 to 1.15 units behind the root along the ground. Two wheel sprites (`wheel_left`, `wheel_right`,
  `0301_wheel_walk_*`) sit at the root, and the lantern (`light_sway` -> sprites + two `Light`s) at the cart.
- Rigid body meshes with that much ground extent meet a slope the way any 3D object does: the end that is uphill
  sits below the terrain surface and the terrain, which depth-tests against the meshes, hides it. On the Inquisition
  Base slope the ground rises about 0.5 over the cart's length (samples in the geometry log), and the parts sat at
  4.97–5.33 with ground 5.51 behind them.
- The game never draws a cart on such ground: the delivery route (`donkey_01` -> `donkey_05`) avoids that hill, the
  user confirmed the real donkey does not cross it. `WgoMovementAdjustComponent` only sets the root height.

Fix taken (option A, `CartFollowsGround`, per style, default on): every frame after the root is placed, each
top-level cart part is moved straight up or down by (ground under its renderer − ground under the donkey body) ×
weight, capped by `CartGroundMaxShift` (0.8). Weight 1 for wheels, cart body and lantern, 0.5 for the bars so they
split the difference between donkey and cart. Rest positions are the prefab's local positions taken at spawn; the
`_sway` nodes carry a pivot offset (0.36, 0.267) that their child cancels, so any sway the animator adds should be a
rotation about the axle, which a local-position write does not disturb. The geometry log's new `cart lift:` block
prints, per part, lift, ground sample and source, local position vs rest, local rotation and world position, so this
assumption and the visible result can be checked in one press. Because the parts are meshes, tilting them (option B)
would not have the pixel-art rotation problem noted earlier; it stays the fallback if the stepped look is not
acceptable.

**First in-game result of option A (2026-09-28, Inquisition Base slope, geometry log lines 31–32):** the cart body
no longer sank, but a second pair of wheels floated above the cart. Two causes, both from the log:

1. The prefab reuses names down the tree: each `Object3D` root has a mesh child of the same name, and the far wheel's
   mesh child is itself called `donkey_cart_wheel_f`. The collector took 11 "parts" instead of 6 and moved parent
   and child both, so the wheel meshes went up twice (0.396 + 0.396) and the bars 0.046 + 0.046. Fixed: a transform
   with a cart-part ancestor is skipped.
2. The wheels exist twice: as animated sprites (`wheel_left`, `wheel_right`, `0301_wheel_walk_*`) at the root and as
   meshes on the axle. Per-part sampling gave the sprites 0.23 and the meshes 0.40, and any difference between the
   two shows as a second pair of wheels. Fixed: one shift for the whole cart, sampled under the cart body
   (`donkey_cart_empty_sway`), bars at half.

Also learned: the cart parts' transform offsets (z 0.3, 0.85) are lateral, across the cart's width; the length of the
cart along the travel axis lives inside the mesh geometry (body 0.9, shafts 0.76 long), so "ground under the part"
has to be sampled under the renderer bounds centre, not at the transform. And the `_sway` nodes stayed at
`lrot=(0,0,0)` in both readings, so the animator does not move them while the donkey is idle; a reading while
walking is still wanted.

**Second in-game result (2026-09-28, video `2026-09-28 13-56-30.mp4`, 120 fps):** on the slope the cart now rises
with the ground and the wheels stay with the body (the shafts split the gap, accepted for now). On flat-ish ground
near the Inquisition Base ruins, at night, the cart flickered. Frame by frame the cart is not culled: it drops below
the terrain for 1–3 rendered frames at a time, wheels first, then the whole cart, while the lantern's light keeps
lighting the grass (a Unity light is not occluded). That is the whole-cart shift hitting its negative cap, so one
of the two ground samples was intermittently wrong by more than half a unit. Both samplers ignored height:
`ClosestAsSeenFromAbove` on the navigation mesh cannot tell stacked levels apart (the ruins have walkways and floors
above and below the road), and the single ground raycast from 1 above took the first walkable-layer collider it met.

Changes made, all in `Mount.FollowCartParts` and its helpers:

1. Both samples come from one source (navmesh for both or raycast for both), as the donkey/keeper delta already
   did, so a height mismatch between the two sources can never move the cart on flat ground.
2. Both samples look for ground at the keeper's own level: the navmesh query uses true 3D distance from a point at
   the keeper's height and accepts a node only within 1.5 of it; the raycast collects every walkable-layer hit
   between 1.5 above and 1.5 below and keeps the one nearest the keeper's height.
3. A height difference over 1.2 between the two samples (steeper than any walkable slope) is rejected and the
   previous target kept.
4. The applied shift moves towards the target at 3 units/s, so a one-frame outlier cannot make the cart jump.
5. Every rejected sample and every target jump over 0.15 is logged as `[cart] ...` (at most twice a second) with
   both samples, their sources and positions; the geometry log's `cart lift:` block shows target, applied shift and
   the anomaly count.

**Third in-game result (2026-09-28, video `2026-09-28 14-05-52.mp4`):** the flat-ground flicker is gone. Turning on
the slope made the cart dip into it. The `[cart] target jumped` log lines show why: at each turn the target flips
sign (+0.38 -> -0.38) because the cart swaps to the donkey's other side in one frame (the sample point moves about
1.7 units), while the applied shift glided there at 3 units/s, leaving the cart a quarter second on the new side with
the old side's shift. Fixed: a sample point that moves more than 0.5 in one frame (walking moves it at most 0.12) is
a side change, and the new shift is applied at once; the glide only smooths sample noise. Side changes are not
logged as anomalies any more.

**Fourth in-game result (2026-09-28, video `2026-09-28 14-07-02.mp4`, recorded with the build before the side-change
fix):** going down the road at Inquisition Base the cart, uphill behind the donkey, sank instead of rising. The
`[cart]` lines show the cause: at x≈21.1 the cart sample hit "Collider 0" at exactly 3.595 (a flat box under the
terrain) while the reference hit the terrain mesh "Ground_01" at 3.78; the terrain surface under the cart was higher
than the keeper's level by more than the box was below it, so "nearest to the keeper's level" chose the box, and the
cart got -0.19 where +0.38 was right. The terrain is layered in places: a walkable mesh with a flat box under it,
sometimes a bridge or roof above. Fixed in `GroundHeightPair`: when both points hit the same collider, that
collider's (highest) hits are used for both; otherwise the highest walkable hit within the level window at each
point. The level window (1.5 above and below the keeper) still keeps far-away levels out.

**Fifth in-game result (2026-09-28, video `2026-09-28 14-10-49.mp4`):** with the "collider both points share" rule
the cart sank on the same slope as at the very start. The `[cart]` lines show `ref=3.595 cart=3.595 (Collider 0)`
with the keeper at 5.09: the terrain is made of tiles (Ground_01, Ground_02, ...), so the two sample points often sit
on different tiles and the only collider they share is the flat box 1.5 below, which gave a shift of 0. Rule
replaced by the plain one: the highest walkable hit within the level window at each point is the surface; the
window is now 1.0 above and below the keeper (a 45° slope climbs 1.0 over the cart's length; the box sits 1.5 below
the surface here, so it is outside the window).
