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

**Sixth in-game result (2026-09-28 14:17): working.** Log review of the session (52 lines, no warnings or
exceptions): 5 mounts, each with the attached script skipped, 8 cart parts collected each time; one dismount by
"control taken ByFlow" (a cutscene or dialogue took the keeper, the expected reaction). Nine `[cart]` lines, all
explained by real geometry, none by a bad sample:

- `target 0.400 -> 0.000` / `0.000 -> 0.400`: the cart point leaving a terrain-mesh slope for the flat ground box
  ("Collider 0" is simply the flat ground at 3.595 or 5.995, the mesh tiles rise above it), or crossing onto or off
  `BigDropTopSurface`, a walkable surface 0.4 above the flat ground; the 3 units/s glide turns these into a 0.13 s
  step. Correction to the fourth/fifth notes above: the box is the ground of the flat areas, not something under the
  terrain everywhere.
- `navmesh: 0.385 -> 0.002`: the same slope-to-flat transition seen through the navigation mesh.
- `no ground sample` once at x≈86 (map edge or water): the previous shift was kept, as designed.

The `target jumped` log threshold (0.15) therefore also catches legitimate edges; it stays, as a debug aid.

## Round 7 (2026-09-28): carrying cargo on the cart, what the game's code allows

Source reading only (decompiled build 25533739); the in-game checks it calls for are listed at the end. Goal: let
the keeper carry items on the cart while riding, the way the game's own delivery donkey carries a body.

### What is carryable: "big" items, the ones the keeper carries overhead

- `ItemDef.itemSize` (`ItemSize.Small = 1`, `Big`) is a plain serialized field with no `[AutoParse]` key, so it is set
  in the balance asset and the earlier item CSV dump (`BepInEx/GK2-itemdefs.csv`) does not contain it. Nothing in code
  derives it from a group. The Debug tab's new "Dump carry facts" button lists every Big item from `GameBalance.Me.itemDefs`.
- Big is what the game treats as an overhead item: every dropped Big item gets a `BigDropInteractionHandler` (bodies
  and zombies a `ZombieDropInteractionHandler` on top, which adds the inspect action), the drop collector and
  `PlayerData` never auto-collect Big drops, and picking one up is `dropSystem.RemoveDrop` + `PlayerData.AddOverheadItem`.
- The item group `overhead` in the item table marks 29 items, which is the expected Big set: `body_corpse` (Corpse,
  groups body/corpse/overhead), `body_zombie`, `body_wild_zombie`, `wood` (Log), `stone`, `marble`, `iron_ore_2h`, and
  22 town supply crates `box_*` (group `town_box`). All have `stack_count` 1, so each occupies one inventory slot.
- Meshes exist for 27 of them: the keeper's overhead view (`PlayerView2D/.../overhead/item`, a `DropViewAtomMesh`,
  the same atom as `Assets/AddressableAssets/Drops/BigItem.prefab`) has elements named by icon id: `i_wood`, `i_stone`,
  `i_iron_ore_2h`, `i_body` (Object3D `drops_item_corpse_shroud`), `i_body_wild_zombie`, and 22 `i_box_*`, plus the
  `drops_item_placeholder` default (used with an error log when an icon id has no element) and 10 zombie-pose prefabs
  per pose (`drops_item_zombie_pose1_t1050..1059`, activated through `ActivateZombie` with the zombie's head id and LUT,
  not by icon id). `marble` is not in that list; the dump's "big items without an element" line settles it.
- Zombies (`body_zombie`, `ItemDef.isLinkedToWgo`) are items whose `UniqueId` points at a `ZombieWgoData` in
  `ZombieSystemData`; they sit in inventories like any item (the morgue pallets take them), and the drop handler
  re-links item and zombie on pickup (`ZombieDropInteractionHandler.LinkItemToZombieData`).

### How the keeper carries, including more than one

- `PlayerData.overheadItems` is a list. `OverheadStackLimit = 1 + GetResInt("extra_overhead")`; the game resource is
  raised by the expressions `IncreaseOverheadStackLimit(n)` / `SetOverheadStackLimit(n)` (a perk or talent), so
  carrying several items overhead is a built-in mechanic. `AddOverheadItem` drops the top item when the stack is
  full; `TryAddOverheadItemNoReplace` refuses instead; `TryGetOverheadItem(predicate)` searches top-down.
