# GK2 Showcase (placeholder name)

Ride the donkey in Graveyard Keeper 2, or drive its cart. This is the playable mod built on
[GK2 Mounts](../GK2Mounts/README.md), which it needs: install both DLLs, `GK2Mounts.dll` and `GK2Showcase.dll`.
It uses nothing but the public GK2 Mounts API ([`docs/mounts-api.md`](../../docs/mounts-api.md)), so it's also the
reference example for mod authors.

Version 0.1.0, for GK2 Mounts 0.1.0 (API 0.4.0 or newer) and Steam game build 25601286.

## Playing

| Key | On foot | Riding the donkey | In the cart |
|---|---|---|---|
| **H** | ride the donkey | get off | switch to riding |
| **G** | take the cart | switch to the cart | get off |
| Bell key (none by default) | – | ring the bell | ring the bell |

- **You're faster while mounted.** The keeper always faces left or right, like the donkey.
- **The cart carries two items.** Anything you pick up goes onto it. Press the interact key with nothing in front of
  you to set the top item down. On the donkey alone, your hands stay empty.
- **Cargo comes back to you** when you get off, and before the game saves.
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
| `Behaviour.DismountIndoors` | true | Get off when entering a building, church or dungeon, and don't mount inside. The cart would cut through furniture. |
| `Behaviour.StayMountedThroughDoors` | false | Keep riding through a door that loads another area. A house door isn't one. |
| `Behaviour.ShowMessages` | true | The keeper says in a thought bubble why he can't mount. |

**Where the other settings are:**
- The look and sound values (speed, seat, lantern, volumes) are GK2 Mounts' settings, in `dreas.gk2.mounts.cfg`.
- GK2 Mounts has a dev panel for tuning them live: `Debug.TuningWindow = true` in that cfg.
- The behaviour switches above are handed to GK2 Mounts for the session, so the dev panel shows them locked.

## How it's built

- **One file,** `Plugin.cs`.
- **Keys:** `Mounts.TryMount`, `Mounts.Dismount` and `Mounts.SwitchStyle`, with this mod as the ride's owner.
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
