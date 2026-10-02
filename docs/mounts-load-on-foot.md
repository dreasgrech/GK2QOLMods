# Loading a standing cart on foot (investigation, 2026-10-01)

The wish: walk up to a standing cart holding something on the head, see "[E] Load on cart", press E, and the item
moves from the head onto the cart. The keeper may hold several items (a perk).

Today a standing cart takes nothing by hand (docs/mounts-instances.md, "Known limits"): only the ridden cart takes
pickups, through the head reroute (HeadReroute.cs).

## What the game does

**Several items on the head** (decompiled `PlayerData`, Steam build 25601286):
- The head is a list, `overheadItems`. The top item is `overheadItem`, the last one in the list.
- The limit is `OverheadStackLimit = 1 + extra_overhead`. `extra_overhead` is a game resource set by the perk
  `perk_extra_overhead_1` (from the talent `talent_extra_overhead_1`), or by the script functions
  `IncreaseOverheadStackLimit` / `SetOverheadStackLimit`. In the user's save it is 0 (limit 1, GK2-head-facts.txt).
- `TryGetOverheadItem(match, out item)` walks the list from the top down and returns the first item that matches.
- `InsertOverheadItemTo(wgoData, item)` adds the item to that object's inventory, takes it off the head by GUID,
  refreshes the drawn stack, and fires the `PlayerInsertOverheadToWgoAnItem` trigger. That trigger carries only
  the item id, not the target. Its listeners come from the game data (quests register them), so the code doesn't
  show whether any listen. The ridden cart's load at mount already fires it. To stay clear of it, the mod can do the
  same two steps itself (add to the cart, then `RemoveOverheadItem`) without the trigger. Then a quest step like
  "put the body on X" can't be completed by putting it on the cart instead.

**The game's own "put on" target is the town pallet** (`TownPaletteInteractionHandler`):
- **The hint** is "[E] Put" (`hint_put`), shown only when some held item fits.
- **The item** is the topmost held item that the pallet's inventory can take (`TryGetOverheadItem` with
  `CanAddItemToInventory`).
- **Several items:** one per press.

**The interact key while holding something** (`PlayerInputHandler.UpdateInput`), in this order:
1. A big drop in front with an interaction (picking it up too, when there is a free head slot).
2. A world object in front with an interaction.
3. Otherwise `DropOverheadItem()`, which sets the top item down in front of the keeper.

The mod's interact patch (`InstanceControls.TryInteract`) already runs before step 3. It steps aside when the game
has a target of its own (steps 1 and 2).

## What the mod already has

- **A standing cart keeps its cargo.** A registered instance's cargo stays on its cart when the keeper walks away. It
  is saved through the side file and the save-time lend (docs/mounts-instances.md, tests 4, 10 and 14). Only the
  showcase's summoned instance (ephemeral) hands its cargo back at dismount.
- **A standing cart's cargo rig is attached** whenever its view is built in Cart style (`Mount.cs`,
  `CargoRig.Attach`). An item added to the cart's inventory is drawn by the packer, and the `CargoLoaded` API event
  fires, through the existing `OnChanged` → `SyncMirror` → `Announce` path.
- **Moving items from the head to the cart:** `Rig.LoadFromHead` (used at mount) already does it with the game's own
  `InsertOverheadItemTo`. It skips zombies (`Cargo.Allowed`) and checks the cart's room with
  `CanAddItemToInventory`. The cart holds 2 items (`npc_donkey.inventorySize = 2`).
- **Prompts:** `InstanceControls` already finds the nearest standing instance in reach, shows the game's own hint
  bubble ("[E] Ride", "[E] Drive", "[E] Hitch the cart") and takes the key in `TryInteract`. The on-foot branch
  today only looks when the hands are empty, for Ride/Drive.
- **The thump:** `MountSounds.CargoLanded()` is played when an item lands in the cart.

## Proposed design