- Visual: `AnimationComponentBase.SetOverheadItems(items)`: item 0 on the prefab's `dropView`
  (`DropViewAtomMesh.Activate(iconId)`), every further item on a clone made by `EnsureExtraOverheadViews`
  (`Instantiate(dropView)`, parented under the first view, local position `LazyConsts.OVERHEAD_STACK_OFFSET`
  (0, 0.3, -0.04) x index, renderers' `sortingOrder` = index). Zombies use `ActivateZombie` instead of `Activate`.
- With more than one item overhead the game refuses work interactions and tool hints (`WorkInteractionHandler`,
  `WGOInteractionHandlerBase.GetInteractionInfoByUsingTool`), custom interactions, reservoirs, fights, and the
  `Flow_PlayerDropOverhead` node does nothing; `SetInteractingItem` (holding a small item in hand) is skipped.
- Input: the Interaction key first serves a Big drop under the cursor box (`BigDropUnderInteraction`), then the WGO
  under interaction, and only when neither did anything it drops the top overhead item in front of the keeper
  (`PlayerInputHandler.UpdateInput` -> `PlayerData.DropOverheadItem` -> `SpecialPhysicsCastUtils.GetPlayerDropPosition`
  -> `dropSystem.DropItem(item, currentGameSceneId, pos)`).

### How the game's cart carries the body: the donkey's own inventory

- Delivery (`full_cycle`, graph dumped in Round 6): `Flow_GenerateBody` (BodyDef `body_0_1` ->
  `new Item(linkedBodyItemId)` = `body_corpse`, its parts, pockets and burial rewards added as nested items, zombie
  skin and start-items properties rolled) -> `Flow_AddItem` (giveType Add, instanceAsInput, WgoData = the donkey) ->
  **`donkey.Inventory.AddItemToInventory(item)`**. At the morgue (`full_cycle_back`): `Flow_RemoveItemsFromWgoData`
  itemId `body_corpse` -> `removedItems` -> ForEach -> `Flow_DropItem` at GD point `RT_body_spawn` ->
  `dropSystem.DropItem`. So the cargo of the game's cart is literally the donkey `WgoData`'s inventory; there is no
  separate cart object. (`Flow_GetCorpseDropAmount`: 1, plus the const `donkey_drop_additional` while the resource
  `nun_many_body_drop` is set, capped by the morgue's free capacity; the extra bodies are generated at the morgue and
  go to the `morgue_pallets` group via `Flow_TryToAddCorpseToPallet` or are dropped at `RT_morgue_body_spawn`; they
  are never on the cart.)
- Capacity: `npc_donkey.inventorySize = 2` (Round 1). `WgoData.SetDataFromDefinition` builds
  `Inventory.Create(2, isFuelContainer, inventoryWhiteList, inventoryBlackList, id)`; the filters are unknown until
  dumped. Slot rules (`Item.CanAddItemCountToInventory`): white/black list properties, then free slots x `stackCount`
  (bags aside), so two overhead items fit. **Our temp donkey is built by the same constructor**
  (`new WgoData(id, pos, sceneId)` -> `SetDataFromDefinition`), so it already owns a 2-slot inventory in memory; being
  a temp object it is never written to the save.
- Visual: the donkey prefab's `WgoPart` has a `ConditionalDrawer` with **2 rules** (the Round 1 dump shows only
  `{...}`; the new dump prints them), `dropViewBig/Small = null`, and its `cachedObjects3D` are `donkey_cart_wheel_f`,
  `donkey_cart_wheel_b`, `donkey_cart_bars`, `donkey_cart_empty`, `donkey_cart_body`, `drops_item_corpse_shroud`,
  `donkey_cart_lantern`: the loaded cart (`donkey_cart_body_sway`, off by default) is the cart body plus the same
  corpse-shroud mesh the keeper's `i_body` overhead element uses. `ConditionalDrawer.SubscribeToEvents` hooks
  `Inventory.OnItemsAdd/OnItemsRemove` for `ItemsChanged` rules, and `WgoPart.OnEnable` runs `Init` on every spawned
  part, ours included. The available inventory conditions are `ItemCountCondition` (one item id),
  `ItemGroupCountCondition` (group, e.g. `body`), `TotalItemsCountCondition` and `ItemGroupInventoryIndexCondition`;
  the actions `SetActiveAction`/`SetActiveManyAction` (with `invertLogic`) and `UpdateDropViewsFromInventory`. The
  expected pair is therefore "count > 0 -> loaded body on" and "-> empty body off (inverted)", which means
  **adding an item to our donkey's inventory should switch the cart to the loaded look through the game's own code,
  with no mesh handling by the mod**. Whether the rule tests a body group or any item decides whether a log would show
  the shroud too (which would look wrong) or nothing. The Debug tab's "Put in cart inventory" test answers this.
