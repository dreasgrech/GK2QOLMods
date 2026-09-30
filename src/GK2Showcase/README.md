# GK2 Showcase (placeholder name)

Ride the donkey in Graveyard Keeper 2, or drive its cart. This is the playable mod built on
[GK2 Mounts](../GK2Mounts/README.md), which it needs: install both DLLs, `GK2Mounts.dll` and `GK2Showcase.dll`.
It uses nothing but the public GK2 Mounts API ([`docs/mounts-api.md`](../../docs/mounts-api.md)), so it's also the
reference example for mod authors.

Version 0.1.0, for GK2 Mounts 0.1.0 (API 0.7.0 or newer) and Steam game build 25601286.

## Playing

| Key | On foot | Riding the donkey | In the cart |
|---|---|---|---|
| **H** | your donkey comes and you ride it | get off | leave the cart standing, ride on |
| **G** | your donkey comes with its cart and you drive | the cart comes and is hitched | get off |
| **E** (the game's interact key) | next to a standing donkey: ride it | next to a free cart: take it | set the top item down |
| Send-away key (none by default) | your donkey and its cart go | the same (you get off first) | the same |
| Bell key (none by default) | – | ring the bell | ring the bell |

- **Your donkey stays in the world.** Getting off leaves it where you got off, with its cart and whatever is on the
  cart. Walk up to it and press E to ride it again, or press H or G anywhere to call it: it comes to you, into your
  area, and you get on.
- **Its cart is yours too.** H in the cart leaves the cart standing where it is, cargo and all. G brings it back and
  hitches it.
- **Sending it away** sets the cart's cargo down on the ground first, so nothing is lost. The next H or G brings a
  new donkey.
- **Not saved:** after loading a game you get a new donkey on the first H or G. Any cargo that was on the cart lies on
  the ground where the cart stood.

- **You're faster while mounted.** The keeper always faces left or right, like the donkey.
- **The cart carries two items.** Anything you pick up goes onto it. Press the interact key with nothing in front of
  you to set the top item down. On the donkey alone, your hands stay empty.
- **Cargo stays on the cart** when you get off. Around each save it is set down for a moment so the save has it.
- **You get off automatically** for work, ladders, building, fishing, fighting and sleep, and when entering a
  building. Chests and the inventory keep you mounted.

## Settings (`BepInEx\config\dreas.gk2.showcase.cfg`, or the game's Mods menu)

With [GK2 Mod Framework](https://www.nexusmods.com/graveyardkeeper2/mods/42) installed (optional), all of these are
in the game's **Mods** menu and apply at once. Otherwise edit the cfg with the game closed.

| Key | Default | Meaning |
|---|---|---|
| `Keys.RideKey` | H | Ride the donkey / get off / switch from the cart. Combinations such as Shift+H work. |
| `Keys.CartKey` | G | Take the cart / get off / switch from riding. |
| `Keys.BellKey` | none | Ring the bell while mounted. |
| `Keys.SendAwayKey` | none | Send your donkey and its cart away (their cargo is set down first). |
| `Behaviour.DismountIndoors` | true | Get off when entering a building, church or dungeon, and don't mount inside. The cart would cut through furniture. |
| `Behaviour.StayMountedThroughDoors` | false | Keep riding through a door that loads another area. A house door isn't one. |
| `Behaviour.ShowMessages` | true | The keeper says in a thought bubble why he can't mount. |
| `Behaviour.IdleShuffle` | true | Your donkey, left standing without its cart, takes a few slow steps now and then instead of standing perfectly still. |

**Where the other settings are:**
- The look and sound values (speed, seat, lantern, volumes) are GK2 Mounts' settings, in `dreas.gk2.mounts.cfg`.
- GK2 Mounts has a dev panel for tuning them live: `Debug.TuningWindow = true` in that cfg.
- The behaviour switches above are handed to GK2 Mounts for the session, so the dev panel shows them locked.

## How it's built

- **One file,** `Plugin.cs`.
- **Its donkey and cart are instances in the world,** all through the API:
  - **Spawning:** `Mounts.SpawnDonkey` and `Mounts.SpawnCart`, the first time they're needed.
  - **Calling:** `MountHandle.ComeToKeeper`, then waiting for `IsReady` before `Ride`.
  - **Carts:** `Mounts.Hitch`, `Mounts.Unhitch` and `Mounts.UnhitchRidden`.
  - **Getting off:** `Mounts.Dismount` (the donkey stays).
  - **Sending away:** `SetDownCargo` then `Remove`.
  - **Tracking its own handles:** the events `InstanceRemoved`, `Hitched` and `Unhitched`.
  - `Mounts.TryMount` (the summon that vanishes at dismount) is no longer used. Other mods may still use it; the
    showcase's keys then switch that ride's style the old way.
- **Bell:** `Mounts.RingBell`.
- **Behaviour switches:** `Mounts.Behaviour.Set(...)` at startup and on every change.
- **The Mods menu:** `FrameworkLink.cs`, optional. Without the framework the same DLL works, and
  `tests/FrameworkScan` checks that.
- **Checks:** it declares a hard BepInEx dependency on `dreas.gk2.mounts` and checks `Mounts.ApiVersion` at
  startup.

## Tested in game (2026-09-30)

- **Keys:** H and G rode, switched style both ways and got off, with every ride owned by `dreas.gk2.showcase`.
- **Indoors:** a building door dismounted.
- **Without the showcase:** GK2 Mounts alone did nothing on any key.
- **Mods menu:**
  - the entry registered as Compatible, with Status Active;
  - a bell key set there (B) rang the bell;
  - unticking "Dismount indoors" let the donkey into the house, and the dev panel showed that row locked, Off.
