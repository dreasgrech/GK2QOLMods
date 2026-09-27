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
