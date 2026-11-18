# GK2 Event Decorations

`GK2EventDecorations.dll` · GUID `dreas.gk2.eventdecorations` · config `BepInEx\config\dreas.gk2.eventdecorations.cfg`

Choose when and how the game's temporary events show. The game turns them on by the PC's date: Halloween from
24 October to 7 November. With this mod each event has a mode:

| Mode (window / cfg) | What happens |
|---|---|
| By date / `Auto` (default) | The game's own dates, or your own (`Start` / `End`, e.g. `10-01` to `11-30`). |
| Always / `On` | The event every day of the year. |
| Never / `Off` | Never, even on its dates: the whole event, whatever its parts are set to. |

Halloween is three things in the game. The mode above says when Halloween is on; each of the three then shows With
Halloween (`Event`) or is switched off on its own with Never (`Off`). A part has no "Always" of its own: for
decorations all year without the ghost, set Halloween to Always and the ghost to Never. `Off` as the mode is the whole event off; the options window greys
them then, and greys the areas and kinds whenever the decorations can't show:

- **Decorations**: pumpkins, garlands, scarecrows, pies, candles and lanterns, bats and glowing eyes, in the village,
  the port, the dungeons, your grounds and most interiors. Each **area** and each **kind** can be switched off, the
  **glow** too, and they can be made **walk-through**.
- **The ghost** that walks through the village at night.
- **The Halloween zombie head** some zombies get.

- **Changes apply at once.** The decorations go up or down as you change a setting, and areas, kinds, glow and
  walk-through switch on the spot (villagers' paths follow walk-through at the next load). The ghost follows at its
  next step or evening; zombie pictures already drawn change when the game redraws them.
- **Nothing is written into your save.** The decorations are scenery the game adds at load, not saved objects; the
  ghost is in every save from game 1.011 on, with or without the event. Removing the mod returns every event to its
  dates.
- **The ghost goes home.** When it's off, a ghost left standing in the village is sent home (`SendGhostHome`, cfg
  only). The game itself leaves it there when the save was made during its walk home in the morning.
- **Later events.** If a game update adds another event (game 1.012 already holds Christmas and Easter food items, not
  obtainable yet), this version leaves it to the game: it shows on the game's own dates, as without the mod, and the
  mod names it once in the log. Its options come with a version of the mod written for it, once what the event
  places, starts and saves has been read (as Halloween's was).
- **Settings window.** The game's **Settings** window (main menu or ESC) has a **GK2 Event Decorations** row (press **Open**): an
  options window in the game's own look: **General** ("Mod enabled", a Status line, and an "On today" line saying
  what of each event shows today) and one tab per event, **Halloween** (its mode and own dates, then the headings
  *Parts of the event*, *Areas* and *Kinds of decorations*); descriptions, Defaults, gamepad. The own dates are greyed
  unless the mode is `Auto`, and everything but "Mod enabled" is greyed while that is off. A change applies at once, as from the
  cfg. The window code is shared with GK2 Donkey API and GK2 Tech Tree Search (`lib\GK2DreasUI`, a git submodule
  compiled into this DLL: nothing to install).
- Optional: with [GK2 Mod Framework](https://www.nexusmods.com/graveyardkeeper2/mods/42) installed, the mod is in the
  game's Mods menu too, and its **Settings** button opens the same options window (one set of settings for everyone).
  The same DLL works without it.

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
SendGhostHome = true         # cfg only (no row in the window): false stops sending a stray ghost home
# Areas, true / false each:
Village, Port, PlayerZone (your grounds), PlayerInterior (your house), TavernInterior, RestaurantInterior,
Barracks, BarracksInterior, AstrologerInterior, WarehouseInterior, Dungeons
# Kinds of decorations, true / false each:
Pumpkins, Garlands, Scarecrows, Pies, Plants, OtherProps, Bats (bats and glowing eyes)
CandleGlow = true            # off: unlit
BlockTheWay = true           # off: walk through them

[Safety]                     # the dependency check, as in every mod of this repo; cfg only (no row in the window)
StopOnChangedGameCode = true # this mod: on by default, it stays off after a game update that changed its game code
```

## How it works

The game asks `TemporaryEventDates.IsHalloweenEvent()` whenever it needs to know whether Halloween is on. The mod
answers on three paths: the decorations' condition (`HalloweenConditionalLoadContentPart.ShouldLoad`, asked while a
save loads), the zombie head table (`HalloweenHeadSkinOverride.Resolve`), and the gate itself for what is left, the
event's scripts (the ghost's two daily triggers and its walking script). The game's own `ShouldLoad` and `Resolve` ask
that gate too, so when a part follows the game's dates they run with the gate bypassed: the game's real date check,
never the ghost's setting. `tests/HarmonyHarness-EventDecorations` checks every combination of the settings. `Auto` asks the game's own date check, or the
mod's dates. The decorations are one scenery content part the game keeps loaded; taking them down and putting them up
is the game's own `GameSceneConfig.TryUnloadSceneDataContent` / `TryLoadSceneDataContentByName`, and areas, kinds,
glow and walk-through switch its groups, items, `DecorLight` objects and solid colliders (the log says what is on
after each change: groups, items, glows, blocking colliders). Every other
`Is<Name>Event` method on `TemporaryEventDates` gets a mode and dates by reflection. The investigation, with the
evidence: [`docs/seasonal-events.md`](../../docs/seasonal-events.md).
