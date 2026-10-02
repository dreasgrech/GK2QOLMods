# GK2 Mounts: instanced donkeys and carts

Design notes for the instance work started 2026-09-30. The user asked for:

- donkeys that exist in the world, each with its own settings;
- donkeys with their cart;
- carts on their own, which a donkey can then be hitched to;
- any number of each, all spawnable from the API.

They couldn't be asked questions while this was built, so the choices below are defaults made from the game code.
Each one says why.

## What the game offers

- **No cart-only object.** The only world object with the cart is `npc_donkey` (and `npc_donkey_test`, the same
  art). The cart is part of the donkey prefab: the parts under `Base` named `donkey_cart_*`, `wheel_left`,
  `wheel_right` and `light_sway`.
  - **A free cart** is therefore the donkey prefab with the donkey's own renderers kept from rendering
    (`forceRenderingOff`, as the reveal already does). Those renderers are `gfx/bdy`, `gfx/hed`,
    `gfx (90 rotated)/-bdy`, `Cylinder Shadowcaster` (the donkey's shadow) and `DeformTextureSprite` (the grass it
    bends). The cart meshes, wheel sprites and lamp rig stay.
  - The shafts are lowered until their harness end touches the ground.
- **Nothing in the game's save.** Every donkey the mod shows is a temp `WgoData` (`isTempObject`), and none of them
  is ever in the game's save. Instances with `Persist` are kept in a side file instead (docs/mounts-persistence.md).
