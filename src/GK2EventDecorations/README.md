# GK2 Event Decorations

`GK2EventDecorations.dll` · GUID `dreas.gk2.eventdecorations` · config `BepInEx\config\dreas.gk2.eventdecorations.cfg`

Choose when and how the game's temporary events show. The game turns them on by the PC's date: Halloween from
24 October to 7 November. With this mod each event has a mode:

| Mode | What happens |
|---|---|
| `Auto` (default) | The game's own dates, or your own (`Start` / `End`, e.g. `10-01` to `11-30`). |
| `On` | The event every day of the year. |
| `Off` | Never, even on its dates. |

Halloween is three things in the game, each of which can also be set on its own (`Event` = follow the mode,
`On`, `Off`):

- **Decorations**: pumpkins, garlands, scarecrows, pies, candles and lanterns, bats and glowing eyes, in the village,
  the port, the dungeons, your grounds and most interiors. Each **area** and each **kind** can be switched off, the
  **glow** too, and they can be made **walk-through**.
- **The ghost** that walks through the village at night.
- **The Halloween zombie head** some zombies get.

- **Changes apply at once.** The decorations go up or down as you change a setting (`SwitchWithoutReload`; off: from
  the next load). Areas, kinds, glow and walk-through always apply at once. The ghost follows at its next step or
  evening; zombie pictures already drawn change when the game redraws them.
- **Nothing is written into your save.** The decorations are scenery the game adds at load, not saved objects; the
  ghost is in every save from game 1.011 on, with or without the event. Removing the mod returns every event to its
  dates.
- **The ghost goes home.** When it's off, a ghost left standing in the village is sent home (`SendGhostHome`). The
  game itself leaves it there when the save was made during its walk home in the morning.
- **Later events.** If a game update adds another event (game 1.012 already holds Christmas and Easter food items, not obtainable yet), the
  mod finds it and gives it its own `Mode`, `Start` and `End`.
- Optional: with [GK2 Mod Framework](https://www.nexusmods.com/graveyardkeeper2/mods/42) installed, every setting
  also appears in the game's Mods menu, with a "Today" line saying what is on. The same DLL works without it.

## Settings

```
[General]
Enabled = true               # off: every event follows the game's dates

[Halloween]
Mode = Auto                  # Auto / On / Off
Start =                      # Auto only: own dates as month-day, e.g. 10-01 (empty: the game's 10-24)
End =                        #                                    e.g. 11-30 (empty: the game's 11-07); may wrap the new year
Decorations = Event          # Event / On / Off
Ghost = Event
ZombieHead = Event
SwitchWithoutReload = true   # put up / take down the decorations at once
SendGhostHome = true

[Halloween Areas]            # true / false each
Village, Port, PlayerZone (your grounds), PlayerInterior (your house), TavernInterior, RestaurantInterior,
Barracks, BarracksInterior, AstrologerInterior, WarehouseInterior, Dungeons

[Halloween Decorations]      # true / false each
Pumpkins, Garlands, Scarecrows, Pies, Plants, OtherProps, Bats (bats and glowing eyes)
CandleGlow = true            # off: unlit
BlockTheWay = true           # off: walk through them

[Safety]                     # the dependency check, as in every mod of this repo
StopOnChangedGameCode = false
```

## How it works

The game asks `TemporaryEventDates.IsHalloweenEvent()` whenever it needs to know whether Halloween is on. The mod
answers on three paths: the decorations' condition (`HalloweenConditionalLoadContentPart.ShouldLoad`, asked while a
save loads), the zombie head table (`HalloweenHeadSkinOverride.Resolve`), and the gate itself for what is left, the
event's scripts (the ghost's two daily triggers and its walking script). `Auto` asks the game's own date check, or the
mod's dates. The decorations are one scenery content part the game keeps loaded; taking them down and putting them up
is the game's own `GameSceneConfig.TryUnloadSceneDataContent` / `TryLoadSceneDataContentByName`, and areas, kinds,
glow and walk-through switch its groups, items, `DecorLight` objects and solid colliders. Every other
`Is<Name>Event` method on `TemporaryEventDates` gets a mode and dates by reflection. The investigation, with the
evidence: [`docs/seasonal-events.md`](../../docs/seasonal-events.md).
