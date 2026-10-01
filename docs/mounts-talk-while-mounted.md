# GK2 Mounts: staying mounted while talking

This was investigated on 2026-09-30 against the Steam build 25601286, using the decompiled flow nodes in
`decomp_steam/GK2.FlowCanvasNodes`.

**Built 2026-10-01 as proposed below, not yet tested in game.**
- **`Mount.WhyMustDismount`:** `ByFlow` no longer ends the ride by itself. A scripted walk, a staged note or the
  letterbox ends it (reason `script: ...`).
- **`Patches.cs`:** `SetState` (a state other than Walk or Idle), a new `SetTrigger(string)` prefix, and a new
  `SetDirection(Direction)` prefix.
- **`Mount.NoteScriptStaged`:** keeps the staged note until the script gives control back.

Also on the same day: the ride label hides while the game has its own interaction target (`InstanceControls`).
The two prompts on one key are gone.

**The question:** pressing E at an NPC while riding starts the conversation and dismounts the keeper. The log says
`control taken ByFlow`. Can he stay mounted, and what would that do to quests and cutscenes?

## How the game takes the keeper during a conversation

- **Taking control:** the NPC's flow script runs `Flow_SetControlActive` ("Control Disable", category
  Game/Cutscenes). That calls `PlayerController.SetControlTakenType(ByFlow, false)`, which does three things:
  - makes the keeper's body kinematic (`SetNonKinematicFlag(ByFlowPlayerControl)`);
  - drops his interacting item;
  - fires the global event `PlayerChangeControlByFlow`.
- **The same node, used in cutscenes:** it has an "affect cinematic" option that also shows the letterbox bars
  (`UICinematic`). Scripts can show those on their own too, with `Flow_Cinematic`.
- **Full cinematics** (`CinematicsSceneDisplayManager`) use a separate control type, `ByCinematics`. The mod already
  dismounts on it and keeps doing so.
- **The talking itself:** `Flow_Talk` / `Flow_MultiTalk` show speech bubbles over the NPC or the keeper
  (`Bubble.Talk`). Nothing is done to the keeper's body.
- **`ByFlow` is not only for talking.** Staged scenes, quest moments and the town-building window
  (`Flow_OpenTownBuildingWindow`, `TownBuildingPlaceInteractionHandler`) use it too. The control type alone can't
  tell a chat from a scene.

## What a script can do to the keeper while it holds control

| Flow node | What it does to the keeper | While mounted |
|---|---|---|
| `Flow_GoTo` with *movePlayer* | Walks him with **his own** `MovementComponent` (`MainGame.PlayerController.MovementComponent`), GD graph or world-zone path | Staged: dismount. Signal: `pc.MovementComponent.IsMoving` |
| `Flow_TeleportPlayer` | `PlayerController.Teleport`, taking `ByTeleport` | Already a door for the mod: `StayMountedThroughDoors` or a dismount at the scene change |
| `Flow_SetTriggerToAnimator` (Player) | `PlayerAnimation.SetTrigger(name)`: a gesture, kneeling and so on | Staged: dismount. Signal: a new prefix on `AnimationComponentBase.SetTrigger(string)` for the keeper's `PlayerAnimation` (the mod itself never triggers the keeper) |
| `Flow_SetStateInAnimator` / `Flow_SetTriggerToAnimator` with a state (Player) | `PlayerAnimation.SetState(state)` | A state other than Walk or Idle: dismount. The mod's `SetState` prefix already sees every one |
| `Flow_SetDirection` (Player) | `PlayerAnimation.SetDirection(Direction)`, which **can be Up or Down** | Keep him facing left or right. The mod's facing prefix covers only the `Vector2` overload today, so a script making him face Up would show him from behind on the donkey. Needs the `Direction` overload too |
| `Flow_LockPlayerMovement` | `PhysicalBody.LockMovement` | Harmless: he can't ride off, and the donkey stays |
| `Flow_Cinematic`, `Flow_SetControlActive` with cinematic | Letterbox bars (`UICinematic` active) | Staged: dismount. Signal: `LazyUI.Get<UICinematic>().gameObject.activeSelf` |
| `Flow_CameraFly`, `Flow_CameraAnim`, `Flow_Fade` | Camera and screen only | Harmless |
| `Flow_Talk`, `Flow_MultiTalk` | Speech bubbles and answer choices | Harmless: the bubble is placed at the keeper's head, which is on the donkey |
| `Flow_PlayerDropOverhead` | Drops what he carries | Cart style already routes overhead calls to the cart (`HeadReroute`) |
| Sleep, sermon, fight, work | Leave `FreePlayerState` | Already dismount (state check), unchanged |

## Would quests notice?

**No, as far as the code shows.**
- **What the scripts check:** quest and flow logic reads game resources, items, GD points, world objects and global
  events. None of them reads "is mounted", because the game has no such thing.
- **What the ride changes:** the keeper's speed multiplier, his seat offset, his pose (which replaces Walk/Idle), and
  the cart standing in for his hands.
- **Quest hand-ins:** these take the cart's item through `HeadReroute`, which is how carrying works already.
- **Global events:** `PlayerChangeControlByFlow` fires either way.

The risks are how a scene looks, not what it does. That is why anything staged still dismounts.

## Proposed rule

While **mounted** and **`ByFlow` is the only control taken** (besides the ones already ignored: UI, teleport, the
rider's own pose), **stay mounted**. The ride ends at once when any of these happens:

1. The keeper's own `MovementComponent` starts moving (a scripted walk).
2. A trigger is set on his `PlayerAnimation`, or a `SetState` other than Walk or Idle (a gesture or pose).
3. The letterbox bars show (`UICinematic` active), or `ByCinematics` is taken.
4. The state leaves `FreePlayerState` (as now).
5. Any other control type is taken (as now).

Also:
- **Facing:** a scripted facing of Up or Down keeps his last left/right facing while mounted. A new prefix on
  `SetDirection(Direction)`, the same rule as the `Vector2` one.
- **While the script holds control:** the donkey stands still (he can't move), with the pose and rider as when
  standing.
- **When control comes back:** nothing to do. He is still mounted.
- **Log:** each dismount says which signal ended the ride (`scripted walk`, `player trigger 'x'`,
  `player state X`, `cinematic`). The first test then shows which NPC scripts are plain chats and which are staged.

## What it costs

- **One new patch:** `AnimationComponentBase.SetTrigger(string)`, a prefix that only notes it. Add it to
  `Access.Verify` and the Cecil scan.
- **One more overload** on the existing facing patch, `SetDirection(Direction)`.
- **`WhyMustDismount` changes:**
  - `ByFlow` no longer counts by itself;
  - three checks are added: the keeper's `MovementComponent.IsMoving`, the letterbox, and a "staged" flag that the
    two prefixes set.
- **The flag** is cleared when `ByFlow` is given back.

## Test list (once built)

1. **A plain chat:** talk to the NPC in the screenshot while riding. The keeper stays on the donkey, the bubbles show,
   and afterwards he rides on.
2. **A quest hand-in** while mounted in Cart style: the item comes off the cart, as it does on foot with it in hand.
3. **A staged quest scene** (one that walks the keeper somewhere or has him kneel): the ride ends as it starts. The
   log names the signal.
4. **The town building window** (`Flow_OpenTownBuildingWindow`): stays mounted, and nothing looks wrong.
5. **A cutscene with the letterbox:** dismounts.
