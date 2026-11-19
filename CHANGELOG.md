# Changelog

Changes to the mods in this repository, per mod, newest first. Every change is added under **Unreleased** of its mod
when it is made; at a public release that heading becomes the version and date, and the entry is the Nexus changelog
text for that version. Released versions are never reused.

Versions when this changelog was started (2026-10-16): Global Storage 1.1.5, Built Count 1.0.6, Move Building 1.1.6,
Owned Count 1.0.4, Tech Search 1.0.0. Earlier changes are in git history and each mod's README.

## Global Storage

### Unreleased

- Audited against game 1.012.7 (Steam build 26624074, 2026-11-13; `docs/game-update-26624074.md`): nothing the mod uses
  changed.

- Audited against game 1.011.1 (Steam build 26404424, 2026-11-03; `docs/game-update-26404424.md`): nothing the mod uses
  changed. With `IncludeZombieOrderDelivery` on, `[F] Put` at an assistant-worked autocrafter now takes from
  storage only what the station still lacks (the game's change: a delivery puts in only the missing count).

### 1.1.6 (2026-10-30)

- Audited against game 1.011 (Steam build 26297960, 2026-10-30; `docs/game-update-26297960.md`): the one game member
  that changed, `WgoData.GetCraftableMultiInventory` (the zombie assistants' autocrafts), leaves the extension as it
  was; the mod warned and ran without the extension on 1.011 until this rebuild.
- No LINQ (it allocates garbage in a Unity game, 2026-10-25): the storages' order (fuel first, then the ones added,
  then by size) is sorted with a plain comparison, the same order as before; the dependency check at start (shared with every mod here) sorts and looks up without LINQ; the manifest it embeds is unchanged.

## Built Count

### Unreleased

- Audited against game 1.012.7 (Steam build 26624074, 2026-11-13; `docs/game-update-26624074.md`): nothing the mod uses
  changed.

- Audited against game 1.011.1 (Steam build 26404424, 2026-11-03; `docs/game-update-26404424.md`): nothing the mod uses
  changed.
- Audited against game 1.011 (Steam build 26297960, 2026-10-30; `docs/game-update-26297960.md`): nothing the mod uses
  changed; the build desk gained a Move row in every tab.
- No LINQ (it allocates garbage in a Unity game, 2026-10-25): the dependency check at start (shared with every mod here) sorts and looks up without LINQ; the manifest it embeds is unchanged.

## Move Building

### Retired, 2026-10-30

- **Retired** (the user, 2026-10-30): game 1.011 (Steam build 26297960) rewrote the remove pointer (five members the
  mod patched or read are gone) and has a Move row of its own in every build desk tab that relocates the same object,
  contents and references kept, as this mod did (`docs/game-update-26297960.md`). The last build, 1.1.6, stops itself
  on 1.011 with the members named; its DLL is out of `dist/` and the mod out of the build and scan lists; the source
  stays in `src/GK2MoveBuilding/` for history. Players on 1.011: delete `BepInEx\plugins\GK2MoveBuilding.dll` and use
  the game's Move row.
- No LINQ (it allocates garbage in a Unity game, 2026-10-25): the occupied-dock check and the walk through the
  scene's zones by priority are plain loops, the same results; the dependency check at start (shared with every mod here) sorts and looks up without LINQ; the manifest it embeds is unchanged.

## Owned Count

### Unreleased

- Audited against game 1.012.7 (Steam build 26624074, 2026-11-13; `docs/game-update-26624074.md`): the one game member
  the mod patches, `UITooltip.AddItemWidgets`, has new code (the sermon branch's early return became an `if`, and a line
  for the PR machine's items is added at the end). The postfix still fits; the mod runs on it with a warning line at
  start until a build with the new manifest.

- Audited against game 1.011.1 (Steam build 26404424, 2026-11-03; `docs/game-update-26404424.md`): nothing the mod uses
  changed.
- Audited against game 1.011 (Steam build 26297960, 2026-10-30; `docs/game-update-26297960.md`): nothing the mod uses
  changed.
- No LINQ (it allocates garbage in a Unity game, 2026-10-25): the dependency check at start (shared with every mod here) sorts and looks up without LINQ; the manifest it embeds is unchanged.

## Tech Tree Search

### 1.1.0 (2026-11-19)

Built for game 1.012.9 (Steam build 26734308), on Nexus Mods and the Steam Workshop (the same DLL; both now carry a
`Languages\` folder beside it). For players: an options window opened from a row in the game's Settings window, the
mod in the game's 10 other languages, and two fixes for Japanese, Chinese and Korean (the result list's font, whole
names found). Settings carry over: the cfg keeps its sections and keys (`General`, `Layout`, `Safety`, the same
binds as 1.0.2). Uploaded 2026-11-19 (`e4f5724`): the Workshop item 3839100125 updated 00:41, checked through
Steam's public file details (346,843 bytes, the staged folder to the byte; the description the staged one).

- A settings window: the game's Settings window has a "GK2 Tech Tree Search" row under the game's own (a button above
  its OK button until 2026-11-18, never released; under Development below), its "Open" opening an options window in the
  game's look, with the Status line, the search box switch, searching what techs unlock, the number of results, the
  key and the stop-on-changed-game-code switch (descriptions, Defaults, gamepad). The cfg keeps its sections and keys;
  the Layout values stay the cfg's. (2026-11-15; the window code is the same as GK2 Donkey API's, `lib/GK2DreasUI`.)
- Development (2026-11-15): the window code comes from the GK2DreasUI submodule (`lib/GK2DreasUI`, compiled in through
  its `.targets`; `Plugin` fills `UiOwner`, declares the `SettingsPage` over its own binds with `numberSections: false`,
  runs `UiHost` each frame and lists the muffle patch). The DLL grew from about 60 KB to about 258 KB. The embedded
  dependency manifest now holds every game member the window code uses as well (the game's Settings window, LazyUI,
  the sound filter, MainGame): under this mod's one policy a game update that changes any of them is a warning at
  start, or a stop with `Safety.StopOnChangedGameCode`.
- With GK2 Mod Framework installed, the Mods menu's Settings button for this mod opens the mod's own options window
  (2026-11-17, the user's choice; GK2 Saddle Up and Event Decorations do the same). The Mods menu no longer lists the
  mod's settings as rows of its own: they were English only and a second place for the same values. Development:
  `FrameworkLink` registers one button setting ("Options") and points the framework's Settings button at the window
  with a prefix on its private `ModsMenuWindow.OpenSettingsPage(RegisteredMod)`, found by exact signature; without
  the prefix the framework's page shows the one button.
- The window's key row no longer says Ctrl + F "Clashes with GK2 Donkey API: DescendKey (Left Ctrl)" (seen at the
  first open, 2026-11-15): the library takes another mod's key on a modifier as held, and the plugin says it reads
  its key with BepInEx's exact test (`UiOwner.KeysExact`), so only an identical combination elsewhere is a clash.
- The result list uses the game's font for the game's language (2026-11-16). Its rows wore the font of the page's
  point counters, which the game keeps the same in every language (`tiny_font`: no Japanese, Chinese or Korean
  letters), so with the game in one of those three languages the techs' names in the list could not be drawn. Read
  from the game's files (2026-11-15: the counter's text style is marked "always the same font"; every font of the
  page swaps to the game's Japanese, Chinese or Korean font for a label that isn't). Seen in the game 2026-11-17 (the
  user, Japanese, the language switched in the running game with the page alive): eight results for 鍛冶 drawn whole,
  the box's own text too; the log: "the rows wear japanese (has every letter); the page's counter label wears
  tiny_font (LACKS 鍛冶場)". German the same day: `tiny_font`, as before. The
  rows now ask that style's font data for the language's font, again when the language changes; any failure keeps
  the old font. The dot between a result's tab and its unlock is a hyphen where the language's font has no such dot
  (the game's own Japanese and Korean text never uses one). With the developers' marker file, one log line a language
  names both fonts and the letters of the first result's name each can't draw (the font test of GK2 Donkey API's
  self-tests).
- The search finds whole names in Japanese and Simplified Chinese (2026-11-17). In those two languages the game puts
  an invisible break mark (a zero-width space, U+200B) into its texts where a line may break: between the words of a
  name in Japanese, between single characters in Chinese (7,355 and 8,438 of its 9,474 lines; 26 and 32 of the 51
  tech lines; no other language has one, measured in the game's word tables). The search compares letter for letter,
  so a query that spanned a mark found nothing: a whole name typed in Japanese, nearly any two characters in Chinese.
  The marks are dropped from the names, tab names and unlock names the search reads and shows, and from what is
  typed or pasted. To see in the game: 鉄の鍛冶 pasted in Japanese finds its tech.
- The settings window's own words (its buttons, the hints under the rows, the key box, the row in the game's
  Settings window) in the game's 10 other languages (2026-11-16): `Languages\<id>.csv` beside the DLL, the window
  library's own translations (the ones GK2 Saddle Up's window has). `Languages\en.csv` is the template a translator
  copies: every text of the mod, 39 lines.
- The whole mod in the game's 10 other languages (2026-11-17): German, Spanish, French, Brazilian Portuguese,
  Russian, Polish, Turkish, Japanese, Simplified Chinese, Korean. The settings window's own texts (13 lines: two
  tabs, five rows and their descriptions, the title), its Status row, and the search box's own three texts ("Search...", "No visible tech matches", "+3 more:
  type more letters"), which were fixed English before and now follow the game's language, also when it is switched
  in the running game. Each language written by one translator and read by a second (both Claude Opus), against the
  game's own words for the same things (the Tech Tree, techs, tabs, perks, the names in the "beer" example) and the
  window's shipped words; every language uses only characters the game's font for its text holds (below, under
  Development). The statuses' own sentences stay English (they change with the mod's state; decided 2026-11-16).
  Not yet seen in the game.
- Development (2026-11-17): the DLL grants the mods' in-game test plugin its internals by name (`TestAccess.cs`,
  `InternalsVisibleTo("GK2QOLMods.Tests")`; under Development below). Nothing changes for players.
- Development (2026-11-17): the search box's texts are `Word`s of the window library (`SearchWords.cs`:
  `search.placeholder`, `search.no_match`, `search.more`), handed to it with `UiOwner.OwnWords`, so they are in
  `en.csv` and every language file; `tools/l10n.json` gives them the box's room (14, 34 and 34 characters).
- Development (2026-11-16): `src/GK2TechTreeSearch/Languages/` is written by the window library's tools
  (`lib/GK2DreasUI/tools/l10n-sync.py`, `tools/l10n.json`): `en.csv` from the built DLL without the game
  (`tools/EnglishDump`), the window's 21 words in each language from the library's `Languages/`. It deploys beside
  the DLL and ships in the zip and the Workshop folder.

### 1.0.2 (2026-11-13)

Built for game 1.012.7, on Nexus Mods and, for the first time, the Steam Workshop (the same DLL on both: the Workshop
item adds `modinfo.json` for the game's Mods window). For players: the measurement line is gone; nothing else changes.

- Release tooling for the Workshop (2026-11-13): `tools/make-release-zip.py` also stages `workshop/<Mod>/` (not in git)
  for the game's Shift+F11 creator (`modinfo.json`, the plugin folder, `Thumbnail.jpg` from `media/workshop/<Mod>/`,
  `_description.bbcode` from `media/GK2_<Mod>_Workshop.txt`, `_changenote.txt` from `docs/nexus/`); `--no-zip` and
  `--no-workshop`. `tools/workshop-thumbnail.py` and `tools/workshop-gallery.py` cut the item's pictures from a
  screenshot. `tools/check-dist.py` and `tools/release-scans.sh` take a mod list, so one mod releases while another's
  dist waits for its re-audit (Owned Count 1.0.4 against 1.012); `make-release-zip.py` passes its mods on.
- Audited against game 1.012.7 (Steam build 26624074, 2026-11-13; `docs/game-update-26624074.md`): nothing the mod uses
  changed.

- Audited against game 1.011.1 (Steam build 26404424, 2026-11-03; `docs/game-update-26404424.md`): nothing the mod uses
  changed.
- **No more measurement line in the log** (2026-10-30): the first open of the search box in a session wrote a
  `[measure]` line with the page's and the box's rectangles to `BepInEx\LogOutput.log`, a developer measurement from
  2026-10-04 that 1.0.1 shipped ungated. Removed; the box's layout has used fixed defaults since then.
- Audited against game 1.011 (Steam build 26297960, 2026-10-30; `docs/game-update-26297960.md`): nothing the mod uses
  changed.

### 1.0.1 (2026-10-29)

The first version on Nexus Mods (the zip extracts into the game folder: `BepInEx\plugins\GK2TechTreeSearch\`).
Nothing changes for players.

- Renamed from GK2 Tech Search to GK2 Tech Tree Search: the file is `GK2TechTreeSearch.dll`, the ID `dreas.gk2.techtreesearch`.
  Your settings are copied from the old `dreas.gk2.techsearch.cfg` on the first start. If the old `GK2TechSearch.dll` is
  still in `BepInEx\plugins`, this one stays off and the log says so: delete the old file.

- No LINQ (it allocates garbage in a Unity game, 2026-10-25): the dependency check at start (shared with every mod here) sorts and looks up without LINQ; the manifest it embeds is unchanged.

### 1.0.0 (2026-10-04)

- First release, on GitHub (`dist/`): the search box, searching what techs unlock, the jump, the Mods-menu rows with GK2 Mod Framework.

## Event Decorations

### 1.0.0 (2026-11-19)

The first release. Built for game 1.012.9 (Steam build 26734308), for Nexus Mods and the Steam Workshop (the same
DLL, with a `Languages\` folder beside it). What it is:

- Halloween when the player chooses: By date (the game's dates, 24 October to 7 November, or their own first and
  last day), Always or Never. The default is By date, so out of season nothing changes until they pick Always.
- Its three parts each With Halloween or Never: the decorations, the ghost walking at night, the Halloween zombie
  head.
- The decorations by area (11) and by kind (7), the candle and lantern glow, and whether they block the way.
- A change applies at once: the decorations go up or come down without a reload.
- An options window opened from a "GK2 Event Decorations" row in the game's Settings window (General with "Mod
  enabled", Status and "On today"; the Halloween tab), in the game's 11 languages. With GK2 Mod Framework, its
  Settings button opens the same window.
- While Halloween is off, a ghost the game left standing in the village is sent home.
- The mod adds nothing to a save. `Safety.StopOnChangedGameCode` is on by default: after a game update that changed
  game code the mod uses, it stays off until an updated version.
- A later game's event is named once in the log and left to the game.

**How it got there, 2026-11-11 to 2026-11-18.** None of the states below was released. The entries stand as they
were written, the later ones overruling the earlier: the modes were first called Auto / On / Off (the cfg's values
still), the row in the Settings window was a button until 2026-11-18, a part had an "Always" until the same day,
and the mod found and switched other events itself until then.

- New mod: choose when the game's temporary events show. Halloween gets a mode: Auto (the game's dates, 24 October
  to 7 November), On (every day) or Off (never). It covers the decorations, the ghost's night walks and the
  Halloween zombie head. The decorations change from the next save load.
- The options window's own words (its buttons, the hints under the rows, the button in the game's Settings window)
  in the game's 10 other languages (2026-11-16): `Languages\<id>.csv` beside the DLL, the window library's own
  translations. `Languages\en.csv` is the template a translator copies (97 lines). Written by the window library's
  tools, as Tech Tree Search's.
- The whole options window in the game's 10 other languages (2026-11-17): German, Spanish, French, Brazilian
  Portuguese, Russian, Polish, Turkish, Japanese, Simplified Chinese, Korean: every row, choice, heading and
  description (75 lines) and the Status row. Written and proofread as Tech Tree Search's (one translator and one
  second reader a language, the game's own words for its places and things, only characters the language's font
  holds). The "On today" line's value and the statuses stayed English then (decided 2026-11-16; translated
  2026-11-18, below). Not yet seen in the game.
- The Astrologer area's description says "Inside the astrologer's tower." (it said "house"; the game's own name of
  the place is the Astrologer's Tower; found by the translators, 2026-11-17).
- Development (2026-11-17): the DLL grants the mods' in-game test plugin its internals by name (`TestAccess.cs`,
  `InternalsVisibleTo("GK2QOLMods.Tests")`; under Development below), and the decorations' content name is internal
  for it (`Decorations.ContentName`). Nothing changes for players.
- With Halloween off, a ghost the game left standing in the village is sent home.
- An event a later game update adds is left to the game (2026-11-18, the user: "we can't have the mod auto detect
  new events because this is something that we should do"). Until then the mod looked for every other event check
  of the game's at its start and gave each a cfg section, a tab "(new event)" with By date / Always / Never and own
  dates, and a patch on the check: switches for game content nobody had read, which could place or start things
  that end up in a save, and code that had never met a second event (the game has only Halloween). Now the mod
  switches Halloween alone; another event shows on the game's own dates, as without the mod, and is named once in
  the log ("The game has N event(s) this version of the mod doesn't know (...)"). `tests/CecilScan-EventDecorations`
  names a new event at every game update; its options come with a version written for it.
- "On today" and Status in the game's language (2026-11-18, the user's yes; they were English in every language,
  being built in code and not a row's label or description). 18 words of the mod's own, 17 since a part lost its
  Always the same day (`EdWords`, through the
  window library's `UiOwner.OwnWords`): the pieces of the "On today" line (the event with what shows, the list's
  items and how they are joined, "set to Always", the remark about dates that don't count, "the game's own dates")
  and the statuses (Active, Off with the switch's name, not started, and the five sentences of a start that
  failed, each with its number). The names inside the lines are the window's own, so a line names a thing as its
  row does: the event as its tab, "Always" as the part's choice, "Mod enabled" as the switch's label. The remark
  about dates was reworded so it needs no brackets inside the line's own: "your own dates don't count: both are
  needed, as month-day like 10-24, so the game's dates apply". `Languages\en.csv` has 115 lines; the ten languages
  written and proofread as before (one translator and one second reader a language). `Plugin.Status` stays English
  for the log and for the framework's build confirmation.
- Optional GK2 Mod Framework integration: the mod in the game's Mods menu, its Settings button opening the mod's own
  options window (below).
- Own dates for Auto (Start / End, month-day, may wrap the new year), for every event.
- Halloween's decorations, ghost and zombie head can each be switched off on their own: a part is "With Halloween"
  or "Never"; Halloween set to Never turns all three off (greyed in the options window), and the areas and kinds are
  greyed while the decorations can't show.
- A part has no "Always" (taken out 2026-11-18, the user's pick). Until then each part had With Halloween / Always /
  Never, and a part on Always showed while Halloween itself was off: "Halloween: By date" beside decorations up all
  year, and an "On today" line reading "decorations (set to Always)", was taken for a bug twice by the mod's own
  user (2026-11-15 and 2026-11-18). Now Halloween's row alone says when, and a part only whether it takes part.
  Decorations all year without the ghost: Halloween on Always, the ghost on Never. What is no longer possible:
  decorations all year with the ghost keeping to the Halloween dates. The cfg values are `Event` and `Off` (`On` is
  gone; the mod was never released with it). The descriptions of the Halloween row and of the three parts lost
  their sentences about it, in English and in the ten languages (seven lines each, the proofread texts changed as
  little as possible, read by a second reader a language); the "On today" line lost "(set to ...)" and its word.
- Decorations per area (village, port, your grounds, your house, tavern, restaurant, barracks, astrologer, warehouse,
  dungeons) and per kind (pumpkins, garlands, scarecrows, pies, plants, other props, bats and glowing eyes), candle
  glow on/off, and walk-through decorations.
- The decorations go up or down at once when a setting changes.
- An "On today" line says what of each event shows today.
- The window's choices say what they do: an event's mode is By date / Always / Never, a part's With Halloween / Always /
  Never (the cfg keeps Auto / On / Off and Event / On / Off).
- A settings window: the game's Settings window has a "GK2 Event Decorations" button opening an options window in the
  game's look: General ("Mod enabled", Status, "On today") and one tab per event, Halloween (mode, own dates, then the
  headings Parts of the event, Areas, Kinds of decorations); descriptions, Defaults, gamepad. The own dates are greyed
  unless the mode is Auto, every row but "Mod enabled" while it is off. The GK2 Mod
  Framework's Settings button opens this window: its Mods menu keeps no rows of its own. (2026-11-15)
- Settings gone through (2026-11-15): "SwitchWithoutReload" is gone (the decorations always go up or down at once);
  "SendGhostHome" stays in the cfg only, not in the window (a fix for the game's lost walk home, on by default); the
  areas' and kinds' keys moved from [Halloween Areas] / [Halloween Decorations] into [Halloween] (unreleased: nothing
  carried over). Each area and kind checked against the game's decoration data: every group and every item of 1.012
  is under one of them. The log says what is on after each change (groups, items, glows, blocking colliders).
- Fixed: a part following Halloween's dates could take the Ghost setting's answer instead. The game's own code for the
  decorations and the zombie head asks the same gate the ghost's scripts ask, which the mod answers with the Ghost
  setting: with the ghost set to Always, the decorations (at load) and the zombie head showed out of season too, and with
  it set to Never they stayed away in season. Those two now get the game's real date check (2026-11-15).
- The "On today" line says when a part shows only because it is set to Always ("decorations (set to Always)"), and when
  your own dates don't count (only one of the two, or not month-day like 10-24: the game's dates apply).
- After a save loads, the mod checks that the decorations are up or down as the settings say; if not, a warning in the
  log and it puts that right.
- Safety.StopOnChangedGameCode is on by default in this mod and only in the cfg: after a game update that changed game
  code the mod uses, it stays off until an updated version (2026-11-15).
- With "Mod enabled" off, the decorations' areas, kinds, glow and walk-through are as the game made them too
  (they still applied before; 2026-11-15).
- Development (2026-11-15): `tests/HarmonyHarness-EventDecorations`, the truth table of what the game gets over 2430
  combinations of the settings, with the game's own date check as today and forced both ways (in the release scans);
  it fails 45 checks without the gate-bypass fix, in both seasons. The start line
  in the log names the parts that don't follow the event ("Halloween (Auto; decorations On)").
- Development (2026-11-15): the window code comes from the GK2DreasUI submodule (`lib/GK2DreasUI`, compiled in through
  its `.targets`, as Tech Tree Search's); `Plugin` fills `UiOwner`, makes the `SettingsPage` (`numberSections: false`)
  whose rows `OptionsPage` declares over the mod's own binds, runs `UiHost` each frame and lists the muffle patch. The
  embedded manifest grew from 34 to 124 entries (the window's game members). `FrameworkLink` registers one button
  setting and points the framework's Settings button at the window with a prefix on its private
  `ModsMenuWindow.OpenSettingsPage(RegisteredMod)`, found by exact signature (GK2 Donkey API's way); without the prefix
  the framework's page shows the one button.

## Development

- **Event Decorations 1.0.0: the release prep** (2026-11-19, the user: "let's prepare the seasonal mod for release
  ... so we get it done and out"). The texts brought in line with the mod as it is: the mod's README still gave a
  part three values and said other events get a mode by reflection (both taken out 2026-11-18), the main README
  spoke of "each event" and "one tab per event", and the mod's Unreleased was the build's history, contradicting
  itself; it is now the 1.0.0 entry with what the mod is first and the history under it, marked as such. Said in the
  READMEs and on the pages for the first time: out of season nothing changes until Halloween is set to Always (the
  default is By date), and what to do before removing the mod (the ghost's place is saved; without the mod nothing
  sends a ghost standing in the village home). No label or description of the window changed: the eleven language
  files are as committed. New: the page texts (`media/GK2_EventDecorations_Nexus.txt`, `_Workshop.txt`, the Workshop
  one 5,533 bytes, without the Framework) and the two notes (`docs/nexus/event-decorations-1.0.0-*`).
  `build-dist.sh GK2EventDecorations` 0 warnings (the DLL 291,328 bytes, its manifest 133 entries, identical with
  game 1.012.9), `release-scans.sh GK2EventDecorations` 19 of 19 green, `make-release-zip.py GK2EventDecorations`:
  `release/GK2-Event-Decorations-1.0.0.zip` 180,545 bytes (the DLL and 11 language files) and the Workshop folder
  staged (no preview picture yet); `rehearsal-test.sh GK2EventDecorations <the Saddle Up 0.17.3 zip>` passed. The
  mod's 46 tests were last run in the full bench run of 2026-11-18 23:49 (251 tests in 4 suites, 2470 passed, 0
  failed), on the window library as it is at `777d2b2`. The rehearsal the same evening, the user playing (the
  checklist's section 7 has both starts): alone on no cfg without GK2 Mod Framework, then on Always beside the
  Framework, Tech Tree Search 1.1.0 and GK2 Saddle Up 0.17.3 with a save loading while the decorations are up and
  the ghost met at night; no warning or error but one provoked by a mistyped date, the saves identical. The page
  pictures are from it (`media/event-decorations/`, seven and the header; the Workshop preview and seven gallery
  cuts in `media/workshop/GK2EventDecorations/`).
- **Tech Tree Search 1.1.0: the release prep, and a rehearsal script** (2026-11-18). The versions raised (`Plugin.cs`,
  the csproj, the READMEs, `dist/README.md`), the mod's Unreleased closed, the two notes (`docs/nexus/`), the two page
  texts (the Settings section now tells of the row and the options window, with two pictures to come from the
  rehearsal: `media/tech-tree-search/settings-row.png` and `options-window.png`; a part on the languages; the Mods-menu
  picture out of the text, its file kept). `build-dist.sh GK2TechTreeSearch` 0 warnings (the DLL 282,112 bytes, its
  manifest 150 entries, identical with game 1.012.9), `release-scans.sh GK2TechTreeSearch` 19 of 19 green,
  `make-release-zip.py GK2TechTreeSearch`: `release/GK2-Tech-Tree-Search-1.1.0.zip` 145,122 bytes (rebuilt 2026-11-19 on the library with the row fix below, the first one never uploaded; the DLL and 11
  language files) and the Workshop folder staged (its description 4,705 bytes).
  New: `tools/rehearsal.sh` (`stage <Mod> [--cfg <file>] [--no-framework] [--with <zip>]`, `restore`: the game folder
  as a player has it with the mod's release zip, section 5 of the checklist; every build and cfg of ours aside, a
  zip refused unless it holds `BepInEx/plugins/<folder>/` only, nothing restored over a folder that is there) and
  `tools/rehearsal-test.sh`, which proves it on a scratch folder (staged twice and restored, the folder as it was
  byte for byte: passed). The rehearsal (2026-11-18 23:10 to 2026-11-19 00:31, the user playing; the checklist's
  section 7 has it start by start): alone on no cfg, on a cfg shaped as 1.0.2's beside GK2 Saddle Up 0.17.3 and the
  Framework, and beside GK2 Saddle Up 0.17.2 (a button-era copy of the window library next to a rows one): no warning
  or error in any start, the saves identical. The page pictures are from its first start.
- **GK2 Event Decorations: the in-game test suite for its first release** (2026-11-18, the user's ask: "a solid test
  suite ... which covers everything in it"). The five read-only tests of 2026-11-17 became 45, in
  `tools/GK2QOLMods.Tests/EventDecorations/` (a file a group; `EdRun` what a run holds, `EdWorld` how the world is
  read, by the audit's own names and counts, not the mod's lists): the mod's start, patches and settings; the
  player's own settings as found; the player's own dates (what is a date, inclusive ends, past the new year); what
  the game is told for every combination of the settings (the offline truth table again, in the game, with the real
  head table, the game's expression function and the ghost's two balance rows); the decorations put up and taken
  down live, every area, kind, the bats, the glow and the blockers switched and read from the game's objects, the
  two moments of a load; the ghost's script told its events, and the mod's send-home fix; the options window
  (rows, greying, opened, a switch pressed, Defaults, its two lines); the ten languages as the deployed mod reads
  them, and glyphs in the game's own language; the Mods-menu link; a made-up later event through the mod's own
  code for found events. A run keeps the player's settings and puts them back, never writes the cfg file, turns
  the game's own date check both ways with a prefix of its own (so both seasons are tested on any date), and
  listens to the mod's log (a warning no test provoked fails the run). `docs/event-decorations-tests.md` maps each
  part of the mod to what checks it and lists what nothing reaches (the finding of a new event at the mod's start,
  a start that fails, a real save load, how things look). The test project names GK2 Event Decorations under an
  alias (`EDasm`): both mods carry the window code and grant their internals, so a window type without one would
  be two types. A test makes the state it starts from and asks nothing of the person running it (the user,
  2026-11-18: the ghost tests take a ghost that is out home themselves, the window tests wait for a free screen).
  **First run in the game (the user, 2026-11-18 16:03, game 1.012.9): 45 of 45 tests, 226 checks passed, 0 failed, 0
  warnings, none skipped**; the mod's send-home fix for the ghost seen working for the first time. Nothing changes
  for players.
  - The same evening, with the mod's two changes (Event Decorations, Unreleased): the made-up-event test became "an
    event a later game adds is left to the game: named, not switched" (the mod's list of a gates class's other
    events, asked of the game's class and of a made-up one; no patch, no cfg section, no tab for such an event); the
    language test also reads "On today" and Status in each of the ten languages (the event named as its tab,
    "Always" as the part's own choice, the switch by its label, no sentence left English); every test that reads the
    two lines' English pins English first. **Second run in the game (the user, 2026-11-18 16:59): 45 of 45 tests,
    233 checks passed, 0 failed, 0 warnings, none skipped.**
  - **Third run (17:56), on the build where a part has no "Always": 45 of 45 tests, 234 checks passed, 0 failed, 0
    warnings.** The whole suite takes about two seconds, because a setting acts within its frame and the tests look
    in that frame. What that can't see is the game undoing a switch a moment later, so a 46th test leaves the game
    running: an area, a kind, the bats, the glow and the blocking off for 3 s, everything on for 2 s, the decorations
    down for 2 s, looked at again each time. **Fourth run (18:54), with it: 46 of 46 tests, 238 checks passed, 0
    failed, 0 warnings, none skipped**; the 73 things switched off were the same 73 after 356 frames.
- **A row in the game's Settings window, where a button above its OK button was** (2026-11-18, not committed in the
  window library yet): Tech Tree Search's and Event Decorations' options open from a row each under the game's own
  rows, the mod's name and a box saying "Open" with the game's arrow, in one strip with every other mod's that uses
  the window library, in the order of their names; past eight rows the strip scrolls. The investigation and what was
  built: GK2 Donkey API's `docs/options-window.md`, section 15. `Directory.Build.props` has `GK2DreasUIDir` (the
  submodule by default) to build against the library's own checkout for a try in the game; the builds deployed for
  the first look were made so. Seen in the game (the user, 2026-11-18: both mods' rows in the main menu's Settings
  window, with Saddle Up's between them) and run: every suite in one run, 251 tests, 2475 checks passed, 0 failed, 0
  warnings, Tech Tree Search's 7 and Event Decorations' 46 tests among them.
- **Japanese, Chinese and Korean: the word in a mod's row of the game's Settings window was an empty box** (the user,
  2026-11-18, at the first rehearsal start of Tech Tree Search 1.1.0: "the three asian languages dont show the word
  Open"). The row was a copy of the game's Screen Size row, whose value the game keeps in its Latin font in every
  language (`staticFont` 1 on `ResolutionSwitchBtn/Back/Value`, read from the game's files; 0 on every other row's
  value), and that font has none of those letters. Fixed in the window library (a row whose value follows the
  language's font is copied, and the mark is taken off our copy); the mods get it with the library's pointer. The
  rows were never released from this repository; GK2 Saddle Up 0.17.3 has the fault.
- **The window library with its fonts and sizes put right** (2026-11-18, with the pointer's move): the options
  windows of Tech Tree Search and Event Decorations take their title and bold fonts from the game's own styles (in a
  session where the game's Settings window had not been shown yet, a title was drawn in a small stand-in font and
  the headings and buttons in a widened regular one), every text is at the size the game's pixel font is made for,
  and the bottom band holds four lines of a description. Found in GK2 Saddle Up 0.17.2, whose changelog and
  `docs/ui-library.md` (section 12) have the measurements; neither window of this repository has been released.
  The translation writer's room for a description is 600 characters (700); the longest shipped here is 377.
  - Found by the offline translation scan on the new lines, and fixed in the window library's own checkout (the
    mods get it when the library's pointer moves): a shipped text that quotes a name with straight quotes and has
    no comma (`status.off`, "Off: "Mod enabled" is unchecked", in four languages) was shown with its doubled quotes
    and the closing one when a translator had deleted the CSV's opening quote; such a row is now left out, with the
    warning. The library's scan with the fix passes over this repository's files and over GK2 Donkey API's (231 of
    231 catalogue cases, no failing edit). Until the pointer moves, `release-scans.sh` fails on that one scan here.
- **Game update 1.012.9 (Steam build 26734308), re-audited 2026-11-18** (`docs/game-update-26734308.md`): nothing
  the mods here use changed (Built Count, Global Storage, Tech Tree Search and Event Decorations identical on every
  member; Owned Count with its one changed member as since 1.012). Everything compiles and was rebuilt and deployed
  for the new build; scans 22 of 23, the one failure `check-dist` for Event Decorations' missing `dist/` DLL (never
  released). New in the game: its Mods window marks a Workshop code mod "Failed to start" when the plugin wrote an
  Error line or threw before the main menu, so an Error line at start is kept for a start that failed. Nothing
  changes for players; the pins move with the next release.
- In-game tests for the mods here (2026-11-17, the user's ask: only GK2 Saddle Up had automated tests in the game).
  `tools/GK2QOLMods.Tests` is a developer's plugin, never shipped (no zip, no dist, no release scan; a plain build
  doesn't deploy it), that adds a suite for each mod to GK2 Mod Test Bench, the shared test runner with its own
  repository and panel (F4). First suites, read-only but for one setting switched and put back:
  - GK2 Tech Tree Search, 7 tests: the mod running; every visible tech found by its whole name; a name pasted with
    the game's break marks found, and no result carrying a mark or a tag; hidden techs and locked tabs never found;
    the results best first; an empty query; "search what techs unlock" off.
  - GK2 Event Decorations, 5 tests: the mod running; the game's answer to "is it Halloween" as the settings say; the
    game's own dates asked past the mod; the decorations up or down in the world as the settings say; every area
    and kind with its setting.
  A mod that isn't installed has its tests skipped with that reason. The two mods grant the test plugin their
  internals by name (`TestAccess.cs` in each: `InternalsVisibleTo("GK2QOLMods.Tests")`), which is in their shipped
  DLLs from the next release on and does nothing without that plugin. `Directory.Build.props` names the bench's
  deployed DLL (`TestBenchDll`). The first run in the game (the user, 2026-11-17 20:32, English, a save with 202
  visible techs): all 12 tests passed (24 checks), with the bench's own 5: 34 passed, 0 failed, 0 warnings. 202 of 202
  visible techs found by their whole name, 0 of 25 hidden or locked ones found; the game told it isn't Halloween by
  its own dates, the decorations up as set (On).
  - What the run turned up: while the whole-name test ran (25 searches a frame), GK2 Donkey API's frame log said
    frames of about 550 ms, so a search costs somewhere near 22 ms, and the search box runs one a keystroke. The
    test now times the searches themselves and says the number in an INFO line; nothing is changed in the mod until
    that number is read.
  - The number (the second run, 2026-11-17 20:57): **202 searches took 4,810 ms, 23.8 ms a search** (a search by a
    whole name, unlocks searched, English, 202 visible techs). A frame at the game's 120 fps is 8.3 ms, so every
    keystroke in the search box costs about three frames. The unlocks test now also says how long a search takes
    over the names alone and with every unlock's header read (a query nothing matches), to tell which part it is.
    Nothing is changed in the mod yet.
- Translations, written (2026-11-17; GK2DonkeyAPI's `docs/ui-library.md` section 11.11). Both mods' texts in the
  game's 10 other languages, 93 lines a language (the window library's Status row 2, Tech Tree Search 16, Event
  Decorations 75), through the library's new `tools/l10n-write.py` (a translator's JSON checked, then written into
  the `Languages` folders; `--todo` lists what a language lacks) and `tools/l10n-terms.py` (the game's own words).
  What the round found and fixed:
  - The game's fonts decide what a translation may use, and they were read from the game's files
    (`tools/l10n-fonts.py`, into the folder of the game's word tables, never a repository). Japanese, Chinese and
    Korean: the fonts hold more than the game's own text uses (4,075 / 3,966 / 1,552 characters), so the ordinary
    words for a bat and a scarecrow are fine in Chinese. Every other language: the Latin fonts lack more than one
    expects. None of the four has the low opening quote (U+201E): the Polish example's quotes would have drawn a
    box and are straight ones now (the game's own Polish opens its quotes with a straight one for the same reason). The bold font (tabs, headings, buttons) and the
    tiny one (the search box's rows) have no curly quotes or en dash. The writer checks each text against the font
    that draws it; `tools/l10n.json` names the search box's three texts as the tiny font's (`"font"`), beside
    their room (`"room"`: 14, 34 and 34 characters).
  - The writer looked a text's room up under the wrong name, so a tab's, a title's and the search box's own limits
    were not enforced (the translators kept to the limits their work list gave them; all ten languages pass now
    that they are).
  - `tools/release-scans.sh` runs the library's new `tests/l10n-write-test.py` (the writer's checks on a scratch mod
    with made-up fonts, 13 cases); 19 of 20 green, the one failure the known one (no dist DLL for Event Decorations,
    which has no release yet).
- Translations, the pipeline (2026-11-16; the study is GK2DonkeyAPI's `docs/ui-library.md` section 11). The reader
  was in every mod that carries the window code already; what this repository lacked is now here:
  - `tools/l10n.json` names the mods with a settings page (Tech Tree Search, Event Decorations) and says their
    English comes from the build. `python -I lib/GK2DreasUI/tools/l10n-sync.py` writes each one's
    `src/<Mod>/Languages/en.csv` (the window library's `tools/EnglishDump` builds the mod's page from its DLL, no
    game start) and the window's own 21 words into its `Languages/<id>.csv`, as the library has them; `--check`
    fails when either would change. A window word is reworded in the library, never here.
  - `DeployToGame.targets` deploys a mod into a folder of its own, `BepInEx\plugins\<Mod>\` (the zips' layout), with
    its `Languages\` beside it, and removes the loose copy of the old layout. Loose in `plugins\`, every mod would
    read one shared `Languages` folder (measured: a warning for the other mod's keys, and the two mods' files have
    the same names). A mod moves at its next build; the three without a page are loose until then.
  - `tools/make-release-zip.py` puts `Languages/*.csv` into the zip and the Workshop folder, refuses uncommitted
    ones, and has `--dry-run` (every check, then what would be written; nothing written).
  - `tools/release-scans.sh` runs the sync check and the library's `tests/TranslationScan` (the translator mistake
    catalogue and every shipped file read as the game reads it) after the dist check.
- The shared dependency check (`src/Shared`, `tools/DepManifest`, byte-identical with GK2DonkeyAPI's) knows a third
  scope since game 1.011.1, the A* pathfinding, and reads `module-mvid` lines for scopes other than the two named
  ones (2026-11-03). These mods use none of it: their manifests are unchanged. `tests/GameDiff` compares every game
  assembly whose bytes changed; the Owned Count and Tech Tree Search CecilScans resolve pathfinding references too.
- No LINQ in any mod here from now on: `Directory.Build.targets` adds the banned-API analyzer to every netstandard2.1
  project (`BannedSymbols.txt`: `System.Linq.Enumerable`, `Queryable`, `ParallelEnumerable`), error RS0030 at the
  line; `tests/NoLinqScan` checks every built DLL and runs with the release scans. The .NET test tools may use it.
- `tools/game-bundles.py` (2026-11-11): read-only UnityPy reader for the game's addressable bundles and loose data
  files (container index, word search, visual-script graphs, scene content parts and configs, script classes,
  GameBalance text, and `tree`: a prefab's GameObject hierarchy with component kinds and name groups, 2026-11-13).
  Written for the seasonal-events investigation, `docs/seasonal-events.md`, behind GK2 Event Decorations.
