# GK2 Mounts: idle shuffling for a standing donkey

This was investigated on 2026-09-30 against Steam build 25601286, using the source decompiled to
`scratchpad/decomp_steam`.

**Built 2026-09-30 (API 0.7.0), not yet tested in game.**
- **Code:** `src/GK2Mounts/IdleShuffle.cs`, driven from `MountInstance.StandTick`.
- **Switch:** `General.IdleShuffle`, off in the core. The showcase switches it on (`Behaviour.IdleShuffle`, default
  true).
- **API:** `Mounts.Behaviour` key `IdleShuffle`, the `Mounts.Shuffle` settings, per donkey through
  `MountHandle.Tuning`, and `MountHandle.IsStepping`. See docs/mounts-api.md, "Idle shuffle".
- **Panel:** the Instances tab has an "Idle shuffle" section.
- **User decisions:** off in the core and on in the showcase; donkeys without a cart only.

**What the user asked for:** a donkey nobody is riding shouldn't stand perfectly still. Now and then it should take a
few slow steps inside a small square around where it stands, then stop again. It shouldn't travel anywhere, go round
corners or follow routes. It only has left and right art, but it may step in any direction. It should never step
straight up or down the screen without also moving sideways.

## Short answer

- **We need no path finding.** Each step is a short straight line inside a box of about ±1 unit around the donkey's
  spot. The mod only checks that the line stays on walkable ground. It already has what that needs
  (`Mount.GroundHeight`: navmesh first, then a raycast).
- **The mod moves the donkey itself,** the same way `Follow` moves the ridden donkey:
  - It sets `Position` a little each frame, snapped to the ground.
  - It sets the animator to Walk, with `walk_speed` slowed down to match.
  - It sets a pure left or right `MovableDirection`.
- **We don't use the game's `MovementComponent`** (see below).
- **Straight up or down never happens by construction.** Every step's direction is within about ±35° of horizontal.
  The facing comes from the sign of the step's x, so the donkey faces the way it is stepping.

## What the game has

- **No idle wander anywhere.**
  - A search of all 2041 decompiled files for wander, roam, patrol and random-walk code found nothing.
  - `MobWalkIdle` only plays a zombie sound.
  - `FrightenedAnimalBehaviour` plays a one-shot hide trigger.
  - The NPC life simulation walks between points placed by hand, and only when a script asks.
  - The delivery donkey walks a fixed route (`Flow_GoTo`, GD points `donkey_01`→`donkey_05`, default speed 1.5).
- **The donkey's two facings.** The prefab uses `DonkeySteppedRotationPreset` (`AnimationComponentBase.cs:754-762`).
  Any direction with x > 0 faces right, x < 0 faces left, and exactly vertical (x = 0) faces **left**. So the game
  already accepts the donkey moving diagonally in side view. Only exact vertical is a problem, and our steps never
  have it.
- **Animator:** there are only three parameters.
  - `State`: Idle 0, Walk 1.
  - `Direction`: degrees, from the preset.
  - `walk_speed`: `speed / 1.5`. Speed 1.5 plays `walk_left` (0.97 s loop) at its authored rate, so a slow step at
    0.5 u/s plays it at a third of that rate.
  - The idle one-shots (graze, ear, lips) come from the animator itself. `Follow` fires `force_static` before Walk to
    cut them.
- **Art:** side views only.
  - Every body frame in the atlas is `_left`; right is the mirrored `Base`.
  - There is no up or down art. (`-bdy` is the shadow caster; the comment in `MountInstance.cs` now says so.)

## Why not the game's MovementComponent

A temp `WgoData` has a working `MovementComponent`, and `StartPath(..., MovementType.Direct, speed)` would walk it
in a straight line with the animation wired automatically. It is still the wrong tool, for three reasons:

1. **A global event.** Every arrival fires `GlobalEventsSystem.FireTrigger(WgoGoToFinished, MovableObjectId)`
   (`MovementComponent.cs:918`). The id is the definition id `npc_donkey`, and quest checks subscribe to (event, id)
   pairs. A shuffling donkey could therefore complete a quest step that waits for the delivery donkey. The quest data
   is in the assets and can't be checked statically.
2. **Lifecycle.** Its entry in `MovementSystem.movingObjects` isn't removed when the view is destroyed. Every
   destroy, move, ride or hitch would need `ForceStop()` first.
3. **It fights `Stand()`.** `Stand()` writes `Data.Position` from `Instance.Position` every frame, and the game's
   ground snap changes only the transform.