- **When:**
  - the keeper is on foot and holds at least one item the cart accepts (not a zombie);
  - a standing cart is in reach: a donkey with its cart, or a free cart, registered (not the summoned one), ready,
    in the keeper's scene;
  - the game has no target of its own in front.
- **Reach:** measured to the cart body (`CartPoint`), not the donkey (`BodyPoint`), about 1.3 like the others. So
  near a donkey-with-cart the keeper stands at the cart to load it, and at the donkey to ride it. The two never clash
  anyway: Ride needs empty hands, Load needs something held.
- **The hint:** "[E] Load on cart", over the cart (`HintPoint`).
- **The key:** the topmost held item that fits goes onto the cart (added to the cart, then taken off the head,
  without the game's insert trigger), with the thump. One item per press, as on the town pallet. With the perk, the next press loads the next item, until the
  hands are empty or the cart is full.
- **A full cart:** see question 2.
- **API:** a public `MountHandle.LoadFromHead(...)` on the instance does the work and returns a `MountResult`
  ("The cart is full.", "Nothing the cart takes.", Cargo switched off by a rule). The key just calls it, so other
  mods get the same thing. This adds API surface (minor version bump, 0.12.0).
- **Mod rules:** `MountFeature.Cargo` switched off by a rule hides the hint, like the ridden cart's pickups.
- **Testing the perk without it:** a dev-panel button that sets `extra_overhead` (the game's own
  `SetOverheadStackLimit`), so two or three items can be held for the test, and a button that sets it back.

## Status (2026-10-02)

Built, untested in game (API 0.12.0):
- **The areas** (`InstanceControls.ZoneAt`), measured from the cart body's drawn box (`Mount.Instance.TryCartBox`):
  the cart's from its front edge to 0.8 behind its back end, the donkey's from 1.2 ahead of its body to the cart's,
  both the cart body's width plus 0.6 each side (`General.CartZone*`, `DonkeyZoneAhead`). The first rule, the nearer
  of the donkey's body and the back end, split the cart in the middle: measured 1.40 apart, the cart's front edge
  still meant Drive (the user's screenshot).
- **The key**, on foot: holding something the cart takes, anywhere in either area, "Load on cart" (one item per
  press) or "Cart is full"; empty hands in the donkey's area "Drive", in the cart's area with cargo "Take from cart"
  (not within 0.5 s of a load). A cart-less donkey keeps "Ride" within 1.3 of its body.
- **API:** `MountHandle.LoadFromHead`, `TakeToHead`.
- **Fixed on the way:** the controls stepped aside for any game object in front of the keeper; now only for one the
  game's own key would use (an interaction or a script event), as `PlayerInputHandler.UpdateInput` decides.
- **Testing aids** on the Instances tab: the areas drawn on the ground, the live readout, and the head limit (+1 /
  back to 1; the game resource `extra_overhead`, saved with the game).

## Decided

1. **Several items:** one per press (the user, 2026-10-01).
2. **A full cart:** "Cart is full", and the key stays the mod's (choice A below; the user, 2026-10-01).
3. **Take and Ride/Drive at one cart:** by the nearer point, after measuring the zones (the user, 2026-10-01). The
   measurement is on the Instances tab ("Standing cart zones (measuring)"), and each cart's numbers are logged once as
   `[zones]`.

## A full cart: what each choice does (investigated 2026-10-01)

**How the key falls through today** (`PlayerInputHandler.UpdateInput`, the free state; the planting state has its own
copy, `UpdateInputInteractionOnly`, which the mod doesn't patch):
- **A big drop in front** always takes the key: `BigDropInteractionHandler.HasInteraction` is always true, and its
  hint is empty. With full hands, `AddOverheadItem` sets the top held item down first, so it is a swap.
- **A world object in front** takes it when it has an interaction.
- **Otherwise** the top held item is set down in front of the keeper.

**Choice A, the key stays the mod's ("Cart is full"):**
- **No drop next to a full cart.** To set something down there, the keeper takes a step away from the cart.
- **The label is up whenever you carry something past a full cart.** It's noise, but it says why nothing happens.
- **Nothing else changes:** a drop or a game object in front still wins, as now.

**Choice B, no label, the game drops it:**
- **The cart has no collider.** The game's drop-spot search (0.75 to 1.95 in front, avoiding walls, water and other
  drops) doesn't see it, so the item can land under or inside the cart's mesh. It then looks like cargo but isn't, and
  it's half hidden.
- **The dropped item then sits in front of the keeper.** As a big drop it takes the key, so the next press swaps it
  back into the hands, never onto the cart.
- **A double tap** while loading the second of two items onto a cart with one free slot loads one and drops the next
  one into the cart's mesh.

**Recommendation:** A. A press next to the cart never puts an item where it looks loaded but isn't.

**Found on the way** (true today too): `GameHasTarget` steps aside for any game object in front, even one with no
interaction. In that case the game itself would drop the item, so the mod should step aside only when that object
has an interaction, as the game's own check does. Otherwise a fence post next to the cart would turn E into a drop.

## Taking items off a standing cart, and Ride/Drive at the same cart

**What decides it:** the keeper's distance to two points:
- the donkey's body (`BodyPoint`, already used for Ride/Drive);
- the cart's back end (the tailboard).

Whichever of the two is nearer, within reach, wins the key. The nearer point also moves the label: over the donkey
for "Ride"/"Drive", over the cart's back end for "Take from cart".

**The rules, in order:**

| Holding | Near | Label | E |
|---|---|---|---|
| something the cart takes | anywhere around the cart | Load on cart | the topmost item that fits onto the cart |
| something, cart full | anywhere around the cart | Cart is full | nothing (says it) |
| nothing | the donkey (nearer than the back end) | Ride / Drive | as now |
| nothing, cart has cargo | the back end (nearer than the donkey) | Take from cart | the top cart item to the head |
| nothing, cart empty | the back end | (none) | (the game's) |

- **A free cart:** it has no donkey, so only the back end counts.
- **The perk:** taking is offered with empty hands only. Holding one item with a free slot means Load, so one press
  is never ambiguous.

**What it needs:**
- **The back end of a standing cart.** `TryCartRear` (the set-down behind the cart) works only while riding in Cart
  style. Its measuring of the cart body's drawn box doesn't depend on riding, so a standing version is the same code
  without that condition.
- **Taking:** the reverse of loading. The top cart item comes off the cart inventory, then `TryAddOverheadItemNoReplace`
  puts it on the head, with the game's own pickup sound. That's the same pair of calls the dismount hand-back uses
  (`Rig.Unload`). As an API method: `MountHandle.TakeToHead()`.

**What has to be measured first:** how far the cart's back end is behind the donkey's body on a standing cart.
- **Unmeasured so far.** The ride's set-down logs it ("cart body ends X behind the donkey's body"), but no log on
  disk has that line now. The bed is 0.9 long.
- **The zones need it.** With reach 1.3 around each point, the two zones overlap where the points are close. The
  nearer-point rule still always picks one, but if the back end is only about 1.5 behind the body, the switch is
  half a step either way and easy to cross without meaning to.
- **The measurement:** a line on the Instances tab's Details row for a cart ("back end X behind the donkey") and a
  live readout of which zone the keeper is in. The zones are then tuned from real numbers, not guessed.

**Side effects to keep in mind:**
- **Load, then take back:** right after the last item is loaded at the back end, the label turns to "Take from cart"
  and a quick second press takes it back. A short pause after a load (about half a second) before Take is offered
  avoids this.
- **Drive vs Take on a donkey with its cart** is the main risk of misreading the zone, hence the measurement first.

## Risks to check in game

- **The drawn stack on the keeper** after one of several items leaves the head (the game refreshes it, so it should
  be correct).
- **The cart's look in the sleep levels:** a cart in reach is always at the active level, so the view is there.
- **Saving right after loading** a standing cart: the cargo should be on the cart after waking and after a reload
  (the existing lend path).
