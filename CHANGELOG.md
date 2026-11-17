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

### Unreleased

- A settings window: the game's Settings window has a "GK2 Tech Tree Search" button opening an options window in the
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
- The settings window's own words (its buttons, the hints under the rows, the key box, the button in the game's
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

### Unreleased (1.0.0)

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
  holds). The "On today" line's value and the statuses stay English (decided 2026-11-16). Not yet seen in the game.
- The Astrologer area's description says "Inside the astrologer's tower." (it said "house"; the game's own name of
  the place is the Astrologer's Tower; found by the translators, 2026-11-17).
- With Halloween off, a ghost the game left standing in the village is sent home.
- An event a later game update adds gets its own Auto / On / Off setting by itself.
- Optional GK2 Mod Framework integration: the mod in the game's Mods menu, its Settings button opening the mod's own
  options window (below).
- Own dates for Auto (Start / End, month-day, may wrap the new year), for every event.
- Halloween's decorations, ghost and zombie head can each be set on their own (follow the event, on, off); Halloween
  set to Off turns all three off (greyed in the options window), and the areas and kinds are greyed while the
  decorations can't show.
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