Moving it ourselves is about 100 lines per instance, fires no events and adds nothing to the game's lists.

## Design

### When it shuffles

Only a standing donkey **without a cart** shuffles (decided by the user, 2026-09-30):

- it has a donkey and no cart (`HasDonkey && !HasCart`), is not ridden and is not the ephemeral summon;
- its view is ready and its scene is loaded;
- the keeper isn't close (about 2 units), so the E prompt and getting on don't chase a moving target.

A donkey with its cart and a free cart never move. A donkey starts shuffling when its cart is unhitched and stops
when one is hitched.

### The loop, per instance

1. **Idle:** it waits a random `ShufflePauseMin`–`ShufflePauseMax` seconds (default 5–15). The animator's own idle
   variants keep playing, as now.
2. **Pick a step.** It tries up to about 12 random targets and takes the first that passes all of these:
   - **Box:** inside the box `Home ± ShuffleArea` (default 1.0 on x, 0.5 on z; the screen's up/down is z, so the box
     is flatter than it is wide).
   - **Length:** `ShuffleStepMin`–`ShuffleStepMax` long (default 0.3–0.9).
   - **Angle:** the direction is within ±`ShuffleMaxAngle` (default 35°) of horizontal. **Up and down are never
     picked.**
   - **Ground:** walkable at 4–5 samples along the line, with `GroundHeight` near the start's height (no stepping
     off a ledge).
   - **Spacing:** not within reach of another instance.

   It also prefers steps that keep the current facing, so turns are rare (`ShuffleTurnChance`, default 0.25). The
   facing always follows the step, so it never walks backwards.
3. **Step:** it moves `Position` toward the target at `ShuffleSpeed` (default 0.5, a third of normal walking pace).
   It eases in and out over the first and last 0.1 units, so the start and stop don't look abrupt.
   - Every frame it snaps to the ground and sets `MovableDirection = (sign(dx), 0)`.
   - At the start it sets Walk (with `force_static` first) and `walk_speed = ShuffleSpeed`; at the end, Idle.
4. Back to Idle.

- **Home:** the spot it was parked, spawned, `MoveTo`'d or called to (`ComeToKeeper`). Steps are always measured
  from home, not from the current spot, so the donkey never drifts away.
- **Interruption:** a step is dropped and the donkey is left where it is (that becomes its `Position`) at the start
  of any of these:
  - `RideFromWorld`, `ComeTo`, `MoveTo`, `Hitch` or `Unhitch`;
  - `ReleaseView`, `Discard` or `Remove`;
  - a scene unload.

  Because `Instance.Position` is always the truth, nothing needs resyncing.

### Where it hooks in

- **`MountInstance.StandTick`:** a new `Shuffle.Tick(dt)` runs before `Stand()`. It updates `Position`, and
  `Stand()` keeps writing the view from it. It only needs to skip its own "Idle once" line while a step is running.
- **Settings:** the Tuned values below, so each instance can have its own through the instance tuning and the
  panel's Instances tab.

  | Setting | Default | Unit |
  |---|---|---|
  | `IdleShuffle` | on or off (decision) | bool |
  | `ShuffleArea` | 1.0 × 0.5 | units (x, z) |
  | `ShuffleStepMin` / `ShuffleStepMax` | 0.3 / 0.9 | units |
  | `ShuffleSpeed` | 0.5 | u/s |
  | `ShufflePauseMin` / `ShufflePauseMax` | 5 / 15 | s |
  | `ShuffleMaxAngle` | 35 | degrees |
  | `ShuffleTurnChance` | 0.25 | 0–1 |

- **API:** nothing new is required, because the settings go through `MountHandle.Tuning`. A `MountHandle.SetHome`
  could come later.

## Limitations

- **Donkeys with a cart don't shuffle** (the user's choice): a turn would make the cart jump sides, and a cart
  needs more room than a small box gives.
- **No collision.** The view's colliders are off, but the steps are tiny and pause while the keeper is close.
- **Only while its scene is loaded.** In other scenes the donkey stands still.
- **Scenes without a navmesh:** `GroundHeight` falls back to a raycast. If that finds nothing, it doesn't step.
- **No room to turn:** turning moves the root about a unit to the body's other side, and that spot needs ground too.
  A donkey left with a fence or wall on that side can't turn. It then steps one way until it reaches the edge of its
  area and stands there. This is by design, not a bug.

## Check in game while building

- How slow `walk_speed` can go before the walk cycle looks like a stutter. It may be better to keep `walk_speed` a
  little above the movement speed.