- **No walking on its own.** A standing donkey is posed by the mod (`Data.Position`, `MovableDirection`,
  `SetStateToAnimator(Idle)`); it never goes through the game's movement system. "A donkey approaches the cart"
  therefore means the player rides it there. No path-finding was built. (Since 0.7.0 a donkey without a cart can take
  a few slow steps on its own, the idle shuffle: [mounts-roaming.md](mounts-roaming.md). It is moved by the mod too,
  not by the game's movement system.)

## Model

`MountInstance` is one donkey view: the former static body of `Mount`, with all its per-donkey state.
- **Rigs:** it owns one of each per-donkey rig: cargo (`Cargo.Rig`), lantern (`Lantern.Rig`), hanging lamp
  (`LampHang.Rig`) and colours (`Colors.Rig`).
- **Flags:** `HasDonkey` and `HasCart`. Donkey = donkey only; donkey with cart = both; free cart = cart only.
- **Kinds:** registered instances (spawned through the API or the panel) stay in the world. The old summon (the
  showcase's H and G, `Mounts.TryMount`) is an ephemeral instance: made at mount, gone at dismount, with its cargo
  handed back as before.
- **The bound instance:** the static classes (`Mount`, `Cargo`, `Lantern`, `LampHang`, `Colors`) are fronts that
  forward to it. It is the ridden one, and while the mod processes a parked one (its frame update, spawn, destroy),
  that one. Everything the game calls into (pickups, the interact key, the pose patches) happens while the ridden
  one is bound.
- **Rider state:** seat, speed, pose, legs and facing are part of the instance, used only while it is ridden. There
  is only ever one rider (co-op is refused as before).

## Settings per instance

- **Where overrides live:** each instance has overrides by key name, for any tuning, colour or behaviour key (the
  same keys as `Mounts.Tuning`, `Mounts.Colors`, `Mounts.Behaviour`).
- **Which value wins:** `Tuned<T>.Value` is first the bound instance's override, then another mod's session
  override (`MountTuning.Set`), then the cfg. Every `S.X.Value` in the code stays as it is.
- **Donkey keys and cart keys:** a key belongs to the cart when it starts with `Cart`, `Cargo` or `Lantern`, or is
  a colour key of a Cart or Lantern region, or is `GlowColor`, `LitGlassTint` or `CharacterLightColor`. Every other
  key belongs to the donkey.
  - **Hitching:** the cart's keys move with the cart into the donkey instance.
  - **Unhitching:** they move out again into the new free cart.
- **Style:** an instance with a cart reads the Cart style's settings, one without reads the Ride style's. The
  override is by name, so "this donkey's SpeedMultiplier" applies in either style.

## In the game

- **Riding a standing donkey:** press the interact key near it, with nothing else to interact with and empty hands.
  A "[E] Ride" or "[E] Drive" label shows above the nearest one in reach.
  - The keeper is put on its seat (`PlayerController.SetPosition`), so the donkey doesn't jump.
  - Its view is taken over as it is, so the cargo, colours and lantern stay.
- **Dismounting an instance** (any dismount: the showcase's key, `Mounts.Dismount`, a rule, indoors): the donkey
  stays where it is, with its cart and cargo. Nothing is handed back to the keeper.
- **Hitching:** riding a donkey without a cart, press the hitch key (General.HitchKey, J) or the interact key near
  a free cart's shafts. The cart, its cargo and its settings join the donkey, and the ride goes on in Cart style.
- **Unhitching:** riding a donkey with its cart, press the hitch key. The cart stays where it stands, as a free
  cart with its cargo and settings, and the ride goes on in Ride style.
- **Pickups** go onto the ridden cart only, as before. A standing cart is loaded and unloaded on foot with the interact
  key instead (docs/mounts-load-on-foot.md).

## Scenes and saves

- **Scenes:** an instance lives in one scene (`SceneId`).
  - Its view is spawned whenever that scene is loaded, and destroyed by the game when the scene unloads (the
    `Wgo.OnDestroy` prefix restores colours and renderers first).
  - The instance, its settings and its cargo list are kept in memory, and the view comes back when the scene does.
  - The ridden instance goes through doors as before (StayMountedThroughDoors) and moves to the new scene.
- **Saves:**
  - **Registered instances:** right before the game writes a save (`SaveSystem.Save`), each one's cargo is added as
    ground drops at its cart, in its scene, and so is written into the save. Right after the save, those drops are
    removed again and the items stay on the cart. So a reload never loses cargo: the items lie where the cart
    stood.
  - **Scene not loaded:** an instance whose scene isn't loaded lends its cargo to that scene's drop queue, and it
    is taken back from there the same way.
  - **The ephemeral ride:** as before (hands, then the ground).
- **Game restart or load:** the instances themselves aren't saved. Instances spawned with `Persist`
  (API 0.9.0; the panel's spawns are) come back when that save is loaded, with their cargo and own settings, from a
  side file next to the game's saves. See docs/mounts-persistence.md.

## API (0.6.0)

- `Mounts.SpawnDonkey(ownerId, position, withCart, facingRight)` and `Mounts.SpawnCart(ownerId, position,
  facingRight)` return a `MountHandle`. They spawn in the keeper's current scene.
- `Mounts.Instances` lists every live one.
- `MountHandle`:
  - identity and state: `Id`, `Owner`, `HasDonkey`, `HasCart`, `IsRidden`, `SceneId`, `Position`, `FacingRight`,
    `CargoIds`;
  - settings: `Tuning` (`Get` / `Set` / `Clear` / `Reset` by key, like `MountTuning`);
  - actions: `Ride()`, `Remove()`, `MoveTo(position)`.
- `Mounts.Hitch(donkey, cart)` and `Mounts.Unhitch(donkey)`.
  - A parked donkey is hitched where it stands, and the cart is moved to it.
  - `Unhitch` returns the new free cart.
- `Mounts.Riding` gives the handle of the ridden instance (null for the ephemeral summon).
- Events: `InstanceSpawned`, `InstanceRemoved`, `Hitched`, `Unhitched`.

## Code

- **`MountInstance.cs`:** `Mount.Instance`'s instance half: standing, the free cart's look and its lowered shafts,
  `ReleaseView`, getting on and off, hitching.
- **`Mount.cs`:** the view and ride code, now per instance.
- **`MountRegistry.cs`:** the registry, the binding, the frame loop, spawning, and the save lending.
- **The rigs:** `Cargo.Rig`, `Lantern.Rig`, `LampHang.Rig` and `Colors.Rig` hold each instance's state. The static
  classes of the same names are fronts that forward to the bound instance.
- **`InstanceControls.cs`:** the interact key, the hitch key and the label.
- **`Api/MountInstances.cs`:** `MountHandle` and `InstanceTuning`.
- **`Dev/TuningWindow.Instances.cs`:** the Instances tab, and editing an instance's own settings from every tab.

### Putting the view back before it goes

The game destroys a temp object's parts rather than pooling them (`Wgo.DestroyParts` pools only when
`!Data.isTempObject`, decomp Wgo.cs), so nothing the mod changes can reach the game's own delivery donkey through
the pool. `ReleaseView` still puts everything back before a view goes, whether the mod or the game (a scene
unloading) destroys it: renderers kept from rendering, colours, the posed lantern and split lamp mesh, the cargo
holder (now destroyed), the cart parts' active state and pose, and the colliders, their layer and the navmesh cuts.
It costs nothing and holds if the game ever pools temp objects.

## Test list (in game, with the dev panel on)

1. **Nothing changed for the summon:** H and G (showcase) ride as before, with cargo, lantern, colours, sounds,
   slopes, and the set-down behind the cart. A dismount hands the cargo back as before.
2. **Spawning:** Instances tab: Donkey, Donkey with its cart, Cart on its own. Each appears in front of the keeper,
   facing his way. The donkey idles. The cart stands behind its donkey with the lantern lit. The free cart shows no
   donkey (no body, head, shadow or grass bend), and its shafts rest on the ground (the log says how far they turned).
3. **Riding:** walk up to a standing donkey with empty hands. "[E] Ride" (or "[E] Drive") shows over it. E puts you on
   it without a jump. Riding works as with the summon. The showcase's key (or any dismount) leaves it standing where
   you got off; E rides it again.
4. **Cargo stays:** load the donkey-with-cart, dismount, walk away and back: the cargo is still on it. Ride it again:
   the cargo is still there, and E sets it down behind the cart.
5. **Hitching:** ride a donkey without a cart up to a free cart ("[E] Hitch the cart" over it). E (or J) takes it:
   the cart is behind the donkey, with the free cart's cargo, and the ride goes on in Cart style.
6. **Unhitching:** riding with the cart, J leaves it standing. It becomes a free cart with the cargo, shafts on the
   ground, and you ride on without it. Hitch it again.
7. **Own settings:** Instances tab, "Edit its settings" on one donkey: the banner shows. Change the coat colour
   (Colors tab) and the speed (Riding tab). Only that donkey changes, and its rows are magenta. "Stop" and the other
   donkeys still look like everyone's. Unhitch an edited cart's colours: they stay with the cart.
8. **Several at once:** three or four donkeys and carts, each with other colours. Riding one doesn't change the
   others.
9. **The game's delivery donkey** (Debug tab, start a real delivery) after all of this: its cart, lantern, colours
   and collisions are the game's own.
10. **Save with cargo on a standing cart** (sleep): after waking the cargo is still on the cart. Load that save: the
    items lie on the ground where the cart stood, and the instances are gone. (With `Persist`, the panel's spawns:
    see the test list in docs/mounts-persistence.md.)
11. **Quit to the menu / load another save:** the instances are gone, with no errors in the log.
12. **A door that loads another scene** (with two instances standing outside): walk through and back. The log
    should not say "left the game". If it does, the game has no current scene while one loads, and the wipe needs
    the ride's teleport guard. The instances come back with their scene ("the game removed its view ... it comes
    back with the scene").
13. **The label never shows next to a standing donkey:** the log line "is in reach but in scene ... the keeper in
    ..." means the two scene ids differ in form.
14. **A drop sound at each save** (with cargo on a standing cart) is the cargo being set down for the save and taken
    back (a real drop for a moment). It isn't a fault.

## Not done (first version)

- Standing donkeys make no sounds (no idle brays or grazing). Sounds play for the ridden one only.
- Persistence only for instances with `Persist` (docs/mounts-persistence.md).
