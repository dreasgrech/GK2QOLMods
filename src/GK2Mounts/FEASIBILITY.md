# Rideable mounts: does the game have them, and could a mod add them?

Investigated 2026-09-27 against game build 25533739 (Assembly-CSharp MVID `97f92f0d-…`), from the decompiled
game code, the Addressables catalog and the flow-graph data dumped earlier.

## 1. Does the game have mounts?

**No.** There is no mount, riding, saddle or vehicle concept anywhere in the code or data.

What does exist and is easy to confuse with one:

| Thing | What it actually is |
|---|---|
| `npc_donkey` (WGO) | The corpse-delivery donkey, an NPC like in GK1. It has its own voice (`VoiceID.Donkey`), dialogue lines (`5_intro_graveyard_donkey_*`, `21_base_donkey_*`, `165..167_base_donkey*`), quest events `DonkeyStart` / `DonkeyMorgue`, a `donkey_ready` game resource and a delivery point `donkey_01`. It is talked to, never ridden. |
| `DonkeySteppedRotationPreset` | Its sprite only has two facings, left and right (NPCs have four, the player four). |
| `0301_donkey` sprite atlas with a `_Cart` sprite, `donkey_cart_*_prt1/2` meshes | Its cart (2D sprite plus 3D parts). |
| `cow`, `cow_1`, `cow_2` (WGOs) | Decorative farm animals. |
| `FrightenedAnimalBehaviour` | Small critters that hide when the player walks into their trigger. |
| `PlayerController.attachedWgo` | The **fight flag** the player carries in the tower-defence fights, not an animal. |

## 2. How the player moves (what a mount has to plug into)

- The player is a **Rigidbody driven by forces**: `PlayerPhysicalBody.MoveByDirection` adds a velocity change of
  `physicsConfig.speed * SpeedMultiplier` per fixed frame (`speed` is 5 in the `PlayerPhysicsConfig` asset).
  `SpeedMultiplier` is a **public, settable property** the game itself uses: the bow and sword combat states set it
  to 0.5 while aiming and back to 1 on exit. Changing the player's speed is therefore one assignment, no patch.
- Control flow is a small state machine (`SSM`) on `PlayerController`: `FreePlayerState` (default) plus Work, Ladder,
  Build, Planting and seven attack states. `Ssm.CurState` is public; so are `AddState` and `ForceEnterState<T>`.
- Player visuals are **layered 2D sprites** (`PlayerAnimation` : `AnimationComponent`, a list of `SpriteRenderer`s
  re-skinned by name by `SkinChangerGK2`), standing upright in a 3D world seen from a fixed 36.9° camera. Animation is
  an `Animator` with an integer `State` (`AnimationState` enum: Idle, Walk, …) and a float `Direction`.
  `PlayerView.LateUpdate` snaps the view to the pixel-rounded body position every frame.
- Input goes through `LazyInput` (Rewired underneath). `LazyInput.GetKeyDown(GameKey.Interaction)` is public, and
  BepInEx's `UnityInput` works for a configurable extra key.

## 3. How NPC visuals work (what we can reuse for the animal)

- Every world object is a `WgoData` (saved) plus a `Wgo` view (MonoBehaviour) whose prefab is loaded by
  `Addressables.LoadAssetAsync<GameObject>("Assets/AddressableAssets/WGOs/<id>.prefab")`.
- The view follows the data: setting `WgoData.Position` fires `OnPositionChanged` → `Wgo.HandleChangedPos` moves the
  transform; setting `WgoData.MovableDirection` → `WgoPart.HandleDirectionChanged` → `AnimationComponent.SetDirection`;
  `WgoData.SetStateToAnimator(AnimationState.Walk/Idle)` drives the walk cycle. The `State`/`Direction`/`walk_speed`
  animator parameters are shared constants in `AnimationComponentBase`, so the donkey animates the same way the
  player does.
- **Temporary, unsaved objects are a supported game concept.** The build preview is exactly that:
  `Wgo.Spawn(new WgoData(id, pos, sceneId) { isTempObject = true }, gameScene.transform, true, ignoreChunkRegistration: true, applyDefaultWgoPartState: true)`
  (`BuildPointer.PrepareAndSpawnWgoBuildPointer`). A temp `WgoData` is never added to `WorldData` or any zone list, so
  it is **never written to the save**; `Wgo.Spawn` also skips GD-point registration for it, and the NPC life
  simulator only knows objects it can find via `WorldData.GetWgoData`, so a temp NPC is invisible to it. The preview
  is torn down with plain `Object.Destroy(gameObject)`; `Wgo.OnDestroy` → `DeInit` unbinds everything.
- The game already **carries a WGO around with the player**: `AttachTheFlag` disables the flag's colliders
  (`SetInteractableCollidersState(false)`, `SetLayerToAllColliders(26)`) and `PlayerController.Update` copies the
  player position into `attachedWgo.Data.Position` every frame. That is the pattern a mount needs.