- Mod interplay: `Mount.PrepareParts` keeps `donkey_cart_body_sway` off and the cart-lift list is collected from the
  parts active at mount time, so a part the drawer turns on later would not be lifted. `Mount.OnCargoChanged()` (new)
  resets the parts to their prefab positions and re-collects; the test buttons call it.

### Moving an item between the keeper and an object's inventory: the pallet and conveyor-cell pattern

- Put (town pallet, `WGODef.InteractionType.TownPalette`, `TownPaletteInteractionHandler`):
  `PlayerData.TryGetOverheadItem(item => wgo.Inventory.CanAddItemToInventory(item), out item)` ->
  `PlayerData.InsertOverheadItemTo(wgoData, item)` (= `AddItemToInventory` + `RemoveOverheadItem` + the trigger
  `PlayerInsertOverheadToWgoAnItem`); hint `hint_put`. The pallet is the "timber pile": a WGO whose `inventorySize`
  is the pile's capacity; every put fills the next slot.
- Take (`ConveyorCellInteractionHandler.Interact`): `Inventory.RemoveItemById(firstItem.id)` -> Big item ->
  `PlayerData.TryAddOverheadItemNoReplace(item)`, else `wgoData.MakeDrop(item)` (drops at the WGO's drop point, else
  the nearest dock point offset by `DockPointData.GetDropPos(itemSize, 0.3, 0.4, 0.5)`, else its position);
  `HasInteraction` is false for a Big item while `!HasFreeOverheadSlot`. The expressions `TakeOvrhdItem(id)` /
  `TakeOvrhdItemByGroup(group)` do the same with the free-slot check first.
- Stacked visuals for several items (pallets): the drawer action `UpdateDropViewsFromInventory` holds a list
  `bigDrops` of `DropViewAtomMesh`, and on every `ItemsChanged` activates `bigDrops[i]` with the icon id of inventory
  slot i and deactivates the rest. So "N items on a pile" = an inventory of N slots plus N atom meshes placed in the
  prefab. The donkey prefab has only the single shroud, so more than one item, or anything but a body, needs atoms
  of our own: instantiate the keeper's overhead `DropViewAtomMesh` (it has all 27 elements) the way
  `EnsureExtraOverheadViews` does, parent the clones to the cart body, place them with `OVERHEAD_STACK_OFFSET`
  (or side by side along the cart), `Activate(iconId)` per slot, and hide the prefab's shroud rule where it would
  double up. Zombies would use `ActivateZombie(headId, lut)` as `ApplyOverheadItemToView` does.

### Can the cart carry more than one item?

Data side: yes, two by the definition, and our donkey's inventory is ours (`Inventory.Data.InventorySize` is a plain
setter on `Item`), never saved, so a configurable capacity is possible. Visual side: one body is free (the prefab's
shroud); anything beyond needs the atom clones above. Gameplay side: nothing in the code caps a WGO inventory below
its size for overhead items; the only limits are the item filters (dump) and slots x stack 1.

### What a cargo feature needs (design notes, not built)

