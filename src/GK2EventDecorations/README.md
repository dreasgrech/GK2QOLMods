# GK2 Event Decorations

`GK2EventDecorations.dll` · GUID `dreas.gk2.eventdecorations` · config `BepInEx\config\dreas.gk2.eventdecorations.cfg`

Choose when and how the game's Halloween shows. The game turns its temporary events on by the PC's date, and
Halloween (24 October to 7 November) is the only one it has. With this mod Halloween has a mode:

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

- **Out of season nothing changes until you choose.** The default is By date: installed on any other day, the game
  looks as before. Set Halloween to Always for the decorations today.
- **Changes apply at once.** The decorations go up or down as you change a setting, and areas, kinds, glow and
  walk-through switch on the spot (villagers' paths follow walk-through at the next load). The ghost follows at its
  next step or evening; zombie pictures already drawn change when the game redraws them.
- **The mod adds nothing to your save.** The decorations are scenery the game adds at load, not saved objects, and
  the zombie head is a picture. The ghost is in every save from game 1.011 on, with or without the event, and the
  game saves where it stands, as any villager's place. Removing the mod returns Halloween to the game's dates.
- **The ghost goes home.** When it's off, a ghost left standing in the village is sent home (`SendGhostHome`, cfg
  only; checked when a save has loaded and every ten seconds). The game itself leaves it there when the save was made
  during its walk home in the morning, after its own Halloween too.
- **Before removing the mod**, if Halloween was on outside the game's dates: load your save, set Halloween to Never,
  play on for a minute or two (the ghost ends a walk it is on, then goes home), save. A ghost that was out is home
  then; without the mod nothing sends it home until the next Halloween (it only stands in the village).
- **Later events.** If a game update adds another event (game 1.012 already holds Christmas and Easter food items, not
  obtainable yet), this version leaves it to the game: it shows on the game's own dates, as without the mod, and the
  mod names it once in the log. Its options come with a version of the mod written for it, once what the event
  places, starts and saves has been read (as Halloween's was).
- **Settings window.** The game's **Settings** window (main menu or ESC) has a **GK2 Event Decorations** row (press **Open**): an
  options window in the game's own look: **General** ("Mod enabled", a Status line, and an "On today" line saying
  what of Halloween shows today) and the **Halloween** tab (its mode and own dates, then the headings
  *Parts of the event*, *Areas* and *Kinds of decorations*); descriptions, Defaults, gamepad. The own dates are greyed
  unless the mode is `Auto`, and everything but "Mod enabled" is greyed while that is off. A change applies at once, as from the
  cfg. The window code is shared with GK2 Donkey API and GK2 Tech Tree Search (`lib\GK2DreasUI`, a git submodule
  compiled into this DLL: nothing to install).
- **In the game's language.** The window follows the game's language: English, German, Spanish, French, Brazilian
  Portuguese, Russian, Polish, Turkish, Japanese, Simplified Chinese and Korean (`Languages\<id>.csv` beside the
  DLL).
- Optional: with [GK2 Mod Framework](https://www.nexusmods.com/graveyardkeeper2/mods/42) installed, the mod is in the
  game's Mods menu too, and its **Settings** button opens the same options window (one set of settings for everyone).
  The same DLL works without it.

## Settings

```
[General]
Enabled = true               # off: Halloween follows the game's dates

[Halloween]
Mode = Auto                  # Auto / On / Off
Start =                      # Auto only: own dates as month-day, e.g. 10-01 (empty: the game's 10-24)
End =                        #                                    e.g. 11-30 (empty: the game's 11-07); may wrap the new year
Decorations = Event          # Event (with Halloween) / Off (never), the three parts
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
after each change: groups, items, glows, blocking colliders). Any other
`Is<Name>Event` method on `TemporaryEventDates` (a later game's event) is named once in the log and left alone. The
investigation, with the evidence: [`docs/seasonal-events.md`](../../docs/seasonal-events.md); what is tested and
where: [`docs/event-decorations-tests.md`](../../docs/event-decorations-tests.md).
