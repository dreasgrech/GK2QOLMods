# Showcase: what players get (proposal, 2026-10-02)

The showcase (`src/GK2Showcase`, placeholder name) becomes the mod players install to have a donkey. Everything in it
goes through the GK2 Mounts API. This file says what it includes, what players may change, and what stays fixed.
The full list it picks from is the feature table in [`src/GK2Mounts/README.md`](../src/GK2Mounts/README.md).

**The rule for options:** a player may change *what happens* (keys, on/off, how fast, which colours), never *how it's
drawn or measured* (seat pixels, leg slices, cargo packing, sound ids, slope maths). The second kind is the dev
panel's, and a wrong value there breaks the picture.

## 1. What it is, in one line

**Your own donkey and cart:** call it, ride it, drive it, haul corpses and logs, leave it standing with its load, and
find it where you left it after a reload.

## 2. Always on (no option)

| Area | What the player gets |
|---|---|
| **Your donkey** | One donkey of your own in the world. H calls it and you ride; G calls it with its cart and you drive. |
| **It stays where you leave it** | Getting off leaves it standing, cart and cargo included. Walk up and press E to get on again. |
| **Kept with the save** | Your donkey and cart come back after a reload, where you left them, cargo on the cart. (Already in: `KeepWithSaves`.) |
| **Cart as your hands** | Pickups go onto the cart (2 items); piles, tables, graves and quest hand-ins take from it; E with nothing in front sets the top item down behind the cart. |
| **Loading on foot** | At a standing cart: "[E] Load on cart" and "[E] Take from cart". |
| **Hitch and unhitch** | Leave the cart standing as storage and ride on (J), take it back (J or E at its shafts). |
| **Riding rules** | Faster while mounted; automatic dismount for work, ladders, fishing, fighting, sleep; off at building doors. |
| **The look** | The tuned seat, pose, legs, cart on slopes, cargo on the cart, the lantern hanging from its pole. |
| **Sounds** | Hoofsteps by surface, wheels, cargo thump, grazing, brays, the bell. |
| **Idle life** | Your donkey, standing without its cart, shuffles a few steps now and then. |

## 3. Player options (cfg and the Mods menu)

Kept short: everything a player would look for, nothing that can break the picture.

| Group | Option | Default | Why it's safe |
|---|---|---|---|
| **Keys** | Ride, Cart, Hitch, Bell, Send away | H, G, J, none, none | Only keys. (Hitch moves from the core's cfg to here.) |
| **Riding** | Speed while riding | Normal | A choice of Normal / Faster / Fastest (maps to fixed multipliers, e.g. 1.5 / 1.75 / 2.25 per style), not a free number. |
| | Get off at building doors | on | Off lets the cart into houses, through the furniture. It only looks wrong; nothing breaks (tested 2026-09-30). |
| | Thought bubbles | on | Text only. |
| **Donkey** | Coat | Grey (the game's) | A list of presets: Grey, Brown, Dark, Cream, Black, maybe one silly one. Each preset is a tested set of colour values. |
| | Harness colour | the game's | A short preset list. |
| | Shuffles when standing | on | On/off only. |
| | Brays | on | On/off (some players find them noisy). |
| **Cart** | Paint | the game's | Presets: Natural wood, Dark wood, Red, Green, Blue. |
| | Lantern | on | Show the lantern pole at all. |
| | Lantern lights by day | on | Off: it goes dark by day like the game's hand lantern. |
| | Lantern colour | warm orange | Presets: Warm, White, Green (graveyard), Blue. |
| | Bell when setting off with cargo | on | On/off. |
| **Sound** | Mount sounds volume | 100% | One slider, clamped 0–150%. |
| **Saves** | Keep my donkey with the save | on | Already in. |

## 4. Fixed, not offered

| Not offered | Why |
|---|---|
| Seat, torso, leg and rider pixel offsets, pose and fidget timing | They're tuned to the art; a few pixels off and the keeper floats or sinks into the donkey. |
| Cargo packing (bed depth, gaps, overhang, layout file) | Tuned per item pair; wrong values put logs through the cart. |
| Cart slope style and limits | Only `WholeCartPitch` is the tuned one. |
| Lantern position, tilt, scale, sway | Tuned; the defaults are the good look. |
| Sound ids, pitches, phases | An unknown id plays nothing. |
| Raw colour sliders and swaps | Easy to make a mess; the presets cover what players want. (Still possible by hand in `dreas.gk2.mounts.cfg` for those who look.) |
| Standing-cart areas, idle-shuffle box and speeds, levels, view reuse | Internals. |
| `MountWgoId` | Only the donkey works. |
| The dev panel | Stays in the core for mod authors, off by default. |

## 5. Not yet: needs testing first

| Feature | Before it can go in |
|---|---|
| **Riding through doors that load another area** | Untested. Stays off and hidden until it's tested. |
| **Talking to NPCs while mounted** | Built, untested. It's always on in the core, so it ships either way: test it. |
| **Take from cart** | Built, untested. In section 2: test it. |
| **Carrying zombies** | The cart refuses them; check the message once zombies are unlocked. |
| **The door bug** (2026-10-02) | The donkey parked on the doorstep after "Dismounted (indoors)". Fix before release. |

## 6. Optional extras (decide)

These would make it more of a game, each at a cost:

| Extra | What it adds | Cost |
|---|---|---|
| **A. Unlock it in the story** | Option "Earn your donkey": the donkey after the story's own donkey quest, the cart after a tech. Off by default, so it works at once. | Small (the TechGate example does the tech half). The quest id needs confirming in game. |
| **B. A foal** | Option "Your donkey has a foal": a small donkey that follows yours around. | Small to build; foals are only partly tested. |
| **C. Name your donkey** | The name shown over it ("[E] Ride Bella"). | Needs a text field in the Mods menu or a cfg value; label text change in the core. |
| **D. Spare carts** | More than one cart: leave loaded carts around as storage and swap between them. | Small in code (the API has free carts); needs a way to get the second cart (a craft? a cost?). |
| **E. A second donkey** | A stable of two or more. | Medium: calling with H must pick one. Probably later. |

## Open decisions

1. The option list in section 3: anything to add or drop?
2. Colours as presets only, or also "custom" for those who want it?
3. Which extras from section 6 (A–E)?
4. The name (after the above).