1. **Load / unload control.** Simplest and patch-free: a configurable key while mounted in Cart style. With an
   overhead item: `TryGetOverheadItem(cart can add)` -> `InsertOverheadItemTo(donkey.Data, item)` (the pallet
   path). Without one: take the last cargo item -> `TryAddOverheadItemNoReplace`, else `dropSystem.DropItem` beside
   the cart (the conveyor-cell path). Hooking the Interaction key instead would need a prefix on
   `PlayerInputHandler.UpdateInput` (the game's order is Big drop -> WGO -> drop overhead).
2. **Give everything back.** Cargo lives in a temp inventory that no save contains, so every path that ends the
   ride must empty it first: dismount (return to overhead up to the stack limit, drop the rest at the keeper's drop
   position), `ForceCleanup` (death, quit to menu, scene errors), sleep. With `StayMountedThroughDoors` the item list
   must be carried over in memory and re-added to the new donkey after re-summon. A save made while riding with
   cargo would lose the cargo; the save entry point should get a prefix that unloads first, or cargo is refused
   while a save is pending. To be investigated before building.
3. **Pickup while riding.** `BigDropInteractionHandler` works unchanged while mounted (the interaction box follows the
   keeper), so bodies and logs can be picked up onto the keeper's head as usual and then moved to the cart; the mount
   does not need its own pickup.
4. **Bodies.** A corpse on the cart is exactly the game's own case (shroud mesh via the drawer); zombies keep their
   `ZombieWgoData` link through inventories. `cur_bodies_count` is a game resource changed by the morgue script, not
   by inventories, so carrying bodies around does not disturb it.

### In-game checks (Debug tab, "Cart cargo (investigation)")

1. Mount in Cart style, press **Dump carry facts**: `BepInEx/GK2-carry-facts.txt` gets the two drawer rules with
   their condition fields and target objects, the definition's filters, the Big item list, and the overhead element
   names with any Big item that lacks a mesh.
2. **Put in cart inventory** with `wood`: the log line says whether the inventory accepted it (filters) and which of
   `donkey_cart_empty_sway` / `donkey_cart_body_sway` / `drops_item_corpse_shroud` are on afterwards; **Take first
   out** reverses it. Then the same with `body_corpse` (a plain corpse item without body parts, fine for a visual
   test) and with `stone`.

**In-game result (2026-09-28 15:05, `GK2-carry-facts.txt` and `[cargo]` log lines):**

- The two drawer rules are both `ItemCountCondition` on the item id `body_corpse`: count > 0 turns
  `donkey_cart_body_sway` on, count == 0 turns `donkey_cart_empty_sway` on (no inversion, both default off). So the
  game's loaded look is corpse-only: two test corpses showed the one shroud; two logs showed nothing (the empty cart
  stayed on). Zombies and every other big item need meshes of our own (clones of the keeper's overhead atom).
- The definition has no white or black list, so the donkey inventory accepts any item; 2 slots, both tests filled it
  to 2/2 and the third add would be refused by the slot count.
- Our donkey reacts to its inventory through the game's own drawer: every add/remove line shows the meshes switching
  synchronously, and `OnCargoChanged` re-collected the 8 lifted parts with `donkey_cart_body_sway` in place of
  `donkey_cart_empty_sway` while a corpse was aboard.
- 29 Big items, exactly the `overhead` group. The keeper's overhead atom lacks elements for `body_zombie`
  (`i_body_zombie`; zombies go through `ActivateZombie` with a head id instead, so not needed), `marble` (`i_marble`)
  and `box_engineering_mechanism` (the element is named `i_box_engineering_mec`, a truncated name, so the game itself
  would show the placeholder for that crate). Marble on the cart would need a fallback mesh (e.g. the stone element).
- Player overhead stack limit in this save: 1.

**Proof of concept built (2026-09-28 15:20, `Cargo.cs`):** cargo key (General.CargoKey, L) loads the overhead item
into the mounted donkey's inventory with `PlayerData.InsertOverheadItemTo` (the pallet path) and takes the last
item back with `Inventory.RemoveItemFromInventoryByUID` + `TryAddOverheadItemNoReplace`, else drops it at the
keeper's drop position (the conveyor-cell path). Zombies refused. Non-corpse items are shown by copies of the
keeper's overhead `DropViewAtomMesh` under a `cart_cargo` holder at the measured top of `donkey_cart_empty`
(placement: Cart.CargoUp / CargoAlong / CargoSpacing, panel sliders); the holder is a lifted cart part. Cargo is
handed back on dismount, cleanup and before `SaveSystem.Save` (Harmony prefix), and carried over in memory through
doors. Untested in game at the time of writing.

**First in-game result of the cargo proof of concept (2026-09-28 15:49, 54-line log, no warnings, errors or missing
mesh elements):** three Cart mounts; logs loaded to 2/2 each time; "unloaded on dismounted: 1 in hand, 1 dropped"
and "unloaded on indoors: 1 in hand, 1 dropped" (the indoors auto-dismount hands the cargo back too). Bed top
measured at local (0.37, 0.53, −0.03) under Base (the sway pivot's 0.36 offset plus the body's half height), cart
body 0.90 × 0.30 × 0.75. The `cart_cargo` holder is the 9th lifted part.

Correction to Round 6: `BigDropTopSurface` is the walkable top collider of a dropped big item (`DropView.
bigDropTopSurface`), not a terrain ledge. The two `[cart] target jumped` lines here (±0.400, source
`BigDropTopSurface/Collider 0`) are the cart point passing over the log the unload had dropped on the road, so the
cart climbed 0.4 over a dropped log. The cart raycast now skips that collider name; the keeper's own walking is
untouched.

**Visual result (2026-09-28, screenshots 15:5x):** the cargo mesh sits at the right height on the bed; a log on top
of a corpse looks right (the game's shroud below, our log centred above). Two logs overlapped: the spread ran along
the cart (local x), which is the log's own long axis (0.9 of the 0.9 bed). Spread moved to the cart's width (local
z, bed 0.75 wide), default spacing 0.3, so two items lie side by side.

**Two logs side by side (screenshot 15:5x) look right; a lone log centred on the bed looked floaty over the front
rim.** Slots are now fixed across the width (count = inventory size): the first item on the far side (+z, away from
the camera) against the back of the bed, the second towards the camera; a lone item therefore leans on the back wall.

**A lone log in the far slot looked about to fall off the back (screenshot 16:0x); two logs side by side look right.**
Cargo placement is now per slot: Cargo1Along/Up/Across and Cargo2Along/Up/Across in both style sections (defaults
0/0/0.15 and 0/0/−0.15), six live sliders in the Riding tab under "Cart cargo", exported with the other defaults.
The slot values are to be tuned in the panel and exported; the shipped defaults will follow.

**Big-item mesh sizes (dump 16:06, shared-mesh bounds in the element's space, along × high × across; pivot = the
element's origin):** every pivot is at the bottom (min y 0.00; the clothes crates −0.03). Log 0.82 × 0.57 × 0.35,
footprint centre 0.15 towards the camera of the pivot; stone and ore 0.74 × 0.33 × 0.35 (−0.13); corpse 0.84 × 0.33
× 0.43 (−0.06); wild zombie 0.86 × 0.30 × 0.43; crates 0.52–0.56 × 0.40–0.58 × 0.45–0.50 (footprint −0.19 to +0.01).
Bed 0.90 long × 0.75 wide. Consequences: one height rule serves every item (bottom on the bed); the pivot offsets
mean slot values must address the footprint, not the pivot; two logs fit across (0.70), two crates do not (1.00), a
corpse plus anything leaves 0.32. The game never faces this: each of its containers holds one item kind.

Built as `CargoAutoFit` (default on): after `Activate`, the mesh bounds are measured (`Cargo.MeasureMeshes`, mesh
bounds are valid on inactive objects), the footprint centre goes to the slot and the bottom onto the bed; a second
item is pushed across by half the two widths when the slots overlap, or stacked on the first when the two are wider
than the measured bed; with a corpse aboard, items sit on the loaded body's measured top. Slot defaults moved to the
footprint convention (slot 1 across 0, slot 2 −0.3), which reproduces the two-log picture the user approved. The
corpse itself is the game's mesh and is not moved by the sliders. Panel got a scroll view (Riding tab overflowed).

**Slots follow the inventory order (16:18):** a corpse in inventory slot 1 keeps slot 1 although the game draws it,
so a log loaded after it takes slot 2. **Several corpses:** the game's rule shows one shroud for any count, so the
first corpse is the game's, every further corpse is drawn by the mod with the corpse mesh (`i_body`, 0.84 × 0.33 ×
0.43) and stacked on the shroud by the auto-fit (two corpses, 0.86, are wider than the 0.75 bed). **Spawn button
(Debug tab):** `dropSystem.DropItem(new Item(id), currentGameSceneId, GetPlayerDropPosition)`, the game's own drop
path; corpses via `BodyDef body_0_1.GenerateItem()` as the delivery script does; zombies excluded (linked WGOs).

**`CargoDrawCorpse` (16:2x, default on):** the mod draws every corpse itself with the game's corpse mesh (`i_body`),
so corpses follow the same footprint rule and sliders as any item; after each inventory change the game's loaded
body (`donkey_cart_body_sway`) is switched off and the empty body on again (the drawer only re-evaluates on
inventory events, and the mod's refresh runs after each of them). Off: the game's drawer stands, first corpse = the
shroud, other items ride on its measured top.

**Corpse + log: slot 2 did nothing, slot 1 moved both (16:3x).** 0.43 + 0.35 = 0.78 tripped the "wider than the
bed, stack" branch at the 0.75 bed + 0.02, and the stacked item ignored its slot. Now up to 0.12 of overhang over the
rails is allowed before stacking (corpse + log lie side by side, two crates at 1.0 still stack), and a stacked item
takes (its slot − slot 1) as offsets from the item below, across limited so it stays on top.

**Push-apart removed (16:3x):** while tuning slot 2 across with a corpse in slot 1, the log stopped at the corpse's
edge as if it had a collider: that was the auto-fit's minimum-gap rule (half the two widths). With two hand-tuned
slots the rule only fights the sliders, so it is gone; auto-fit now means footprint/bottom alignment plus stacking
when a pair is wider than the bed + 0.12.

### How the game shows several items on a pile (dump 17:12, 12 inventory-driven drawers in the yard scenes)

**Nothing is placed procedurally. Every pile is hand-made art switched by count.** No object in the loaded scenes
uses `UpdateDropViewsFromInventory` (one mesh atom per slot); every one uses `TotalItemsCountCondition` rules with
`SetActiveAction` on meshes an artist positioned in the prefab:

- `wood_container` (the timber stockpile in the yard): 9 rules "total > n" (n = 0..8), each turning on one baked log
  mesh `wb_timber_stockpile_log_01..09`. The logs are separate meshes with their placement baked into the geometry:
  widths 0.26–0.34, heights 0.23–0.27, lengths 0.80–1.05; row 1 bottoms at 0.10 with centres 0.26 apart across
  (0.33, 0.59, 0.85), row 2 bottoms at 0.33, then 0.43–0.47, 0.57, 0.67: layers one log height apart, logs one log
  width apart, slightly irregular (an artist's pile, not a grid).
- Resource containers (`*_container_conveyor_TOP`: coal, stone, marble, clay, sand, iron ore, wood): four stage
  meshes at 25/50/75/100 % fill, each a differently shaped heap (stone 25 % = 0.44×0.23×0.40 at one corner, 100 % =
  0.62×0.53×0.47 centred), plus a broken/repaired top switched by `InteractableCondition`.
- Firewood sheds: three stage meshes (1–49 %, 50–99 %, 100 %) of pre-stacked wood blocks.
- `grave_ground`: `ItemGroupCountCondition` gravebot == 0 turns the bare ground on.

So there is no game rule for arranging N arbitrary items; the developers author a mesh per fill level per object.
What the artist data does give us is the intended density: logs packed side by side at one log width, layers one
log height apart, small irregular offsets. The stockpile logs are 0.23–0.27 high, while the overhead/drop log mesh
(`drops_item_wood_log`) is 0.57 high for 0.35 wide: the carried log is a different, taller model than a pile log.

**Mesh geometry is not readable:** every big-item mesh reports `readable=False` (GPU-only), so vertices and
triangles come back empty; only the import-time bounds are available (Triangles topology, 363 indices for the log).
Reading the vertex buffer back from the GPU (`AsyncGPUReadback`) would work but is not worth it: what matters is how
stacked items look on screen, and that is best judged from rendered pictures.

**Consequence for the design:** the cart layout must be the mod's own algorithm (nothing to copy), it should pack at
the artist's density (spacing = width, layer = height), and it must be verified visually across all pairs, which is
what the planned HTML permutation sheet (in-game screenshots per pair) is for. The screenshot harness should be built
before the layout algorithm is tuned.

**Cargo sheet harness built (17:17, `CargoSheet.cs`):** Debug-tab button; coroutine on the plugin: for every distinct
mesh shape (crates sharing `box_crates` collapse to one representative; shape key = the element's non-shadow,
non-blackout mesh names; marble = placeholder) alone and every ordered pair, the cart inventory is cleared and
refilled, two frames pass, `ScreenCapture.CaptureScreenshotAsTexture` at end of frame, cropped to the projected world
box ±(1.25, 1.1, 0.9) around the donkey root, PNG per combination, then `index.html` grid (rows slot 1, columns slot
2, first column alone) with the settings used. Panel hidden and cargo key blocked while running; previous cargo
restored. Needs UnityEngine.ScreenCaptureModule and ImageConversionModule references (added to the csproj).
