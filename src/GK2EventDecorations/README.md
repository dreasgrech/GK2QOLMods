# GK2 Event Decorations

`GK2EventDecorations.dll` · GUID `dreas.gk2.eventdecorations` · config `BepInEx\config\dreas.gk2.eventdecorations.cfg`

Choose when the game's temporary events show. The game turns them on by the PC's date: Halloween from 24 October to
7 November (game 1.011). With this mod each event has a mode:

| Mode | What happens |
|---|---|
| `Auto` (default) | The game's own dates, as without the mod. |
| `On` | The event every day of the year. |
| `Off` | Never, even on its dates. |

Halloween covers everything the game ties to it: the decorations (pumpkins, flags, scarecrows, candles and lights,
bats, in the village, the port, the dungeons, your yard and most interiors), the ghost's walks through the village at
night, and the Halloween zombie head.

- **Load your save after a change.** The game places the decorations while a save loads; a new mode shows from the
  next load. (The ghost and the zombie head follow at once, as far as the game redraws them.)
- **Nothing is written into your save.** The decorations are scenery the game adds at load, not saved objects; the
  ghost is in every save from game 1.011 on, with or without the event. Removing the mod returns every event to
  its dates.
- **The ghost goes home.** When the event is off, a ghost left standing in the village is sent home
  (`[Halloween] SendGhostHome`). The game itself leaves it there when the save was made during its walk home in the
  morning.
- **Later events.** If a game update adds another event (the game's code is built for more than one), the mod finds
  it and gives it the same `Auto` / `On` / `Off` row on its own.
- Optional: with [GK2 Mod Framework](https://www.nexusmods.com/graveyardkeeper2/mods/42) installed, the settings also
  appear in the game's Mods menu. The same DLL works without it.

## Settings

```
[General]
Enabled = true          # off: every event follows the game's dates

[Halloween]
Mode = Auto             # Auto / On / Off
SendGhostHome = true

[Safety]                # the dependency check, as in every mod of this repo
StopOnChangedGameCode = false
```

## How it works

The game asks one method, `TemporaryEventDates.IsHalloweenEvent()`, whenever it needs to know whether Halloween is
on: while loading a save (the decorations, `HalloweenConditionalLoadContentPart`), in the event's scripts (the
ghost's two daily triggers and its walking script) and when drawing a zombie head (`HalloweenHeadSkinOverride`).
The mod answers that method (both overloads) for `On` and `Off` and lets the game's date check run for `Auto`.
Every other `Is<Name>Event` method on `TemporaryEventDates` gets the same treatment by reflection. The full
investigation, with the evidence: [`docs/seasonal-events.md`](../../docs/seasonal-events.md).