- Whether the idle one-shots cut into a step. `force_static` handles this for the ridden donkey.
- Whether the `turn_left` frames play on a facing flip.

## As built: details the design above did not say

- **Turning around** keeps the body where it is, and the root moves to the body's other side. The body is drawn about
  half a unit to the facing side of the root, so flipping in place would make it jump about a unit.
  - The facing flips in one frame and the root moves in the next. The animator shows a new facing only on its next
    update, so moving both in the same frame would draw the old facing at the new root for a frame.
  - With the dev panel on (`Debug.TuningWindow`), each turn logs `[shuffle] ... turns` and then three frames of the
    body's drawn position, so this can be checked.
- **The body offset** is measured from the view (`bodySprite` against the root), not assumed. No step is taken until
  it has been measured.
- **Ground:** six samples along both the root's and the body's line.
  - Ground from `GroundHeight` (the navmesh, else the ground raycast).
  - Nothing of the keeper-blocking layer 8 in a capsule of radius 0.25 at each sample. This is the game's own test
    for where the keeper can stand (`PlayerColliderTester`).
  - **First build (2026-09-30), found in game:** it required navmesh ground and never stepped. The log said "ground
    120" rejected. The NPC navmesh doesn't cover everywhere the keeper walks.
  - The rise between two samples is at most 0.45, and 0.6 over the whole step. The first build had 0.12 and 0.3, and
    foals got stuck on a corpse's walkable top, 0.4 above the ground (found 2026-09-30).
- **Spacing:** no step ends within 1.1 of another donkey or cart, or within `ShuffleKeeperDistance` of the keeper.
- **Pause:** when the game's update manager is paused (menus), the donkey holds.
- **Being moved:** anything that moves the donkey (`MoveTo`, `ComeToKeeper`, a dismount, a hitch) is noticed because
  its `Position` differs from what the shuffle last wrote. Its home is then taken there again.
- **Walk speed:** `walk_speed` is set to `ShuffleSpeed × ShuffleAnimSpeed` during a step and back to 1.5 after.
- **No hoofstep sounds** during a step: `MountSounds` belongs to the ridden donkey.

## In-game test list

**Log lines** (`[shuffle]`, always on):
- Once per donkey: `... shuffles around <home> ...`, with the measured body offset and whether the scene has a
  navmesh.
- At most once a minute: `... found no step ...`, with how many tries each check rejected and the first ground
  failure (no ground, a rise, or which collider was in the way).

**Panel:**
- On the Instances tab, each donkey shows an "Idle shuffle:" line: off and why, holding and why, the next step, or
  stepping. It also shows how far it is from home, the steps taken, and the last failed try.
- While that tab is open, or while a donkey is being edited, every donkey and cart in the scene shows its number
  over it (magenta: the one being edited, `~` while stepping).
- A line under the tab bar says whose settings the rows change.
- With the dev panel on, each turn and three frames of the body's drawn position after it.

1. **Off by default:** without the showcase (or with its `Behaviour.IdleShuffle` off), a standing donkey stands still
   as before.
2. **With the showcase:**
   - Call your donkey (H), get off and walk more than 2 units away.
   - Within about 5–15 s it takes a slow step sideways, sometimes a little up or down, never straight up or down.
   - Then it pauses and steps again.
3. **Stays home:** watch it for a minute. It stays within about 1 unit sideways and 0.5 up/down of where you left it.
4. **Turning:** now and then it turns around first. The body shouldn't jump sideways when it turns; send the
   `[shuffle]` log lines if it does.
5. **Walk cycle:** the walk animation at a slow step doesn't look like a stutter. If it does, try the panel's "Walk
   animation rate" above 1.
6. **Keeper close:** walk up to it and it stops. Its E prompt stays put, and riding it works as before.
7. **With a cart:** take the cart (G, or hitch) and get off. A donkey with its cart never moves, and a free cart
   never moves.
8. **Unhitch:** leave its cart standing (H while in the cart) and get off. The donkey shuffles again, the cart
   doesn't.
9. **Ground:**
   - A donkey left next to a fence, building or water edge doesn't step into it.
   - On a slope it stays on the ground.
10. **Several donkeys** (panel: spawn two next to each other) don't step into each other, and don't step in time.
11. **Per donkey:** with a donkey picked in the panel ("Edit its settings"), switching Idle shuffle off stops just that
    one.
12. **Pause menu:** a donkey mid-step holds while the game is paused.