## 4. Proposed design: "GK2 Mounts" plugin

**Mount = a temporary donkey view glued to the player + a speed multiplier + the rider drawn a bit higher.**
Nothing is added to the save; the mount exists only while riding.

Mounting (a configurable key, default anywhere like a whistle; optional setting to require standing next to the real
`npc_donkey`):

1. Refuse unless `Ssm.CurState is FreePlayerState`, controls are enabled, not in build mode, not co-op, no save
   loading in progress.
2. `Wgo.Spawn` a temp `npc_donkey` (same call as the build preview), `UpdateChunkVisibility(true)`, disable its
   colliders exactly like the flag and the preview do (layers 8/11/6 off, interactable colliders off, layer 26), so
   it never blocks, gets targeted or shows interaction hints.
3. Every frame (our `Update`): donkey `Position` = player position (+ a small configurable depth offset so it draws
   behind the rider), `MovableDirection` = player direction, animator state Walk/Idle mirrored from
   `PlayerData.charState`, walk-speed multiplier matched to the speed boost.
4. `PhysicalBody.SpeedMultiplier = MountSpeed` (config, default around 1.75).
5. One Harmony postfix on `PlayerView.LateUpdate` adds `Vector3.up * SeatHeight` while mounted so the keeper sits on
   the donkey instead of standing inside it.

Dismounting (key again, or automatically): restore `SpeedMultiplier = 1`, `Object.Destroy` the donkey, drop the seat
offset. Automatic when the state machine leaves `FreePlayerState` (work, ladder, build, planting, attack, fishing),
on `PlayerController.OnPlayerTeleported` and scene unload (the temp view is a child of the scene and dies with it, so
we also treat "our Wgo is gone" as dismounted), on sleep/death/return to menu. Opening inventory or a chest does not
dismount.

Configuration: key, speed, seat height, depth offset, `MountWgoId` (default `npc_donkey`, `cow` as an experiment),
`RequireNearbyDonkey`, `Enabled`, `Safety.DisableOnGameUpdate` as in the other mods.

## 5. Risk assessment

| Area | Risk | Why |
|---|---|---|
| Save corruption | **Very low** | The mount is a temp object like the build preview: not in `WorldData`, not in any zone list, no new serialized types. `SpeedMultiplier` lives on a MonoBehaviour, not in `PlayerData`. `charState` is read, not written. If the plugin crashes mid-ride the worst case is a stray donkey view until the scene unloads and a 1.75× walk speed until restart; nothing persists. |
| Game logic | Low | No game method's behaviour is changed except the view position in `LateUpdate`. The real `npc_donkey` NPC and its delivery schedule are untouched. |
| Physics feel | Medium | Movement is force based with ground friction, so 1.75× acceleration does not mean exactly 1.75× speed; collisions with invisible walls and slopes at speed need in-game tuning. The combat states already run at 0.5× through the same property, so the mechanism itself is proven. |
| Visuals | Medium | Two upright sprites at almost the same depth can z-fight; needs a tuned depth offset (or a sorting-order nudge on the donkey's renderers). The donkey has only left/right sprites, so riding up/down shows it side-on. It may spawn with its cart; whether a cart-less variation (`WgoPartState`) exists is only visible at runtime. Pixel rounding may make the rider jitter by a pixel relative to the mount. |
| Co-op | Refused | Same policy as Move Building. |

## 6. Unknowns that need a runtime probe (first step if we build it)

The game is running right now, and a new plugin only loads on restart, so nothing was installed. A small probe
(debug-only mode of the plugin, or a throwaway plugin) should dump, once a save is loaded:

- the player prefab hierarchy (`PlayerView` children, sprite renderers, sorting layers/orders, animator parameters);
- a temp-spawned `npc_donkey`: its `WgoPart` variations, animator states and parameters, sprite bounds, whether
  the cart is a separate part, its `WGODef` fields (`interactionType`, `npcLifeSimGroup`, `attachedScript`);
- the same for `cow`, to see if it animates at all.

With that in hand the plugin itself is roughly the size of Owned Count plus a mount controller: one Harmony patch,
one per-frame follow loop, and the spawn/teardown calls above.

## 7. Alternatives considered

- **Ride the real donkey NPC**: would move a saved object around, fight its life-sim schedule and quest events,
  and risk leaving it somewhere it should not be if the game closed mid-ride. Rejected.
- **Speed only, no animal** (a "boots" mod): trivial and safe, but not a mount. Could be kept as a fallback setting
  (`ShowMount = false`) so the speed part still works if a game update changes the donkey prefab.
- **Custom mount art** (a horse): possible technically (a sprite sheet plus an Animator built in the plugin), but a
  lot more work than reusing the donkey, and it would look out of place next to the game's art.
